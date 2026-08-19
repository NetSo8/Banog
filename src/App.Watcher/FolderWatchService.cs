using System.Runtime.Versioning;
using Banog.Core.Abstractions;
using Banog.Core.Model;

namespace Banog.Watcher;

/// <summary>
/// Orchestre la surveillance de l'ensemble des dossiers configurés :
/// un <see cref="DirectoryWatcher"/> natif par dossier, un <see cref="FileStabilizer"/>
/// partagé, et une sortie unique sous forme d'événement <see cref="FileReady"/>.
///
/// Ce service ne connaît pas le moteur de règles : il publie des fichiers prêts,
/// c'est l'hôte qui les fait traverser le moteur.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FolderWatchService : IDisposable
{
    private readonly Dictionary<string, DirectoryWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _recursive = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileStabilizer _stabilizer;
    private readonly IRuleLog _log;
    private readonly Lock _gate = new();
    private int _disposed;

    public event Action<FileReadyEvent>? FileReady;

    public FolderWatchService(TimeSpan debounce, IRuleLog? log = null)
    {
        _log = log ?? NullRuleLog.Instance;
        _stabilizer = new FileStabilizer(debounce);
        _stabilizer.FileReady += e => FileReady?.Invoke(e);
        _stabilizer.GaveUp += path => _log.Warn(Banog.Core.Localization.CoreTexts.FileLocked(path));
        _stabilizer.Dropped += path => _log.Warn(Banog.Core.Localization.CoreTexts.QueueFull(path));
    }

    public IReadOnlyCollection<string> WatchedPaths
    {
        get { lock (_gate) { return [.. _watchers.Keys]; } }
    }

    /// <summary>Aligne les watchers actifs sur la configuration : ajoute, retire, rien d'autre.</summary>
    public void Sync(IEnumerable<WatchedFolder> folders)
    {
        var desired = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            if (!folder.Enabled || string.IsNullOrWhiteSpace(folder.Path)) continue;

            // Un chemin non normalisable ne doit pas faire échouer la surveillance
            // d'ensemble : on ignore ce dossier, comme le fait le contrôleur.
            string root;
            try
            {
                root = Path.GetFullPath(folder.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _log.Warn(Banog.Core.Localization.CoreTexts.FolderNotFound(folder.Path));
                continue;
            }

            desired[root] = folder.IncludeSubfolders;
        }

        lock (_gate)
        {
            foreach (var path in _watchers.Keys.ToList())
            {
                var stillWanted = desired.TryGetValue(path, out var recursive)
                                  && _recursive.TryGetValue(path, out var current)
                                  && recursive == current;

                if (stillWanted) continue;

                _watchers[path].Dispose();
                _watchers.Remove(path);
                _recursive.Remove(path);
            }

            foreach (var (path, recursive) in desired)
            {
                if (_watchers.ContainsKey(path)) continue;

                if (!Directory.Exists(path))
                {
                    _log.Warn(Banog.Core.Localization.CoreTexts.FolderNotFound(path));
                    continue;
                }

                try
                {
                    var watcher = new DirectoryWatcher(path, recursive);
                    watcher.Changed += _stabilizer.Notify;
                    watcher.Faulted += (p, ex) => _log.Error($"Surveillance interrompue : {p}", ex);
                    watcher.Overflowed += OnOverflow;
                    watcher.Start();

                    _watchers[path] = watcher;
                    _recursive[path] = recursive;
                    _log.Info(Banog.Core.Localization.CoreTexts.WatchingFolder(path, recursive));
                }
                catch (Exception ex)
                {
                    _log.Error($"Impossible de surveiller {path}.", ex);
                }
            }
        }
    }

    /// <summary>
    /// Après un débordement du buffer noyau, des événements sont définitivement perdus :
    /// on réinjecte le contenu du dossier pour retrouver un état cohérent.
    /// </summary>
    private void OnOverflow(string path)
    {
        _log.Warn(Banog.Core.Localization.CoreTexts.NotificationOverflow(path));
        Rescan(path);
    }

    /// <summary>Réinjecte les fichiers déjà présents (démarrage, ou reprise après débordement).</summary>
    public void Rescan(string path)
    {
        bool recursive;
        lock (_gate)
        {
            if (!_recursive.TryGetValue(path, out recursive)) return;
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,

                // ReparsePoint exclut jonctions et liens symboliques. Sans ça, une jonction
                // déposée dans un dossier surveillé ferait sortir la réanalyse de
                // l'arborescence — jusqu'à C:\Windows — et un lien circulaire la ferait
                // tourner indéfiniment.
                AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
            };

            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                _stabilizer.Notify(new FileChangeEvent(file, path, FileChangeKind.Changed));
            }
        }
        catch (Exception ex)
        {
            _log.Error(Banog.Core.Localization.CoreTexts.RescanFailed(path), ex);
        }
    }

    public void RescanAll()
    {
        foreach (var path in WatchedPaths) Rescan(path);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        lock (_gate)
        {
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            _recursive.Clear();
        }

        _stabilizer.Dispose();
    }
}
