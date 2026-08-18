using System.Text.Json;
using System.Text.Json.Serialization;
using Banog.Core.Model;

namespace Banog.Core.Serialization;

/// <summary>
/// Contexte System.Text.Json généré à la compilation (source generator).
/// Aucune réflexion à l'exécution : indispensable pour Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfiguration))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(WatchedFolder))]
[JsonSerializable(typeof(List<RuleCondition>))]
[JsonSerializable(typeof(List<RuleAction>))]
// Conditions v1
[JsonSerializable(typeof(ConditionGroup))]
[JsonSerializable(typeof(ExtensionCondition))]
[JsonSerializable(typeof(NameCondition))]
[JsonSerializable(typeof(DateCondition))]
[JsonSerializable(typeof(SizeCondition))]
[JsonSerializable(typeof(SourceFolderCondition))]
// Actions v1
[JsonSerializable(typeof(MoveAction))]
[JsonSerializable(typeof(CopyAction))]
[JsonSerializable(typeof(RenameAction))]
[JsonSerializable(typeof(DeleteAction))]
[JsonSerializable(typeof(RunCommandAction))]
public sealed partial class RulesJsonContext : JsonSerializerContext
{
}

public static class RulesJson
{
    public static string Serialize(AppConfiguration configuration) =>
        JsonSerializer.Serialize(configuration, RulesJsonContext.Default.AppConfiguration);

    public static AppConfiguration? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, RulesJsonContext.Default.AppConfiguration);
}
