using System.Runtime.Versioning;
using System.Threading.Channels;
using Avalonia.Threading;
using Banog.Core.Abstractions;
using Banog.Core.Engine;
using Banog.Core.Execution;
using Banog.Core.Localization;
using Banog.Core.Model;
using Banog.UI.Services;
using Banog.Watcher;

namespace Banog.Host.Automation;

/// <summary>
/// Point de jonction entre la surveillance native et le moteur de règles.
///
/// Les fichiers prêts sont poussés dans un canal et traités par un unique consommateur :
/// deux règles ne peuvent pas déplacer le même fichier en même temps, et l'ordre de
/// traitement reste celui d'arrivée.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutomationController : IAutomationController, IDisposable
{
    private readonly RuleEngine _engine;
    private readonly IFileSystem _fileSystem;
    private readonly FolderWatchService _watchService;
    /// <summary>
    /// File bornée : au-delà, on attend plutôt que d'empiler indéfiniment. Une réanalyse
    /// de dossier volumineux ne doit pas se traduire par une consommation mémoire libre.
    /// </summary>
    private readonly Channel<FileReadyEvent> _queue = Channel.CreateBounded<FileReadyEvent>(
        new BoundedChannelOptions(50_000)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly Lock _gate = new();

    private AppConfiguration _configuration = new();

    /// <summary>
    /// Règles applicables, indexées par dossier surveillé normalisé, et déjà triées par
    /// ordre d'évaluation. Construite au chargement de la configuration : la résolution
    /// par fichier devient une recherche en table de hachage, au lieu d'un balayage des
    /// dossiers doublé d'un balayage des règles à chaque événement.
    /// </summary>
    private Dictionary<string, Rule[]> _rulesByRoot = new(StringComparer.OrdinalIgnoreCase);
    private Rule[] _allRules = [];

    private bool _running;
    private int _disposed;

    public event Action<ActivityEntry>? Activity;
    public event Action? StateChanged;

    public bool IsRunning
    {
        get { lock (_gate) { return _running; } }
    }

    public AutomationController(RuleEngine engine, IFileSystem fileSystem, TimeSpan debounce, IRuleLog log)
    {
        _engine = engine;
        _fileSystem = fileSystem;

        _watchService = new FolderWatchService(debounce, log);
        _watchService.FileReady += OnFileReady;

        _worker = Task.Run(() => ConsumeAsync(_shutdown.Token));
    }

    public void ApplyConfiguration(AppConfiguration configuration)
    {
        var sorted = configuration.Rules.ToArray();
        Array.Sort(sorted, static (a, b) => a.Order.CompareTo(b.Order));

        var index = new Dictionary<string, Rule[]>(configuration.Folders.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var folder in configuration.Folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path)) continue;

            // Une liste de règles vide côté dossier signifie « toutes ».
            Rule[] applicable;
            if (folder.RuleIds.Count == 0)
            {
                applicable = sorted;
            }
            else
            {
                var wanted = new HashSet<string>(folder.RuleIds, StringComparer.Ordinal);
                applicable = Array.FindAll(sorted, r => wanted.Contains(r.Id));
            }

            index[Path.GetFullPath(folder.Path)] = applicable;
        }

        lock (_gate)
        {
            _configuration = configuration;
            _rulesByRoot = index;
            _allRules = sorted;
            if (_running) _watchService.Sync(configuration.Folders);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _watchService.Sync(_configuration.Folders);
            _running = true;
        }

        Report(CoreTexts.WatchingStarted(_watchService.WatchedPaths.Count));
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _watchService.Sync([]);
            _running = false;
        }

        Report(CoreTexts.WatchingStopped);
        StateChanged?.Invoke();
    }

    public void RunNow()
    {
        lock (_gate)
        {
            if (!_running)
            {
                // Un passage manuel n'exige pas d'être en marche : on monte les watchers
                // le temps de l'énumération, l'utilisateur garde la main sur l'état.
                _watchService.Sync(_configuration.Folders);
            }
        }

        _watchService.RescanAll();
    }

    private void OnFileReady(FileReadyEvent ready)
    {
        // Écriture non bloquante : le thread de surveillance ne doit jamais attendre.
        // Si la file est pleine, on bascule sur l'écriture asynchrone plutôt que de perdre
        // l'événement.
        if (_queue.Writer.TryWrite(ready)) return;

        _ = WriteWhenFreeAsync(ready);
    }

    private async Task WriteWhenFreeAsync(FileReadyEvent ready)
    {
        try
        {
            await _queue.Writer.WriteAsync(ready, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Arrêt en cours.
        }
        catch (ChannelClosedException)
        {
            // Arrêt en cours.
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var ready in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await ProcessAsync(ready, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal.
        }
    }

    private async Task ProcessAsync(FileReadyEvent ready, CancellationToken ct)
    {
        Rule[] rules;
        lock (_gate)
        {
            // Recherche en O(1), sur des règles déjà triées : rien à filtrer ni à ordonner
            // par fichier.
            rules = _rulesByRoot.TryGetValue(ready.WatchedRoot, out var forRoot) ? forRoot : _allRules;
        }

        if (rules.Length == 0) return;

        var metadata = _fileSystem.TryGetMetadata(ready.FullPath);
        if (metadata is not { } meta) return; // Disparu entre la stabilisation et le traitement.

        var context = new FileContext(
            ready.FullPath,
            ready.WatchedRoot,
            meta.SizeBytes,
            meta.CreatedUtc,
            meta.ModifiedUtc);

        try
        {
            var report = await _engine.ProcessAsync(context, rules, ct).ConfigureAwait(false);
            ReportOutcome(context, report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report(CoreTexts.FileError(Path.GetFileName(ready.FullPath), ex.Message), isError: true);
        }
    }

    /// <summary>
    /// Une ligne par règle déclenchée, actions enchaînées dans le message : le flux se lit
    /// comme un journal de ce qui est arrivé au fichier, pas comme une trace d'exécution.
    /// </summary>
    private void ReportOutcome(FileContext file, FileProcessingReport report)
    {
        if (!report.AnyRuleMatched) return;

        var name = file.FileName.ToString();

        for (var i = 0; i < report.Rules.Count; i++)
        {
            var rule = report.Rules[i];
            if (!rule.Matched || rule.Actions.Count == 0) continue;

            var failed = false;
            var steps = new List<string>(rule.Actions.Count);

            for (var j = 0; j < rule.Actions.Count; j++)
            {
                var action = rule.Actions[j];
                steps.Add(action.Message);
                if (action.Status == ActionStatus.Failed) failed = true;
            }

            Publish(new ActivityEntry(
                DateTimeOffset.Now,
                CoreTexts.Outcome(name, rule.RuleName, string.Join(CoreTexts.StepSeparator, steps)),
                failed,
                ActivityKind.FileHandled,
                rule.RuleId));
        }
    }

    private void Report(string message, bool isError = false) =>
        Publish(new ActivityEntry(DateTimeOffset.Now, message, isError));

    /// <summary>Pousse une entrée dans le flux d'activité, quel que soit le thread appelant.</summary>
    public void Publish(ActivityEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Activity?.Invoke(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Activity?.Invoke(entry));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _queue.Writer.TryComplete();
        _shutdown.Cancel();

        try { _worker.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }

        _watchService.Dispose();
        _shutdown.Dispose();
    }
}

/// <summary>
/// Journal du moteur et du watcher redirigé vers le flux d'activité de l'UI.
/// Le puits est branché après coup : le journal est créé avant le contrôleur, qui en dépend.
/// </summary>
public sealed class ActivityLog : IRuleLog
{
    public Action<ActivityEntry>? Sink { get; set; }

    public void Info(string message) => Emit(message, false);
    public void Warn(string message) => Emit(message, false);
    public void Error(string message, Exception? exception = null) =>
        Emit(exception is null ? message : $"{message} ({exception.Message})", true);

    private void Emit(string message, bool isError)
    {
        if (Sink is null) return;

        var entry = new ActivityEntry(DateTimeOffset.Now, message, isError);

        if (Dispatcher.UIThread.CheckAccess()) Sink(entry);
        else Dispatcher.UIThread.Post(() => Sink?.Invoke(entry));
    }
}
