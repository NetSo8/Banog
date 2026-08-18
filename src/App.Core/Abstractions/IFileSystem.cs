namespace Banog.Core.Abstractions;

/// <summary>
/// Toutes les opérations disque passent par là. Le moteur de règles ne touche jamais
/// System.IO directement : les tests substituent une implémentation en mémoire.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);

    void Move(string source, string destination, bool overwrite);
    void Copy(string source, string destination, bool overwrite);

    /// <summary>Suppression définitive.</summary>
    void Delete(string path);

    /// <summary>Envoi vers la corbeille Windows.</summary>
    void SendToRecycleBin(string path);

    /// <summary>Métadonnées du fichier, ou <c>null</c> s'il a disparu entre-temps.</summary>
    FileMetadata? TryGetMetadata(string path);
}

public readonly record struct FileMetadata(long SizeBytes, DateTimeOffset CreatedUtc, DateTimeOffset ModifiedUtc);

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IProcessRunner
{
    /// <summary>
    /// Les arguments sont déjà découpés et transmis tels quels au processus, un par un.
    /// Il n'existe volontairement aucune surcharge prenant une ligne de commande unique :
    /// c'est ce qui empêche un nom de fichier de se faire passer pour un argument de plus.
    /// </summary>
    Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool waitForExit,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>Journal minimal, injecté pour éviter une dépendance de logging dans le coeur.</summary>
public interface IRuleLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullRuleLog : IRuleLog
{
    public static readonly NullRuleLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
