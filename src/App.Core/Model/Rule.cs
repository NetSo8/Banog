namespace Banog.Core.Model;

public sealed class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "Nouvelle règle";
    public bool Enabled { get; set; } = true;

    /// <summary>ET / OU appliqué à <see cref="Conditions"/>.</summary>
    public ConditionMatchMode Match { get; set; } = ConditionMatchMode.All;

    public List<RuleCondition> Conditions { get; set; } = [];
    public List<RuleAction> Actions { get; set; } = [];

    /// <summary>
    /// Si vrai, les règles suivantes ne sont plus évaluées pour ce fichier une fois
    /// celle-ci déclenchée.
    /// </summary>
    public bool StopProcessingOnMatch { get; set; } = true;

    /// <summary>Ordre d'évaluation, croissant.</summary>
    public int Order { get; set; }
}

public sealed class WatchedFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Path { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Règles appliquées à ce dossier, dans l'ordre. Vide = toutes les règles.</summary>
    public List<string> RuleIds { get; set; } = [];
}

public enum LanguagePreference
{
    /// <summary>Suit la langue de Windows : français si Windows est en français, anglais sinon.</summary>
    System = 0,
    French = 1,
    English = 2,
}

public enum ThemePreference
{
    /// <summary>Suit le thème clair/sombre de Windows, et réagit à ses changements.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Racine du fichier de configuration persisté.</summary>
public sealed class AppConfiguration
{
    /// <summary>Version du format, pour les migrations futures.</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<WatchedFolder> Folders { get; set; } = [];
    public List<Rule> Rules { get; set; } = [];

    /// <summary>Délai de stabilisation avant traitement d'un fichier (ms).</summary>
    public int DebounceMilliseconds { get; set; } = 750;

    /// <summary>
    /// Préférence d'apparence. Champ ajouté après coup : son absence dans un fichier
    /// existant retombe sur <see cref="ThemePreference.System"/>, sans migration.
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Langue de l'interface. Comme <see cref="Theme"/>, son absence dans un fichier
    /// existant vaut <see cref="LanguagePreference.System"/> : la langue de Windows.
    /// </summary>
    public LanguagePreference Language { get; set; } = LanguagePreference.System;

}
