using Banog.Core.Abstractions;

namespace Banog.Core.Tests;

/// <summary>Système de fichiers en mémoire : les tests du moteur ne touchent jamais le disque.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, FileMetadata> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecycledPaths { get; } = [];
    public List<string> DeletedPaths { get; } = [];
    public List<(string Source, string Destination)> Copies { get; } = [];

    public FakeFileSystem AddFile(
        string path,
        long size = 1024,
        DateTimeOffset? created = null,
        DateTimeOffset? modified = null)
    {
        var timestamp = created ?? DateTimeOffset.UnixEpoch;
        Files[path] = new FileMetadata(size, timestamp, modified ?? timestamp);
        Directories.Add(Path.GetDirectoryName(path)!);
        return this;
    }

    public bool FileExists(string path) => Files.ContainsKey(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public void CreateDirectory(string path) => Directories.Add(path);

    public void Move(string source, string destination, bool overwrite)
    {
        if (!Files.TryGetValue(source, out var metadata))
            throw new FileNotFoundException(source);
        if (Files.ContainsKey(destination) && !overwrite)
            throw new IOException($"Existe déjà : {destination}");

        Files.Remove(source);
        Files[destination] = metadata;
    }

    public void Copy(string source, string destination, bool overwrite)
    {
        if (!Files.TryGetValue(source, out var metadata))
            throw new FileNotFoundException(source);
        if (Files.ContainsKey(destination) && !overwrite)
            throw new IOException($"Existe déjà : {destination}");

        Files[destination] = metadata;
        Copies.Add((source, destination));
    }

    public void Delete(string path)
    {
        Files.Remove(path);
        DeletedPaths.Add(path);
    }

    public void SendToRecycleBin(string path)
    {
        Files.Remove(path);
        RecycledPaths.Add(path);
    }

    public FileMetadata? TryGetMetadata(string path) =>
        Files.TryGetValue(path, out var metadata) ? metadata : null;
}

public sealed class FixedClock(DateTimeOffset now) : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public sealed class RecordingProcessRunner : IProcessRunner
{
    public List<(string Executable, string[] Arguments)> Calls { get; } = [];
    public int ExitCode { get; set; }

    public Task<int> RunAsync(
        string executable, IReadOnlyList<string> arguments, string? workingDirectory,
        bool waitForExit, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add((executable, [.. arguments]));
        return Task.FromResult(ExitCode);
    }
}

public static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    public static FileContext File(
        string path = @"C:\Downloads\facture_client.pdf",
        long size = 2048,
        DateTimeOffset? created = null,
        DateTimeOffset? modified = null,
        string watchedRoot = @"C:\Downloads") => new(
            path,
            watchedRoot,
            size,
            created ?? new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.Zero),
            modified ?? new DateTimeOffset(2026, 2, 10, 18, 45, 0, TimeSpan.Zero));
}
