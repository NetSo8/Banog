using System.Collections.Concurrent;

namespace Banog.Watcher;

/// <summary>
/// Debounce + attente de stabilisation.
///
/// Une seule copie de fichier produit typiquement une rafale d'événements (création,
/// puis N écritures, puis fermeture). On coalesce par chemin, on attend une période de
/// calme, puis on vérifie que le fichier est réellement ouvrable en exclusif — sans quoi
/// on traiterait un téléchargement encore en cours.
/// </summary>
public sealed class FileStabilizer : IDisposable
{
    private sealed class Pending
    {
        public required string WatchedRoot { get; init; }
        public long LastEventTicks;
        public long FirstEventTicks;
        public long LastKnownSize = -1;
    }

    private readonly ConcurrentDictionary<string, Pending> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _maxWait;
    private readonly int _capacity;
    private readonly Timer _timer;
    private int _disposed;

    /// <summary>Levé une fois par fichier stabilisé.</summary>
    public event Action<FileReadyEvent>? FileReady;

    /// <summary>Fichier resté verrouillé au-delà du délai maximum.</summary>
    public event Action<string>? GaveUp;

    /// <summary>Nombre de fichiers en attente au-delà duquel les nouveaux sont refusés.</summary>
    public event Action<string>? Dropped;

    public FileStabilizer(
        TimeSpan quietPeriod, TimeSpan? maxWait = null, TimeSpan? tick = null, int capacity = 100_000)
    {
        _quietPeriod = quietPeriod <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(250) : quietPeriod;
        _maxWait = maxWait ?? TimeSpan.FromMinutes(5);
        _capacity = capacity;

        var interval = tick ?? TimeSpan.FromMilliseconds(200);
        _timer = new Timer(_ => Sweep(), null, interval, interval);
    }

    public void Notify(FileChangeEvent change)
    {
        if (change.Kind == FileChangeKind.Deleted)
        {
            _pending.TryRemove(change.FullPath, out _);
            return;
        }

        // Un renommage invalide l'entrée sous l'ancien nom.
        if (change.OldFullPath is { } old) _pending.TryRemove(old, out _);

        // Borne mémoire : un dossier qui déverse des millions d'entrées ne doit pas faire
        // gonfler le processus sans limite. On refuse les nouveaux plutôt que d'évincer un
        // fichier déjà en cours de stabilisation.
        if (_pending.Count >= _capacity && !_pending.ContainsKey(change.FullPath))
        {
            Dropped?.Invoke(change.FullPath);
            return;
        }

        var now = DateTime.UtcNow.Ticks;

        _pending.AddOrUpdate(
            change.FullPath,
            _ => new Pending
            {
                WatchedRoot = change.WatchedRoot,
                LastEventTicks = now,
                FirstEventTicks = now,
            },
            (_, existing) =>
            {
                Interlocked.Exchange(ref existing.LastEventTicks, now);
                return existing;
            });
    }

    private void Sweep()
    {
        if (Volatile.Read(ref _disposed) != 0 || _pending.IsEmpty) return;

        var now = DateTime.UtcNow;

        foreach (var (path, pending) in _pending)
        {
            var lastEvent = new DateTime(Interlocked.Read(ref pending.LastEventTicks), DateTimeKind.Utc);
            if (now - lastEvent < _quietPeriod) continue;

            if (!File.Exists(path))
            {
                // Le fichier a été déplacé ou supprimé pendant l'attente : un dossier
                // remonte aussi par ce chemin, on l'ignore de la même façon.
                _pending.TryRemove(path, out _);
                continue;
            }

            if (!IsReadable(path, out var size))
            {
                var firstEvent = new DateTime(pending.FirstEventTicks, DateTimeKind.Utc);
                if (now - firstEvent > _maxWait)
                {
                    _pending.TryRemove(path, out _);
                    GaveUp?.Invoke(path);
                }

                continue;
            }

            // Une taille encore mouvante malgré un handle libre (copie par blocs) : on repousse.
            if (pending.LastKnownSize != size)
            {
                pending.LastKnownSize = size;
                Interlocked.Exchange(ref pending.LastEventTicks, now.Ticks);
                continue;
            }

            if (_pending.TryRemove(path, out var removed))
            {
                FileReady?.Invoke(new FileReadyEvent(path, removed.WatchedRoot));
            }
        }
    }

    private static bool IsReadable(string path, out long size)
    {
        size = -1;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            size = stream.Length;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        _pending.Clear();
    }
}
