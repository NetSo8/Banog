using System.Text.Json;
using System.Text.Json.Serialization;
using Banog.Core.Model;

namespace Banog.Core.Serialization;

/// <summary>
/// Convertisseur polymorphe piloté par <see cref="RuleTypeRegistry"/>.
/// On n'utilise pas <c>[JsonDerivedType]</c> : la liste des types dérivés serait figée
/// à la compilation d'App.Core, ce qui empêcherait un module externe (OCR, IA) d'ajouter
/// ses propres conditions sans toucher au coeur.
/// </summary>
internal abstract class RegistryPolymorphicConverter<TBase> : JsonConverter<TBase> where TBase : class
{
    protected abstract bool TryResolve(string tag, out System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo);
    protected abstract bool TryResolve(Type type, out System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo);
    protected abstract string Kind { get; }

    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{Kind} sans discriminant \"type\".");
        }

        var tag = tagElement.GetString()!;
        if (!TryResolve(tag, out var typeInfo) || typeInfo is null)
        {
            throw new JsonException(
                $"{Kind} de type \"{tag}\" inconnu. Ce fichier de règles a probablement été écrit " +
                $"par une version plus récente, ou un module n'est pas chargé.");
        }

        return (TBase?)JsonSerializer.Deserialize(root.GetRawText(), typeInfo);
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        var runtimeType = value.GetType();
        if (!TryResolve(runtimeType, out var typeInfo) || typeInfo is null)
        {
            throw new JsonException($"{Kind} {runtimeType.Name} non enregistré dans RuleTypeRegistry.");
        }

        JsonSerializer.Serialize(writer, value, typeInfo);
    }
}

internal sealed class RuleConditionConverter : RegistryPolymorphicConverter<RuleCondition>
{
    protected override string Kind => "Condition";

    protected override bool TryResolve(string tag, out System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo)
        => RuleTypeRegistry.TryResolveCondition(tag, out typeInfo);

    protected override bool TryResolve(Type type, out System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo)
        => RuleTypeRegistry.TryResolveCondition(type, out typeInfo);
}

internal sealed class RuleActionConverter : RegistryPolymorphicConverter<RuleAction>
{
    protected override string Kind => "Action";

    protected override bool TryResolve(string tag, out System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo)
        => RuleTypeRegistry.TryResolveAction(tag, out typeInfo);

    protected override bool TryResolve(Type type, out System.Text.Json.Serialization.Metadata.JsonTypeInfo? typeInfo)
        => RuleTypeRegistry.TryResolveAction(type, out typeInfo);
}
