using Banog.Core.Engine;
using Banog.Core.Execution;
using Banog.Core.Model;
using Xunit;

namespace Banog.Core.Tests;

/// <summary>
/// Tests de non-régression sur la frontière de confiance.
///
/// Le gabarit d'une règle est écrit par l'utilisateur : c'est une donnée de confiance.
/// Le nom du fichier traité ne l'est pas — il vient de ce que quelqu'un a déposé dans le
/// dossier surveillé (un téléchargement, une pièce jointe, un partage réseau). Tout ce qui
/// suit vérifie qu'une valeur substituée ne peut pas changer la nature de l'opération.
/// </summary>
public class SecurityTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly RecordingProcessRunner _runner = new();
    private readonly FixedClock _clock = new(TestData.Now);

    private RuleEngine CreateEngine() => RuleEngine.CreateDefault(_fs, _runner, _clock);

    private static Rule AllPdf(params RuleAction[] actions) => new()
    {
        Name = "test",
        Conditions = [new ExtensionCondition { Extensions = ["pdf"] }],
        Actions = [.. actions],
    };

    // ---- Injection d'arguments de commande -------------------------------------------

    [Fact]
    public async Task A_shell_metacharacter_in_a_file_name_stays_one_literal_argument()
    {
        // « & » et « ^ » sont des caractères parfaitement valides dans un nom de fichier
        // Windows. Concaténés dans une ligne de commande, ils enchaîneraient une seconde
        // commande. (Pas de « / » ici : ce n'est pas un caractère de nom valide, et un
        // chemin le lit comme un séparateur.)
        const string path = @"C:\Downloads\rapport & start calc ^& x.pdf";
        _fs.AddFile(path);

        var rule = AllPdf(new RunCommandAction { Executable = "cmd.exe", Arguments = "/c echo {name}" });
        await CreateEngine().ProcessAsync(TestData.File(path), [rule]);

        var arguments = _runner.Calls[0].Arguments;

        Assert.Equal(3, arguments.Length);
        Assert.Equal("rapport & start calc ^& x", arguments[2]);
    }

    [Fact]
    public async Task A_file_name_cannot_add_an_argument()
    {
        // Le découpage porte sur le gabarit, pas sur le résultat : quels que soient les
        // espaces du nom, le nombre d'arguments reste celui écrit dans la règle.
        _fs.AddFile(@"C:\Downloads\a b c d e.pdf");

        var rule = AllPdf(new RunCommandAction { Executable = "cmd.exe", Arguments = "/c echo {name}" });
        await CreateEngine().ProcessAsync(TestData.File(@"C:\Downloads\a b c d e.pdf"), [rule]);

        Assert.Equal(["/c", "echo", "a b c d e"], _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task A_file_name_cannot_choose_the_executable()
    {
        _fs.AddFile(@"C:\Downloads\x.pdf");

        var rule = AllPdf(new RunCommandAction { Executable = "{name}.exe", Arguments = string.Empty });
        await CreateEngine().ProcessAsync(TestData.File(@"C:\Downloads\x.pdf"), [rule]);

        // L'exécutable est traité en segment unique : pas de chemin injectable par le nom.
        Assert.Equal("x.exe", _runner.Calls[0].Executable);
    }

    // ---- Évasion de chemin -----------------------------------------------------------

    [Fact]
    public void A_token_value_cannot_introduce_a_path_separator()
    {
        // {path} contient des séparateurs et une lettre de lecteur. En contexte chemin il
        // est aplati en un seul segment, sinon Path.Join avec une valeur enracinée
        // écraserait purement et simplement le dossier de destination.
        var expanded = TokenExpander.Expand(
            @"D:\Archives\{path}", TestData.File(), TestData.Now, 1, TokenScope.Path);

        Assert.StartsWith(@"D:\Archives\", expanded);
        Assert.DoesNotContain(@"C:\", expanded);
        Assert.Equal(2, expanded.Count(c => c == '\\'));
    }

    [Fact]
    public void A_token_value_reduced_to_dot_dot_is_neutralised()
    {
        var file = TestData.File(@"C:\Downloads\..");

        var expanded = TokenExpander.Expand(
            @"D:\Archives\{name}\x", file, TestData.Now, 1, TokenScope.Path);

        Assert.Equal(@"D:\Archives\_\x", expanded);
    }

    [Fact]
    public async Task A_destination_that_expands_to_a_relative_path_is_refused()
    {
        // Un chemin relatif dépendrait du répertoire courant du processus, que l'utilisateur
        // ne contrôle pas et ne voit pas.
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = AllPdf(new MoveAction { Destination = @"Archives\{name}" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Equal(ActionStatus.Failed, report.Rules[0].Actions[0].Status);
        Assert.True(_fs.FileExists(@"C:\Downloads\facture_client.pdf"));
    }

    [Fact]
    public void A_rename_template_cannot_move_the_file_to_another_folder()
    {
        var expanded = TokenExpander.ExpandFileName("{path}", TestData.File(), TestData.Now);

        Assert.False(Path.IsPathRooted(expanded));
        Assert.DoesNotContain('\\', expanded);
        Assert.DoesNotContain(':', expanded);
    }

    [Fact]
    public async Task Rename_keeps_the_file_in_its_own_directory()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = AllPdf(new RenameAction { Template = @"{path}.pdf" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.StartsWith(@"C:\Downloads\", report.FinalPath);
        Assert.Equal(@"C:\Downloads", Path.GetDirectoryName(report.FinalPath));
    }

    // ---- Robustesse de l'évaluation --------------------------------------------------

    [Fact]
    public async Task A_catastrophic_regex_cannot_hang_the_engine()
    {
        // Motif à explosion combinatoire sur une entrée non maîtrisée : le délai maximal
        // du moteur d'expressions régulières borne le coût.
        var name = new string('a', 60) + "!";
        var path = $@"C:\Downloads\{name}.pdf";
        _fs.AddFile(path);

        var rule = new Rule
        {
            Name = "regex",
            Conditions = [new NameCondition { Mode = TextMatchMode.Regex, Value = "^(a+)+$" }],
            Actions = [new DeleteAction()],
        };

        var start = System.Diagnostics.Stopwatch.StartNew();
        var report = await CreateEngine().ProcessAsync(TestData.File(path), [rule]);
        start.Stop();

        Assert.False(report.AnyRuleMatched);
        Assert.True(start.Elapsed < TimeSpan.FromSeconds(3), $"évaluation trop longue : {start.Elapsed}");
        Assert.True(_fs.FileExists(path));
    }

    [Fact]
    public void A_value_longer_than_the_stack_buffer_is_still_expanded_correctly()
    {
        // Le tampon de pile fait 320 caractères ; au-delà on bascule sur ArrayPool.
        // Ce chemin de débordement doit produire exactement le même résultat.
        var longName = new string('x', 900);
        var file = TestData.File($@"C:\Downloads\{longName}.pdf");

        var expanded = TokenExpander.Expand("{name}.{ext}", file, TestData.Now, 1, TokenScope.Raw);

        Assert.Equal($"{longName}.pdf", expanded);
    }

    [Fact]
    public void Command_template_splitting_handles_quotes_and_extra_whitespace()
    {
        Assert.Equal(["/c", "type", "a b"], CommandLineTemplate.Split("  /c   type  \"a b\"  "));
        Assert.Empty(CommandLineTemplate.Split("   "));
        Assert.Equal(["ab"], CommandLineTemplate.Split("\"a\"\"b\""));
    }
}
