namespace Banog.Core.Localization;

/// <summary>
/// Les messages que le moteur écrit dans le journal d'activité.
///
/// Ce sont les seules phrases du noyau qu'un utilisateur lit : elles apparaissent dans
/// « ce qui s'est passé ». Elles suivent donc la langue de l'interface, que l'UI pose
/// ici au démarrage et à chaque changement. Les exceptions purement techniques
/// (format de fichier illisible, type inconnu au registre) restent hors de ce jeu :
/// elles signalent un défaut, pas un événement de rangement.
/// </summary>
public static class CoreTexts
{
    /// <summary>Posé par l'interface. Le noyau n'a pas à connaître le réglage de Windows.</summary>
    public static bool French { get; set; } = true;

    private static string Pick(string fr, string en) => French ? fr : en;

    // ---- Actions ------------------------------------------------------------------------

    public static string EmptyDestination =>
        Pick("Destination vide après expansion des tokens.", "Destination empty after token expansion.");

    public static string RelativeDestination(string directory) =>
        Pick($"Destination non absolue : {directory}", $"Destination is not absolute: {directory}");

    public static string FileAlreadyConsumed =>
        Pick("Fichier déjà consommé.", "File already consumed.");

    public static string MissingDirectory(string directory) =>
        Pick($"Dossier inexistant : {directory}", $"Folder does not exist: {directory}");

    public static string TargetAlreadyThere(string directory) =>
        Pick($"Cible déjà présente dans {directory}.", $"Target already present in {directory}.");

    public static string Moved(string target) =>
        Pick($"Déplacé vers {target}", $"Moved to {target}");

    public static string Copied(string target) =>
        Pick($"Copié vers {target}", $"Copied to {target}");

    public static string NameTaken =>
        Pick("Un fichier porte déjà ce nom.", "A file already has that name.");

    public static string NameUnchanged =>
        Pick("Le nom est déjà celui attendu.", "The name is already the expected one.");

    public static string Renamed(string name) =>
        Pick($"Renommé en {name}", $"Renamed to {name}");

    public static string Recycled =>
        Pick("Envoyé à la corbeille.", "Sent to the recycle bin.");

    public static string Deleted =>
        Pick("Supprimé définitivement.", "Permanently deleted.");

    public static string NoExecutable =>
        Pick("Exécutable non renseigné.", "No executable given.");

    public static string CommandRan(string executable) =>
        Pick($"Commande exécutée : {executable}", $"Command run: {executable}");

    public static string CommandFailed(int exitCode, string executable) => Pick(
        $"Commande terminée avec le code {exitCode} : {executable}",
        $"Command finished with code {exitCode}: {executable}");

    public static string CannotStart(string executable) =>
        Pick($"Impossible de démarrer : {executable}", $"Could not start: {executable}");

    public static string CommandTimedOut(double seconds, string executable) => Pick(
        $"Commande interrompue après {seconds:0} s : {executable}",
        $"Command stopped after {seconds:0} s: {executable}");

    public static string RecycleFailed(string path) =>
        Pick($"Échec de l'envoi à la corbeille : {path}", $"Could not send to the recycle bin: {path}");

    // ---- Moteur de règles ---------------------------------------------------------------

    public static string RuleEvaluationFailed(string rule) =>
        Pick($"Règle « {rule} » : échec d'évaluation.", $"Rule “{rule}”: evaluation failed.");

    public static string RuleStopped(string rule, string reason) =>
        Pick($"Règle « {rule} » interrompue : {reason}", $"Rule “{rule}” stopped: {reason}");

    public static string RuleActionFailed(string rule, string action) =>
        Pick($"Règle « {rule} », action {action} : échec.", $"Rule “{rule}”, action {action}: failed.");

    // ---- Surveillance -------------------------------------------------------------------

    public static string WatchingStarted(int folders) =>
        Pick($"Surveillance démarrée ({folders} dossier(s)).", $"Watching started ({folders} folder(s)).");

    public static string WatchingStopped =>
        Pick("Surveillance arrêtée.", "Watching stopped.");

    public static string WatchingFolder(string path, bool recursive) => Pick(
        $"Surveillance active : {path}{(recursive ? " (récursif)" : string.Empty)}",
        $"Watching: {path}{(recursive ? " (recursive)" : string.Empty)}");

    public static string FolderNotFound(string path) =>
        Pick($"Dossier surveillé introuvable, ignoré : {path}", $"Watched folder not found, skipped: {path}");

    public static string CannotOpenFolder(string path) =>
        Pick($"Impossible d'ouvrir le dossier surveillé : {path}", $"Could not open the watched folder: {path}");

    public static string FileLocked(string path) =>
        Pick($"Fichier resté verrouillé, abandonné : {path}", $"File stayed locked, gave up: {path}");

    public static string QueueFull(string path) =>
        Pick($"File d'attente saturée, fichier ignoré : {path}", $"Queue full, file skipped: {path}");

    public static string NotificationOverflow(string path) => Pick(
        $"Débordement de notifications sur {path}, réanalyse du dossier.",
        $"Notification overflow on {path}, rescanning the folder.");

    public static string RescanFailed(string path) =>
        Pick($"Réanalyse impossible : {path}", $"Rescan failed: {path}");

    /// <summary>Une action après l'autre, dans la ligne de journal d'un fichier rangé.</summary>
    public static string StepSeparator => Pick(", puis ", ", then ");

    /// <summary>« photo.jpg — Photos : Déplacé vers D:\Photos ».</summary>
    public static string Outcome(string file, string rule, string steps) => $"{file} — {rule} : {steps}";

    /// <summary>« photo.jpg : le fichier est utilisé par un autre programme ».</summary>
    public static string FileError(string file, string error) => $"{file} : {error}";
}
