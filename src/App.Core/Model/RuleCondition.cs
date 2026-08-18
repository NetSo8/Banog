using System.Text.Json.Serialization;

namespace Banog.Core.Model;

/// <summary>
/// Base de toutes les conditions. Le discriminant <see cref="Type"/> est sérialisé en
/// première position et sert de point d'extension : un futur type de condition
/// (contenu, OCR, classification IA) s'ajoute en déclarant un nouveau discriminant,
/// sans casser les fichiers de règles existants.
/// </summary>
[JsonConverter(typeof(Serialization.RuleConditionConverter))]
public abstract class RuleCondition
{
    /// <summary>Discriminant stable, écrit tel quel dans le JSON. Ne jamais renommer.</summary>
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-100)]
    public abstract string Type { get; }

    /// <summary>Inverse le résultat de la condition.</summary>
    [JsonPropertyOrder(-99)]
    public bool Negate { get; set; }
}

public enum ConditionMatchMode
{
    All = 0,
    Any = 1,
}

/// <summary>Groupe composable ET/OU. Permet des arbres de conditions imbriqués.</summary>
public sealed class ConditionGroup : RuleCondition
{
    public const string TypeId = "group";
    public override string Type => TypeId;

    public ConditionMatchMode Mode { get; set; } = ConditionMatchMode.All;
    public List<RuleCondition> Children { get; set; } = [];
}

public enum ExtensionMatch
{
    IsOneOf = 0,
    IsNotOneOf = 1,
}

/// <summary>Extension de fichier, sans le point, comparaison insensible à la casse.</summary>
public sealed class ExtensionCondition : RuleCondition
{
    public const string TypeId = "extension";
    public override string Type => TypeId;

    public ExtensionMatch Match { get; set; } = ExtensionMatch.IsOneOf;
    public List<string> Extensions { get; set; } = [];
}

public enum TextMatchMode
{
    Contains = 0,
    StartsWith = 1,
    EndsWith = 2,
    Equals = 3,
    Regex = 4,
}

public enum NameTarget
{
    /// <summary>Nom sans extension.</summary>
    BaseName = 0,
    /// <summary>Nom complet avec extension.</summary>
    FullName = 1,
}

public sealed class NameCondition : RuleCondition
{
    public const string TypeId = "name";
    public override string Type => TypeId;

    public NameTarget Target { get; set; } = NameTarget.FullName;
    public TextMatchMode Mode { get; set; } = TextMatchMode.Contains;
    public string Value { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
}

public enum DateField
{
    Created = 0,
    Modified = 1,
}

public enum DateComparison
{
    /// <summary>Plus ancien que N unités (âge > seuil).</summary>
    OlderThan = 0,
    /// <summary>Plus récent que N unités (âge &lt; seuil).</summary>
    NewerThan = 1,
    Before = 2,
    After = 3,
}

public enum TimeUnit
{
    Minutes = 0,
    Hours = 1,
    Days = 2,
    Weeks = 3,
}

public sealed class DateCondition : RuleCondition
{
    public const string TypeId = "date";
    public override string Type => TypeId;

    public DateField Field { get; set; } = DateField.Modified;
    public DateComparison Comparison { get; set; } = DateComparison.OlderThan;

    /// <summary>Utilisé par <see cref="DateComparison.OlderThan"/> / <see cref="DateComparison.NewerThan"/>.</summary>
    public double Amount { get; set; }
    public TimeUnit Unit { get; set; } = TimeUnit.Days;

    /// <summary>Utilisé par <see cref="DateComparison.Before"/> / <see cref="DateComparison.After"/>.</summary>
    public DateTimeOffset? Instant { get; set; }
}

public enum NumericComparison
{
    GreaterThan = 0,
    LessThan = 1,
    EqualTo = 2,
}

public enum SizeUnit
{
    Bytes = 0,
    Kilobytes = 1,
    Megabytes = 2,
    Gigabytes = 3,
}

public sealed class SizeCondition : RuleCondition
{
    public const string TypeId = "size";
    public override string Type => TypeId;

    public NumericComparison Comparison { get; set; } = NumericComparison.GreaterThan;
    public double Value { get; set; }
    public SizeUnit Unit { get; set; } = SizeUnit.Megabytes;

    public long ToBytes() => (long)(Value * Unit switch
    {
        SizeUnit.Bytes => 1L,
        SizeUnit.Kilobytes => 1024L,
        SizeUnit.Megabytes => 1024L * 1024,
        SizeUnit.Gigabytes => 1024L * 1024 * 1024,
        _ => 1L,
    });
}

/// <summary>Dossier d'origine du fichier (utile quand une règle couvre plusieurs dossiers surveillés).</summary>
public sealed class SourceFolderCondition : RuleCondition
{
    public const string TypeId = "sourceFolder";
    public override string Type => TypeId;

    public string Path { get; set; } = string.Empty;
    /// <summary>Si vrai, les sous-dossiers de <see cref="Path"/> satisfont aussi la condition.</summary>
    public bool IncludeSubfolders { get; set; } = true;
}
