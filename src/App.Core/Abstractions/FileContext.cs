namespace Banog.Core.Abstractions;

/// <summary>
/// Instantané des métadonnées d'un fichier au moment de l'évaluation.
/// Sans I/O : le moteur de règles reste testable sans disque.
///
/// Les composants du chemin (nom, base, extension, dossier) sont découpés une seule fois
/// à la construction et exposés en <see cref="ReadOnlySpan{T}"/> sur la chaîne d'origine.
/// Les exposer en <c>string</c> allouait une chaîne à chaque lecture de propriété, donc à
/// chaque condition évaluée sur chaque fichier.
/// </summary>
public sealed class FileContext
{
    private readonly int _nameStart;
    private readonly int _extensionStart;

    public FileContext(
        string fullPath,
        string watchedRoot,
        long sizeBytes,
        DateTimeOffset createdUtc,
        DateTimeOffset modifiedUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);

        FullPath = fullPath;
        WatchedRoot = watchedRoot;
        SizeBytes = sizeBytes;
        CreatedUtc = createdUtc;
        ModifiedUtc = modifiedUtc;

        var span = fullPath.AsSpan();

        var separator = span.LastIndexOfAny('\\', '/');
        _nameStart = separator + 1;

        // Un point en tête n'ouvre pas une extension : « .gitignore » n'a pas d'extension.
        var dot = span[_nameStart..].LastIndexOf('.');
        _extensionStart = dot > 0 ? _nameStart + dot + 1 : -1;
    }

    public string FullPath { get; }

    /// <summary>Dossier surveillé à l'origine du déclenchement (peut différer du dossier parent en récursif).</summary>
    public string WatchedRoot { get; }

    public long SizeBytes { get; }
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset ModifiedUtc { get; }

    /// <summary>Dossier parent, sans séparateur final. Vide si le chemin n'en a pas.</summary>
    public ReadOnlySpan<char> DirectoryPath =>
        _nameStart > 1 ? FullPath.AsSpan(0, _nameStart - 1) : ReadOnlySpan<char>.Empty;

    /// <summary>Nom complet, extension comprise.</summary>
    public ReadOnlySpan<char> FileName => FullPath.AsSpan(_nameStart);

    /// <summary>Nom sans extension.</summary>
    public ReadOnlySpan<char> BaseName => _extensionStart < 0
        ? FullPath.AsSpan(_nameStart)
        : FullPath.AsSpan(_nameStart, _extensionStart - _nameStart - 1);

    /// <summary>Extension sans le point. Vide si absente. La casse est celle du disque.</summary>
    public ReadOnlySpan<char> Extension =>
        _extensionStart < 0 ? ReadOnlySpan<char>.Empty : FullPath.AsSpan(_extensionStart);

    /// <summary>Nom du dossier parent seul (dernier segment), pour le token <c>{folder}</c>.</summary>
    public ReadOnlySpan<char> DirectoryName
    {
        get
        {
            var directory = DirectoryPath;
            var separator = directory.LastIndexOfAny('\\', '/');
            return separator < 0 ? directory : directory[(separator + 1)..];
        }
    }

    /// <summary>Même fichier vu à un nouveau chemin, après déplacement ou renommage.</summary>
    public FileContext WithPath(string newFullPath) =>
        new(newFullPath, WatchedRoot, SizeBytes, CreatedUtc, ModifiedUtc);

    public override string ToString() => FullPath;
}
