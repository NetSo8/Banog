using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Banog.Core.Model;

namespace Banog.Core.Serialization;

/// <summary>
/// Table discriminant &lt;-&gt; type concret, pour les conditions et les actions.
///
/// C'est le point d'extension du format : un module futur (conditions de contenu,
/// OCR, classification IA) enregistre ses propres types avec son propre
/// <see cref="JsonTypeInfo"/> généré, sans modifier ni recompiler App.Core, et sans
/// invalider les fichiers de règles déjà écrits.
/// </summary>
public static class RuleTypeRegistry
{
    private static readonly Dictionary<string, JsonTypeInfo> ConditionsByTag = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, JsonTypeInfo> ConditionsByType = [];
    private static readonly Dictionary<string, JsonTypeInfo> ActionsByTag = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, JsonTypeInfo> ActionsByType = [];
    private static readonly Lock Gate = new();

    static RuleTypeRegistry()
    {
        var d = RulesJsonContext.Default;

        RegisterCondition(ConditionGroup.TypeId, d.ConditionGroup);
        RegisterCondition(ExtensionCondition.TypeId, d.ExtensionCondition);
        RegisterCondition(NameCondition.TypeId, d.NameCondition);
        RegisterCondition(DateCondition.TypeId, d.DateCondition);
        RegisterCondition(SizeCondition.TypeId, d.SizeCondition);
        RegisterCondition(SourceFolderCondition.TypeId, d.SourceFolderCondition);

        RegisterAction(MoveAction.TypeId, d.MoveAction);
        RegisterAction(CopyAction.TypeId, d.CopyAction);
        RegisterAction(RenameAction.TypeId, d.RenameAction);
        RegisterAction(DeleteAction.TypeId, d.DeleteAction);
        RegisterAction(RecycleAction.TypeId, d.RecycleAction);
        RegisterAction(RunCommandAction.TypeId, d.RunCommandAction);
    }

    public static void RegisterCondition<T>(string tag, JsonTypeInfo<T> typeInfo) where T : RuleCondition
    {
        lock (Gate)
        {
            ConditionsByTag[tag] = typeInfo;
            ConditionsByType[typeof(T)] = typeInfo;
        }
    }

    public static void RegisterAction<T>(string tag, JsonTypeInfo<T> typeInfo) where T : RuleAction
    {
        lock (Gate)
        {
            ActionsByTag[tag] = typeInfo;
            ActionsByType[typeof(T)] = typeInfo;
        }
    }

    public static bool TryResolveCondition(string tag, [NotNullWhen(true)] out JsonTypeInfo? typeInfo)
    {
        lock (Gate) { return ConditionsByTag.TryGetValue(tag, out typeInfo); }
    }

    public static bool TryResolveCondition(Type type, [NotNullWhen(true)] out JsonTypeInfo? typeInfo)
    {
        lock (Gate) { return ConditionsByType.TryGetValue(type, out typeInfo); }
    }

    public static bool TryResolveAction(string tag, [NotNullWhen(true)] out JsonTypeInfo? typeInfo)
    {
        lock (Gate) { return ActionsByTag.TryGetValue(tag, out typeInfo); }
    }

    public static bool TryResolveAction(Type type, [NotNullWhen(true)] out JsonTypeInfo? typeInfo)
    {
        lock (Gate) { return ActionsByType.TryGetValue(type, out typeInfo); }
    }

    public static IReadOnlyCollection<string> KnownConditionTags
    {
        get { lock (Gate) { return [.. ConditionsByTag.Keys]; } }
    }

    public static IReadOnlyCollection<string> KnownActionTags
    {
        get { lock (Gate) { return [.. ActionsByTag.Keys]; } }
    }
}
