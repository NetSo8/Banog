namespace Banog.Watcher;

public enum FileChangeKind
{
    Created = 0,
    Changed = 1,
    Renamed = 2,
    Deleted = 3,
}

/// <summary>Événement brut remonté par le watcher natif, avant debounce.</summary>
public sealed record FileChangeEvent(
    string FullPath,
    string WatchedRoot,
    FileChangeKind Kind,
    string? OldFullPath = null);

/// <summary>Fichier stabilisé, prêt à être soumis au moteur de règles.</summary>
public sealed record FileReadyEvent(string FullPath, string WatchedRoot);
