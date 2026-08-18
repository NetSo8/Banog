using System.Text.Json;
using Banog.Core.Model;
using Banog.Core.Serialization;
using Xunit;

namespace Banog.Core.Tests;

public class SerializationTests
{
    private static AppConfiguration SampleConfiguration() => new()
    {
        Folders = [new WatchedFolder { Path = @"C:\Downloads", IncludeSubfolders = true }],
        Rules =
        [
            new Rule
            {
                Name = "Factures",
                Match = ConditionMatchMode.All,
                Conditions =
                [
                    new ExtensionCondition { Extensions = ["pdf"] },
                    new NameCondition { Mode = TextMatchMode.Contains, Value = "facture" },
                    new ConditionGroup
                    {
                        Mode = ConditionMatchMode.Any,
                        Children =
                        [
                            new SizeCondition { Comparison = NumericComparison.GreaterThan, Value = 1, Unit = SizeUnit.Megabytes },
                            new DateCondition { Comparison = DateComparison.OlderThan, Amount = 7, Unit = TimeUnit.Days },
                        ],
                    },
                ],
                Actions =
                [
                    new RenameAction { Template = "{created:yyyy-MM-dd}_{name}.{ext}" },
                    new MoveAction { Destination = @"D:\Compta\{created:yyyy}" },
                ],
            },
        ],
    };

    [Fact]
    public void Round_trips_a_full_configuration()
    {
        var json = RulesJson.Serialize(SampleConfiguration());
        var restored = RulesJson.Deserialize(json);

        Assert.NotNull(restored);
        var rule = Assert.Single(restored.Rules);

        Assert.Equal("Factures", rule.Name);
        Assert.Equal(3, rule.Conditions.Count);
        Assert.IsType<ExtensionCondition>(rule.Conditions[0]);
        Assert.IsType<NameCondition>(rule.Conditions[1]);

        var group = Assert.IsType<ConditionGroup>(rule.Conditions[2]);
        Assert.Equal(2, group.Children.Count);
        Assert.IsType<SizeCondition>(group.Children[0]);
        Assert.IsType<DateCondition>(group.Children[1]);

        Assert.IsType<RenameAction>(rule.Actions[0]);
        var move = Assert.IsType<MoveAction>(rule.Actions[1]);
        Assert.Equal(@"D:\Compta\{created:yyyy}", move.Destination);
    }

    [Fact]
    public void Writes_the_discriminator_first_and_uses_readable_enum_names()
    {
        var json = RulesJson.Serialize(SampleConfiguration());

        Assert.Contains("\"type\": \"extension\"", json);
        Assert.Contains("\"type\": \"move\"", json);
        Assert.Contains("\"type\": \"group\"", json);
        // Les enums sont écrits en clair : un fichier de règles reste lisible et diffable.
        Assert.Contains("\"unit\": \"Megabytes\"", json);
    }

    [Fact]
    public void Theme_preference_round_trips()
    {
        var configuration = new AppConfiguration { Theme = ThemePreference.Light };
        var restored = RulesJson.Deserialize(RulesJson.Serialize(configuration))!;

        Assert.Equal(ThemePreference.Light, restored.Theme);
        Assert.Contains("\"theme\": \"Light\"", RulesJson.Serialize(configuration));
    }

    [Fact]
    public void A_file_written_before_the_theme_existed_falls_back_to_following_windows()
    {
        // Champ absent : pas de migration, on retombe sur le suivi du système.
        const string json = """{ "schemaVersion": 1, "folders": [], "rules": [] }""";

        var restored = RulesJson.Deserialize(json)!;
        Assert.Equal(ThemePreference.System, restored.Theme);
    }

    [Fact]
    public void Preserves_the_negation_flag()
    {
        var configuration = new AppConfiguration
        {
            Rules = [new Rule { Conditions = [new ExtensionCondition { Extensions = ["tmp"], Negate = true }] }],
        };

        var restored = RulesJson.Deserialize(RulesJson.Serialize(configuration))!;
        Assert.True(restored.Rules[0].Conditions[0].Negate);
    }

    [Fact]
    public void An_unknown_condition_type_fails_loudly_rather_than_being_dropped()
    {
        // Un fichier écrit par une version ultérieure (condition de contenu, OCR...) ne doit
        // jamais être chargé silencieusement amputé de ses règles.
        const string json = """
        {
          "schemaVersion": 1,
          "rules": [
            { "name": "R", "conditions": [ { "type": "ocrContains", "value": "SIRET" } ], "actions": [] }
          ]
        }
        """;

        Assert.Throws<JsonException>(() => RulesJson.Deserialize(json));
    }

    [Fact]
    public void Known_types_are_registered_for_every_v1_condition_and_action()
    {
        Assert.Contains(ExtensionCondition.TypeId, RuleTypeRegistry.KnownConditionTags);
        Assert.Contains(NameCondition.TypeId, RuleTypeRegistry.KnownConditionTags);
        Assert.Contains(DateCondition.TypeId, RuleTypeRegistry.KnownConditionTags);
        Assert.Contains(SizeCondition.TypeId, RuleTypeRegistry.KnownConditionTags);
        Assert.Contains(SourceFolderCondition.TypeId, RuleTypeRegistry.KnownConditionTags);
        Assert.Contains(ConditionGroup.TypeId, RuleTypeRegistry.KnownConditionTags);

        Assert.Contains(MoveAction.TypeId, RuleTypeRegistry.KnownActionTags);
        Assert.Contains(CopyAction.TypeId, RuleTypeRegistry.KnownActionTags);
        Assert.Contains(RenameAction.TypeId, RuleTypeRegistry.KnownActionTags);
        Assert.Contains(DeleteAction.TypeId, RuleTypeRegistry.KnownActionTags);
        Assert.Contains(RecycleAction.TypeId, RuleTypeRegistry.KnownActionTags);
        Assert.Contains(RunCommandAction.TypeId, RuleTypeRegistry.KnownActionTags);
    }

    [Fact]
    public async Task Store_round_trips_through_a_real_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"banog-test-{Guid.NewGuid():n}.json");
        var store = new Core.Storage.JsonConfigurationStore(path);

        try
        {
            await store.SaveAsync(SampleConfiguration());
            var restored = await store.LoadAsync();

            Assert.Single(restored.Rules);
            Assert.Single(restored.Folders);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Loading_a_missing_file_yields_an_empty_configuration()
    {
        var store = new Core.Storage.JsonConfigurationStore(
            Path.Combine(Path.GetTempPath(), $"banog-absent-{Guid.NewGuid():n}.json"));

        var configuration = await store.LoadAsync();

        Assert.Empty(configuration.Rules);
        Assert.Empty(configuration.Folders);
    }
}
