using Banog.Core.Evaluation;
using Banog.Core.Model;
using Xunit;

namespace Banog.Core.Tests;

public class ConditionTests
{
    private readonly FixedClock _clock = new(TestData.Now);

    private ValueTask<bool> Eval(RuleCondition condition, Abstractions.FileContext? file = null) =>
        ConditionDispatcher.CreateDefault(_clock).EvaluateAsync(condition, file ?? TestData.File(), default);

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("PDF", true)]
    [InlineData(".pdf", true)]
    [InlineData("png", false)]
    public async Task Extension_matches_case_insensitively_and_tolerates_leading_dot(string ext, bool expected)
    {
        var condition = new ExtensionCondition { Extensions = [ext] };
        Assert.Equal(expected, await Eval(condition));
    }

    [Fact]
    public async Task Extension_is_not_one_of_inverts_the_set()
    {
        var condition = new ExtensionCondition { Match = ExtensionMatch.IsNotOneOf, Extensions = ["png", "jpg"] };
        Assert.True(await Eval(condition));
    }

    [Fact]
    public async Task Negate_flips_any_condition()
    {
        var condition = new ExtensionCondition { Extensions = ["pdf"], Negate = true };
        Assert.False(await Eval(condition));
    }

    [Theory]
    [InlineData(TextMatchMode.Contains, "client", true)]
    [InlineData(TextMatchMode.StartsWith, "facture", true)]
    [InlineData(TextMatchMode.EndsWith, "client", true)]
    [InlineData(TextMatchMode.Equals, "facture_client", true)]
    [InlineData(TextMatchMode.Contains, "devis", false)]
    public async Task Name_matches_on_base_name(TextMatchMode mode, string value, bool expected)
    {
        var condition = new NameCondition { Target = NameTarget.BaseName, Mode = mode, Value = value };
        Assert.Equal(expected, await Eval(condition));
    }

    [Fact]
    public async Task Name_regex_matches()
    {
        var condition = new NameCondition
        {
            Target = NameTarget.FullName,
            Mode = TextMatchMode.Regex,
            Value = @"^facture_.+\.pdf$",
        };

        Assert.True(await Eval(condition));
    }

    [Fact]
    public async Task Invalid_regex_does_not_throw_and_does_not_match()
    {
        var condition = new NameCondition { Mode = TextMatchMode.Regex, Value = "[unclosed" };
        Assert.False(await Eval(condition));
    }

    [Fact]
    public async Task Name_is_case_sensitive_when_requested()
    {
        var condition = new NameCondition
        {
            Target = NameTarget.BaseName,
            Mode = TextMatchMode.Contains,
            Value = "CLIENT",
            CaseSensitive = true,
        };

        Assert.False(await Eval(condition));
    }

    [Fact]
    public async Task Date_older_than_compares_against_the_clock()
    {
        // Modifié le 10/02, horloge au 15/03 : 33 jours d'âge.
        var older = new DateCondition
        {
            Field = DateField.Modified,
            Comparison = DateComparison.OlderThan,
            Amount = 30,
            Unit = TimeUnit.Days,
        };

        var newer = new DateCondition
        {
            Field = DateField.Modified,
            Comparison = DateComparison.NewerThan,
            Amount = 30,
            Unit = TimeUnit.Days,
        };

        Assert.True(await Eval(older));
        Assert.False(await Eval(newer));
    }

    [Fact]
    public async Task Date_before_uses_the_absolute_instant()
    {
        var condition = new DateCondition
        {
            Field = DateField.Created,
            Comparison = DateComparison.Before,
            Instant = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.True(await Eval(condition));
    }

    [Theory]
    [InlineData(NumericComparison.GreaterThan, 1, SizeUnit.Kilobytes, true)]
    [InlineData(NumericComparison.LessThan, 1, SizeUnit.Kilobytes, false)]
    [InlineData(NumericComparison.EqualTo, 2, SizeUnit.Kilobytes, true)]
    [InlineData(NumericComparison.GreaterThan, 1, SizeUnit.Megabytes, false)]
    public async Task Size_compares_in_the_requested_unit(
        NumericComparison comparison, double value, SizeUnit unit, bool expected)
    {
        var condition = new SizeCondition { Comparison = comparison, Value = value, Unit = unit };
        Assert.Equal(expected, await Eval(condition));
    }

    [Fact]
    public async Task Source_folder_can_require_an_exact_folder()
    {
        var file = TestData.File(@"C:\Downloads\factures\a.pdf");

        var exact = new SourceFolderCondition { Path = @"C:\Downloads", IncludeSubfolders = false };
        var recursive = new SourceFolderCondition { Path = @"C:\Downloads", IncludeSubfolders = true };

        Assert.False(await Eval(exact, file));
        Assert.True(await Eval(recursive, file));
    }

    [Fact]
    public async Task Source_folder_does_not_match_a_sibling_with_a_common_prefix()
    {
        var file = TestData.File(@"C:\Downloads2\a.pdf");
        var condition = new SourceFolderCondition { Path = @"C:\Downloads", IncludeSubfolders = true };

        Assert.False(await Eval(condition, file));
    }

    [Fact]
    public async Task Group_applies_and_or_semantics()
    {
        var pdf = new ExtensionCondition { Extensions = ["pdf"] };
        var png = new ExtensionCondition { Extensions = ["png"] };

        var all = new ConditionGroup { Mode = ConditionMatchMode.All, Children = [pdf, png] };
        var any = new ConditionGroup { Mode = ConditionMatchMode.Any, Children = [pdf, png] };

        Assert.False(await Eval(all));
        Assert.True(await Eval(any));
    }

    [Fact]
    public async Task Groups_nest()
    {
        var inner = new ConditionGroup
        {
            Mode = ConditionMatchMode.Any,
            Children =
            [
                new ExtensionCondition { Extensions = ["png"] },
                new NameCondition { Mode = TextMatchMode.Contains, Value = "facture" },
            ],
        };

        var outer = new ConditionGroup
        {
            Mode = ConditionMatchMode.All,
            Children = [inner, new SizeCondition { Comparison = NumericComparison.GreaterThan, Value = 1, Unit = SizeUnit.Kilobytes }],
        };

        Assert.True(await Eval(outer));
    }
}
