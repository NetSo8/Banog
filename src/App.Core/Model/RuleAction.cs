using System.Text.Json.Serialization;

namespace Banog.Core.Model;

/// <summary>
/// Base de toutes les actions. Même mécanique de discriminant que
/// <see cref="RuleCondition"/> : format extensible sans rupture.
/// </summary>
[JsonConverter(typeof(Serialization.RuleActionConverter))]
public abstract class RuleAction
{
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-100)]
    public abstract string Type { get; }
}

/// <summary>Que faire si la cible existe déjà.</summary>
public enum ConflictPolicy
{
    /// <summary>Ajoute un suffixe numérique ( (1), (2), ... ).</summary>
    Rename = 0,
    Overwrite = 1,
    Skip = 2,
}

public sealed class MoveAction : RuleAction
{
    public const string TypeId = "move";
    public override string Type => TypeId;

    /// <summary>Dossier de destination. Supporte les tokens (ex. <c>D:\Archive\{modified:yyyy}\{modified:MM}</c>).</summary>
    public string Destination { get; set; } = string.Empty;
    public bool CreateDirectories { get; set; } = true;
    public ConflictPolicy OnConflict { get; set; } = ConflictPolicy.Rename;
}

public sealed class CopyAction : RuleAction
{
    public const string TypeId = "copy";
    public override string Type => TypeId;

    public string Destination { get; set; } = string.Empty;
    public bool CreateDirectories { get; set; } = true;
    public ConflictPolicy OnConflict { get; set; } = ConflictPolicy.Rename;
}

public sealed class RenameAction : RuleAction
{
    public const string TypeId = "rename";
    public override string Type => TypeId;

    /// <summary>
    /// Gabarit du nouveau nom, tokens compris.
    /// Ex. <c>{created:yyyy-MM-dd}_{name}_{counter:000}.{ext}</c>
    /// </summary>
    public string Template { get; set; } = "{name}.{ext}";
    public ConflictPolicy OnConflict { get; set; } = ConflictPolicy.Rename;
}

public sealed class DeleteAction : RuleAction
{
    public const string TypeId = "delete";
    public override string Type => TypeId;

    /// <summary>Corbeille par défaut. <c>false</c> = suppression définitive.</summary>
    public bool UseRecycleBin { get; set; } = true;
}

public sealed class RunCommandAction : RuleAction
{
    public const string TypeId = "runCommand";
    public override string Type => TypeId;

    public string Executable { get; set; } = string.Empty;
    /// <summary>Arguments, tokens compris (ex. <c>"{path}"</c>).</summary>
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public bool WaitForExit { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;
}
