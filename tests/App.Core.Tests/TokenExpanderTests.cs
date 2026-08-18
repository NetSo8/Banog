using Banog.Core.Execution;
using Xunit;

namespace Banog.Core.Tests;

public class TokenExpanderTests
{
    private static string Expand(string template, int counter = 1) =>
        TokenExpander.Expand(template, TestData.File(), TestData.Now, counter);

    [Fact]
    public void Expands_name_and_extension()
    {
        Assert.Equal("facture_client.pdf", Expand("{name}.{ext}"));
        Assert.Equal("facture_client.pdf", Expand("{filename}"));
    }

    [Fact]
    public void Expands_parent_folder_name()
    {
        Assert.Equal("Downloads", Expand("{folder}"));
    }

    // Les dates sont développées en heure locale : un fichier modifié à 23h30 doit
    // tomber dans la journée que l'utilisateur voit dans l'explorateur. Les attendus
    // sont donc calculés, pas codés en dur.
    [Fact]
    public void Uses_the_default_date_format_when_none_is_given()
    {
        var expected = TestData.File().CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd");
        Assert.Equal(expected, Expand("{created}"));
    }

    [Fact]
    public void Honours_a_custom_date_format()
    {
        var expected = TestData.File().ModifiedUtc.ToLocalTime().ToString("yyyy-MM");
        Assert.Equal(expected, Expand("{modified:yyyy-MM}"));
    }

    [Fact]
    public void Pads_the_counter_when_a_format_is_given()
    {
        Assert.Equal("007", Expand("{counter:000}", counter: 7));
        Assert.Equal("7", Expand("{counter}", counter: 7));
    }

    [Fact]
    public void Escapes_double_braces()
    {
        Assert.Equal("{name}", Expand("{{name}}"));
    }

    [Fact]
    public void Leaves_unknown_tokens_untouched()
    {
        Assert.Equal("{unknown}", Expand("{unknown}"));
    }

    [Fact]
    public void Detects_the_counter_token()
    {
        Assert.True(TokenExpander.ContainsCounter("{name}_{counter:00}"));
        Assert.True(TokenExpander.ContainsCounter("{nom}_{compteur:00}"));
        Assert.False(TokenExpander.ContainsCounter("{name}"));
    }

    // Les alias français existent pour que l'interface n'oblige pas à écrire en anglais.
    // Les noms anglais restent valides : les fichiers de règles déjà écrits ne bougent pas.
    [Theory]
    [InlineData("{nom}", "{name}")]
    [InlineData("{extension}", "{ext}")]
    [InlineData("{fichier}", "{filename}")]
    [InlineData("{dossier}", "{folder}")]
    [InlineData("{chemin}", "{path}")]
    [InlineData("{compteur:000}", "{counter:000}")]
    [InlineData("{date:yyyy-MM}", "{created:yyyy-MM}")]
    [InlineData("{creation}", "{created}")]
    [InlineData("{modification:yyyy}", "{modified:yyyy}")]
    public void French_aliases_match_their_english_counterpart(string french, string english)
    {
        Assert.Equal(Expand(english, counter: 7), Expand(french, counter: 7));
    }

    [Fact]
    public void Aliases_are_case_insensitive_too()
    {
        Assert.Equal(Expand("{nom}"), Expand("{Nom}"));
        Assert.Equal(Expand("{extension}"), Expand("{EXTENSION}"));
    }

    [Fact]
    public void A_realistic_french_template_produces_the_expected_name()
    {
        var day = TestData.File().CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd");
        Assert.Equal($"{day}_facture_client.pdf", Expand("{date}_{nom}.{extension}"));
    }

    [Fact]
    public void Sanitizes_characters_forbidden_in_file_names()
    {
        var file = TestData.File(@"C:\Downloads\a.pdf");
        var expanded = TokenExpander.ExpandFileName("{path}", file, TestData.Now);

        Assert.DoesNotContain(':', expanded);
        Assert.DoesNotContain('\\', expanded);
    }

    [Fact]
    public void Combines_tokens_in_a_realistic_template()
    {
        var day = TestData.File().ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");

        Assert.Equal(
            $"{day}_facture_client_001.pdf",
            Expand("{modified:yyyy-MM-dd}_{name}_{counter:000}.{ext}"));
    }
}
