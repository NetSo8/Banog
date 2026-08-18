using Banog.Core.Engine;
using Banog.Core.Execution;
using Banog.Core.Model;
using Xunit;

namespace Banog.Core.Tests;

public class RuleEngineTests
{
    private readonly FakeFileSystem _fs = new();
    private readonly RecordingProcessRunner _runner = new();
    private readonly FixedClock _clock = new(TestData.Now);

    private RuleEngine CreateEngine() => RuleEngine.CreateDefault(_fs, _runner, _clock);

    private static Rule PdfRule(params RuleAction[] actions) => new()
    {
        Name = "PDF",
        Conditions = [new ExtensionCondition { Extensions = ["pdf"] }],
        Actions = [.. actions],
    };

    [Fact]
    public async Task Move_relocates_the_file_and_creates_the_destination()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new MoveAction { Destination = @"D:\Archives\{modified:yyyy}" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        var year = TestData.File().ModifiedUtc.ToLocalTime().Year;
        var expected = $@"D:\Archives\{year}\facture_client.pdf";

        Assert.True(report.AnyRuleMatched);
        Assert.Equal(expected, report.FinalPath);
        Assert.True(_fs.FileExists(expected));
        Assert.False(_fs.FileExists(@"C:\Downloads\facture_client.pdf"));
    }

    [Fact]
    public async Task Move_renames_on_conflict_by_default()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");
        _fs.AddFile(@"D:\Archives\facture_client.pdf");

        var rule = PdfRule(new MoveAction { Destination = @"D:\Archives" });
        await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.True(_fs.FileExists(@"D:\Archives\facture_client (1).pdf"));
    }

    [Fact]
    public async Task Move_skips_when_the_policy_says_so()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");
        _fs.AddFile(@"D:\Archives\facture_client.pdf");

        var rule = PdfRule(new MoveAction { Destination = @"D:\Archives", OnConflict = ConflictPolicy.Skip });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.True(_fs.FileExists(@"C:\Downloads\facture_client.pdf"));
        Assert.Equal(ActionStatus.Skipped, report.Rules[0].Actions[0].Status);
    }

    [Fact]
    public async Task Rename_then_move_chain_on_the_current_path()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(
            new RenameAction { Template = "{name}_archive.{ext}" },
            new MoveAction { Destination = @"D:\Archives" });

        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Equal(@"D:\Archives\facture_client_archive.pdf", report.FinalPath);
        Assert.True(_fs.FileExists(@"D:\Archives\facture_client_archive.pdf"));
    }

    [Fact]
    public async Task Rename_keeps_the_original_extension()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new RenameAction { Template = "{name}_archive.txt" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Equal(@"C:\Downloads\facture_client_archive.pdf", report.FinalPath);
        Assert.True(_fs.FileExists(@"C:\Downloads\facture_client_archive.pdf"));
        Assert.False(_fs.FileExists(@"C:\Downloads\facture_client_archive.txt"));
    }

    [Fact]
    public async Task Copy_leaves_the_original_in_place()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new CopyAction { Destination = @"D:\Sauvegarde" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.True(_fs.FileExists(@"C:\Downloads\facture_client.pdf"));
        Assert.True(_fs.FileExists(@"D:\Sauvegarde\facture_client.pdf"));
        Assert.Equal(@"C:\Downloads\facture_client.pdf", report.FinalPath);
    }

    [Fact]
    public async Task Delete_uses_the_recycle_bin_by_default()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new DeleteAction());
        await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Contains(@"C:\Downloads\facture_client.pdf", _fs.RecycledPaths);
        Assert.Empty(_fs.DeletedPaths);
    }

    [Fact]
    public async Task Permanent_delete_bypasses_the_recycle_bin()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new DeleteAction { UseRecycleBin = false });
        await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Contains(@"C:\Downloads\facture_client.pdf", _fs.DeletedPaths);
        Assert.Empty(_fs.RecycledPaths);
    }

    [Fact]
    public async Task Actions_after_a_delete_are_skipped()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new DeleteAction(), new MoveAction { Destination = @"D:\Archives" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Equal(ActionStatus.Skipped, report.Rules[0].Actions[1].Status);
    }

    [Fact]
    public async Task Run_command_expands_tokens_in_its_arguments()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new RunCommandAction { Executable = "cmd.exe", Arguments = "/c echo {name}" });
        await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Single(_runner.Calls);
        Assert.Equal(["/c", "echo", "facture_client"], _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task A_quoted_token_stays_one_argument_even_with_spaces()
    {
        _fs.AddFile(@"C:\Downloads\rapport final.pdf");

        var rule = PdfRule(new RunCommandAction { Executable = "cmd.exe", Arguments = "/c type \"{path}\"" });
        await CreateEngine().ProcessAsync(TestData.File(@"C:\Downloads\rapport final.pdf"), [rule]);

        Assert.Equal(["/c", "type", @"C:\Downloads\rapport final.pdf"], _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task A_non_zero_exit_code_fails_the_action()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");
        _runner.ExitCode = 2;

        var rule = PdfRule(new RunCommandAction { Executable = "cmd.exe" });
        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Equal(ActionStatus.Failed, report.Rules[0].Actions[0].Status);
    }

    [Fact]
    public async Task Rules_run_in_order_and_stop_after_the_first_match_by_default()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var first = PdfRule(new CopyAction { Destination = @"D:\Un" });
        first.Order = 0;

        var second = PdfRule(new CopyAction { Destination = @"D:\Deux" });
        second.Order = 1;

        await CreateEngine().ProcessAsync(TestData.File(), [second, first]);

        Assert.True(_fs.FileExists(@"D:\Un\facture_client.pdf"));
        Assert.False(_fs.FileExists(@"D:\Deux\facture_client.pdf"));
    }

    [Fact]
    public async Task Processing_continues_when_the_rule_does_not_stop_the_chain()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var first = PdfRule(new CopyAction { Destination = @"D:\Un" });
        first.StopProcessingOnMatch = false;

        var second = PdfRule(new CopyAction { Destination = @"D:\Deux" });
        second.Order = 1;

        await CreateEngine().ProcessAsync(TestData.File(), [first, second]);

        Assert.True(_fs.FileExists(@"D:\Un\facture_client.pdf"));
        Assert.True(_fs.FileExists(@"D:\Deux\facture_client.pdf"));
    }

    [Fact]
    public async Task Disabled_rules_are_ignored()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(new CopyAction { Destination = @"D:\Un" });
        rule.Enabled = false;

        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.False(report.AnyRuleMatched);
        Assert.Empty(_fs.Copies);
    }

    [Fact]
    public async Task A_rule_without_conditions_never_matches()
    {
        var rule = new Rule { Name = "vide", Actions = [new DeleteAction()] };
        Assert.False(await CreateEngine().MatchesAsync(rule, TestData.File()));
    }

    [Fact]
    public async Task Any_mode_matches_on_a_single_satisfied_condition()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = new Rule
        {
            Match = ConditionMatchMode.Any,
            Conditions =
            [
                new ExtensionCondition { Extensions = ["png"] },
                new NameCondition { Mode = TextMatchMode.Contains, Value = "facture" },
            ],
            Actions = [new CopyAction { Destination = @"D:\Un" }],
        };

        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);
        Assert.True(report.AnyRuleMatched);
    }

    [Fact]
    public async Task A_failing_action_stops_the_remaining_actions_of_that_rule()
    {
        _fs.AddFile(@"C:\Downloads\facture_client.pdf");

        var rule = PdfRule(
            new MoveAction { Destination = string.Empty },
            new CopyAction { Destination = @"D:\Deux" });

        var report = await CreateEngine().ProcessAsync(TestData.File(), [rule]);

        Assert.Equal(ActionStatus.Failed, report.Rules[0].Actions[0].Status);
        Assert.Single(report.Rules[0].Actions);
    }
}
