using Banog.Core.Model;

namespace Banog.UI.Services;

/// <summary>
/// Nature d'une entrée d'activité. Le mode surveillance s'en sert pour compter les
/// fichiers réellement traités sans avoir à interpréter le texte des messages.
/// </summary>
public enum ActivityKind
{
    /// <summary>Démarrage, arrêt, message du moteur ou du watcher.</summary>
    System = 0,

    /// <summary>Une règle s'est appliquée à un fichier.</summary>
    FileHandled = 1,
}

public sealed record ActivityEntry(
    DateTimeOffset Timestamp,
    string Message,
    bool IsError = false,
    ActivityKind Kind = ActivityKind.System,
    string? RuleId = null)
{
    public string Display => $"{Timestamp.ToLocalTime():HH:mm:ss}  {Message}";

    /// <summary>Heure seule : le flux affiche l'horodatage dans sa propre colonne.</summary>
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>
/// Vue que l'UI a du moteur en marche. L'implémentation vit dans l'hôte (elle assemble
/// le watcher natif et le moteur de règles) : App.UI ne référence donc ni App.Watcher
/// ni aucune API Windows.
/// </summary>
public interface IAutomationController
{
    bool IsRunning { get; }

    event Action<ActivityEntry>? Activity;
    event Action? StateChanged;

    void Start();
    void Stop();

    /// <summary>Recharge la configuration à chaud et réaligne les dossiers surveillés.</summary>
    void ApplyConfiguration(AppConfiguration configuration);

    /// <summary>Passe le contenu actuel des dossiers surveillés dans le moteur.</summary>
    void RunNow();
}

/// <summary>Sélection de dossier ou de fichier, abstraite pour garder les viewmodels testables hors UI.</summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title);

    /// <summary>Sélection d'un fichier existant. <paramref name="filter"/> est un libellé de type.</summary>
    Task<string?> PickFileAsync(string title, string? filter = null, string? extension = null);
}

/// <summary>
/// Élément d'édition portant un chemin de dossier parcourable. Permet au bouton
/// « Parcourir » d'être générique, sans que la vue connaisse le type concret.
/// </summary>
public interface IBrowsableFolder
{
    string BrowseTitle { get; }
    string FolderPath { get; set; }
}
