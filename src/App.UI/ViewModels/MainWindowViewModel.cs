using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Banog.Core.Abstractions;
using Banog.Core.Engine;
using Banog.Core.Evaluation;
using Banog.Core.Execution;
using Banog.Core.Model;
using Banog.Core.Storage;
using Banog.UI.Localization;
using Banog.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Banog.UI.ViewModels;

/// <summary>
/// Les trois espaces de l'application. Un seul est visible à la fois : on regarde ce qui
/// se passe, on écrit les règles, ou on règle l'application — jamais les trois en même
/// temps, ce qui évite d'avoir tous les boutons partout.
/// </summary>
public enum AppSection
{
    Monitoring = 0,
    Rules = 1,
    Settings = 2,
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConfigurationStore _store;
    private readonly IAutomationController _automation;
    private readonly IThemeService _theme;

    /// <summary>
    /// Non figé : un changement de langue reconstruit la fenêtre, et le sélecteur de
    /// dossiers s'adosse toujours à la fenêtre affichée.
    /// </summary>
    private IFolderPicker _folderPicker;

    private AppConfiguration _configuration = new();
    private bool _loading;
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(750);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _autoSaveCancellation;
    private int _changeVersion;

    public ObservableCollection<WatchedFolderViewModel> Folders { get; } = [];
    public ObservableCollection<RuleViewModel> Rules { get; } = [];
    public ObservableCollection<ActivityEntry> Activity { get; } = [];

    /// <summary>La règle sélectionnée, présentée en flux. Vide tant qu'aucune règle n'est choisie.</summary>
    public Flow.RuleFlowViewModel Flow { get; }

    [ObservableProperty] public partial WatchedFolderViewModel? SelectedFolder { get; set; }
    [ObservableProperty] public partial RuleViewModel? SelectedRule { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }

    /// <summary>
    /// La ligne d'état est gardée sous forme de fabrique, pas de texte : un changement de
    /// langue doit réécrire le dernier message, pas laisser une phrase dans l'ancienne.
    /// </summary>
    private Func<string> _status = () => Loc.T("Status_Ready");

    public string StatusMessage => _status();

    private void SetStatus(Func<string> message)
    {
        _status = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    /// <summary>Espace affiché. Pilote la barre latérale et le contenu.</summary>
    [ObservableProperty] public partial AppSection Section { get; set; } = AppSection.Monitoring;

    /// <summary>
    /// L'espace des règles a deux temps : la liste de ce qui existe, puis le flux d'une
    /// règle qu'on a choisi de modifier. Une seule règle à la fois occupe l'écran, ce qui
    /// laisse au flux toute la largeur.
    /// </summary>
    [ObservableProperty] public partial bool IsFlowOpen { get; set; }

    public bool ShowsRuleList => !IsFlowOpen;

    partial void OnIsFlowOpenChanged(bool value) => OnPropertyChanged(nameof(ShowsRuleList));

    /// <summary>
    /// Vrai dès qu'une règle ou un dossier a été modifié sans être enregistré. Le bouton
    /// « Enregistrer » ne vit plus que dans l'espace des règles : sans ce témoin, on
    /// pourrait quitter cet espace en croyant avoir enregistré.
    /// </summary>
    [ObservableProperty] public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial ThemeOption SelectedTheme { get; set; } = ThemeOption.All[0];

    /// <summary>Langue de l'interface. Comme le thème, elle s'applique et se persiste aussitôt.</summary>
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; } = LanguageOption.All[0];

    /// <summary>Délai de stabilisation avant qu'un fichier soit considéré comme prêt.</summary>
    [ObservableProperty] public partial int DebounceMilliseconds { get; set; } = 750;

    /// <summary>Résultat du dernier essai de règle. Vide tant qu'aucun essai n'a eu lieu.</summary>
    [ObservableProperty] public partial string TestResult { get; set; } = string.Empty;
    [ObservableProperty] public partial bool TestMatched { get; set; }

    // ---- Compteurs du mode surveillance ------------------------------------------------

    [ObservableProperty] public partial int FilesHandled { get; set; }
    [ObservableProperty] public partial int ErrorCount { get; set; }
    [ObservableProperty] public partial string LastEventLabel { get; set; } = Loc.T("Mon_Activity_None");

    public bool HasFolders => Folders.Count > 0;
    public bool HasRules => Rules.Count > 0;
    public bool HasActivity => Activity.Count > 0;
    public bool HasTestResult => !string.IsNullOrEmpty(TestResult);

    public bool IsMonitoring => Section == AppSection.Monitoring;
    public bool IsRules => Section == AppSection.Rules;
    public bool IsSettings => Section == AppSection.Settings;

    public int ActiveRuleCount => Rules.Count(r => r.Enabled);
    public int ActiveFolderCount => Folders.Count(f => f.Enabled);

    /// <summary>Phrase d'état affichée en gros dans la barre latérale.</summary>
    public string RunningLabel => Loc.T(IsRunning ? "State_Running" : "State_Paused");

    public string RunningDetail => IsRunning
        ? Loc.F(ActiveFolderCount > 1 ? "State_Watching_Many" : "State_Watching_One", ActiveFolderCount)
        : Loc.T("State_NothingWatched");

    /// <summary>« dernier : 14:32:05 », à côté du titre du journal.</summary>
    public string LastEventSummary => Loc.F("Mon_Activity_Last", LastEventLabel);

    public string ConfigurationPath => _store.Location;

    public MainWindowViewModel(
        IConfigurationStore store,
        IAutomationController automation,
        IFolderPicker folderPicker,
        IThemeService theme)
    {
        _store = store;
        _automation = automation;
        _folderPicker = folderPicker;
        _theme = theme;

        Flow = new Flow.RuleFlowViewModel(() => ActiveFolderCount);

        _automation.Activity += OnActivity;
        _automation.StateChanged += () => IsRunning = _automation.IsRunning;

        // Le démon a pu démarrer avant la fenêtre (lancement en arrière-plan) : l'état
        // affiché doit être le sien, pas un « en pause » par défaut.
        IsRunning = _automation.IsRunning;

        // Changer de langue reconstruit la vue, mais pas ce viewmodel : ce qu'il garde en
        // texte doit être réécrit à la main.
        Loc.Changed += OnLanguageChanged;

        Track(Folders, nameof(HasFolders), nameof(ActiveFolderCount));
        Track(Rules, nameof(HasRules), nameof(ActiveRuleCount));
        Activity.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasActivity));
    }

    /// <summary>
    /// Suit une collection éditable : rafraîchit les compteurs dérivés et marque la
    /// configuration comme modifiée, y compris quand la modification vient d'une condition
    /// ou d'une action imbriquée (les viewmodels de règle propagent déjà ces changements).
    /// </summary>
    private void Track(INotifyCollectionChanged collection, params string[] derived)
    {
        void Refresh()
        {
            foreach (var name in derived) OnPropertyChanged(name);
            OnPropertyChanged(nameof(RunningDetail));

            // Le déclencheur du flux annonce les dossiers surveillés : il les suit.
            Flow.RefreshTrigger();
        }

        void OnChildChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Un compteur de déclenchements n'est pas une modification à enregistrer.
            if (e.PropertyName is not nameof(RuleViewModel.TriggerCount)
                and not nameof(RuleViewModel.TriggerLabel)) MarkDirty();

            Refresh();
        }

        collection.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (INotifyPropertyChanged item in e.OldItems) item.PropertyChanged -= OnChildChanged;
            }

            if (e.NewItems is not null)
            {
                foreach (INotifyPropertyChanged item in e.NewItems) item.PropertyChanged += OnChildChanged;
            }

            MarkDirty();
            Refresh();
        };
    }

    private void MarkDirty()
    {
        if (_loading) return;

        IsDirty = true;
        _changeVersion++;

        // Un flowchart se construit souvent en plusieurs étapes : on laisse la saisie
        // respirer, puis on persiste automatiquement la dernière version complète.
        _autoSaveCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _autoSaveCancellation = cancellation;
        _ = AutoSaveAfterDelayAsync(cancellation);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(AutoSaveDelay, cancellation.Token).ConfigureAwait(true);
            await SaveConfigurationAsync(automatic: true).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_autoSaveCancellation, cancellation)) _autoSaveCancellation = null;
            cancellation.Dispose();
        }
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(LastEventSummary));

        // Le résultat d'un essai a été écrit dans l'autre langue : on le retire plutôt
        // que de le laisser à moitié traduit.
        TestResult = string.Empty;
    }

    /// <summary>Rattache les boîtes de dialogue à une nouvelle fenêtre.</summary>
    public void Rebind(IFolderPicker folderPicker) => _folderPicker = folderPicker;

    partial void OnTestResultChanged(string value) => OnPropertyChanged(nameof(HasTestResult));

    partial void OnSelectedRuleChanged(RuleViewModel? value)
    {
        TestResult = string.Empty;
        Flow.Bind(value);
    }

    partial void OnSectionChanged(AppSection value)
    {
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(IsRules));
        OnPropertyChanged(nameof(IsSettings));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunningLabel));
        OnPropertyChanged(nameof(RunningDetail));
    }

    partial void OnDebounceMillisecondsChanged(int value) => MarkDirty();

    [RelayCommand]
    private void ShowMonitoring() => Section = AppSection.Monitoring;

    [RelayCommand]
    private void ShowRules()
    {
        // Revenir à l'espace des règles montre la liste : c'est l'entrée naturelle,
        // le flux ne s'ouvre que sur une règle qu'on a choisie.
        Section = AppSection.Rules;
        IsFlowOpen = false;
    }

    [RelayCommand]
    private void ShowSettings() => Section = AppSection.Settings;

    /// <summary>Ouvre une règle dans son flux, depuis la liste comme depuis la surveillance.</summary>
    [RelayCommand]
    private void EditRule(RuleViewModel? rule)
    {
        if (rule is null) return;

        SelectedRule = rule;
        Section = AppSection.Rules;
        IsFlowOpen = true;
    }

    /// <summary>Referme le flux et revient à la liste des règles.</summary>
    [RelayCommand]
    private void CloseRule()
    {
        Flow.IsInspectorOpen = false;
        IsFlowOpen = false;
    }

    [RelayCommand]
    private void ClearActivity()
    {
        Activity.Clear();
        FilesHandled = 0;
        ErrorCount = 0;
        LastEventLabel = Loc.T("Mon_Activity_None");
        OnPropertyChanged(nameof(LastEventSummary));
        foreach (var rule in Rules) rule.TriggerCount = 0;
    }

    /// <summary>Constructeur de conception, utilisé uniquement par l'aperçu XAML.</summary>
    public MainWindowViewModel() : this(
        new JsonConfigurationStore(), new NullAutomationController(), new NullFolderPicker(),
        new NullThemeService())
    {
    }

    /// <summary>
    /// Le thème s'applique à la sélection et se persiste immédiatement : c'est une
    /// préférence d'affichage, pas une modification de règles, elle n'a rien à faire
    /// derrière le bouton « Enregistrer ».
    /// </summary>
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _theme.Apply(value.Value);
        if (_loading) return;

        _configuration.Theme = value.Value;
        _ = PersistPreferenceAsync(configuration => configuration.Theme = value.Value);
    }

    /// <summary>
    /// La langue suit le même contrat que le thème : appliquée tout de suite, persistée
    /// seule, jamais mêlée aux règles en cours d'édition.
    /// </summary>
    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        Loc.Apply(value.Value);
        if (_loading) return;

        _configuration.Language = value.Value;
        _ = PersistPreferenceAsync(configuration => configuration.Language = value.Value);
    }

    /// <summary>
    /// Relit le fichier avant d'écrire, pour ne persister que la préférence : les règles
    /// en cours d'édition ne doivent pas se retrouver enregistrées par surprise.
    /// </summary>
    private async Task PersistPreferenceAsync(Action<AppConfiguration> change)
    {
        await _saveGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var persisted = await _store.LoadAsync().ConfigureAwait(true);
            change(persisted);
            await _store.SaveAsync(persisted).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetStatus(() => Loc.F("Status_ThemeFailed", ex.Message));
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void OnActivity(ActivityEntry entry)
    {
        Activity.Insert(0, entry);
        while (Activity.Count > 200) Activity.RemoveAt(Activity.Count - 1);

        if (entry.IsError) ErrorCount++;
        else if (entry.Kind == ActivityKind.FileHandled) FilesHandled++;

        LastEventLabel = entry.Time;
        OnPropertyChanged(nameof(LastEventSummary));

        if (entry.RuleId is not { } id) return;

        // Le compteur par règle rend visible, dans la surveillance, laquelle travaille
        // vraiment — une règle qui ne se déclenche jamais est presque toujours une erreur.
        foreach (var rule in Rules)
        {
            if (rule.Id != id) continue;
            rule.TriggerCount++;
            break;
        }
    }

    public async Task LoadAsync()
    {
        _configuration = await _store.LoadAsync().ConfigureAwait(true);

        // Le drapeau évite de réécrire le fichier avec les valeurs qu'on vient d'en lire,
        // et empêche le chargement de passer pour une modification non enregistrée.
        _loading = true;
        try
        {
            SelectedTheme = ThemeOption.For(_configuration.Theme);
            SelectedLanguage = LanguageOption.For(_configuration.Language);
            DebounceMilliseconds = _configuration.DebounceMilliseconds;

            Folders.Clear();
            foreach (var folder in _configuration.Folders) Folders.Add(new WatchedFolderViewModel(folder));

            Rules.Clear();
            foreach (var rule in _configuration.Rules.OrderBy(r => r.Order)) Rules.Add(new RuleViewModel(rule));
        }
        finally
        {
            _loading = false;
        }

        SelectedRule = Rules.FirstOrDefault();
        SelectedFolder = Folders.FirstOrDefault();
        IsDirty = false;

        _automation.ApplyConfiguration(BuildConfiguration());
        SetStatus(DescribeState);
    }

    private string DescribeState()
    {
        if (Folders.Count == 0) return Loc.T("Status_ChooseFolder");
        if (Rules.Count == 0) return Loc.T("Status_CreateRule");

        var rules = Rules.Count > 1 ? Loc.F("Status_Rules_Many", Rules.Count) : Loc.T("Status_Rules_One");
        var folders = Folders.Count > 1
            ? Loc.F("Status_Folders_Many", Folders.Count)
            : Loc.T("Status_Folders_One");

        return Loc.F("Status_Counts", rules, folders);
    }

    private AppConfiguration BuildConfiguration()
    {
        _configuration.Folders = [.. Folders.Select(f => f.ToModel())];
        _configuration.Rules = [.. Rules.Select((r, index) => r.ToModel(index))];
        _configuration.Theme = SelectedTheme.Value;
        _configuration.Language = SelectedLanguage.Value;
        _configuration.DebounceMilliseconds = Math.Clamp(DebounceMilliseconds, 100, 30_000);
        return _configuration;
    }

    private async Task SaveConfigurationAsync(bool automatic)
    {
        if (!automatic) _autoSaveCancellation?.Cancel();

        await _saveGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var version = _changeVersion;
            var configuration = BuildConfiguration();
            await _store.SaveAsync(configuration).ConfigureAwait(true);
            _automation.ApplyConfiguration(configuration);

            if (version == _changeVersion)
            {
                IsDirty = false;
                SetStatus(() => Loc.F(automatic ? "Status_AutoSaved" : "Status_Saved", _store.Location));
            }
            else
            {
                // Une saisie est arrivée pendant l'écriture : elle reste signalée et sera
                // enregistrée après la même courte pause, sans écraser la version récente.
                MarkDirty();
            }
        }
        catch (Exception ex)
        {
            SetStatus(() => Loc.F("Status_SaveFailed", ex.Message));
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// Attend la dernière sauvegarde avant l'arrêt réel de l'application. Fermer la fenêtre
    /// ne passe pas ici : elle est seulement masquée, tandis que « Quitter » arrête le démon.
    /// </summary>
    public async Task SavePendingAsync()
    {
        _autoSaveCancellation?.Cancel();
        if (IsDirty) await SaveConfigurationAsync(automatic: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task SaveAsync() => SaveConfigurationAsync(automatic: false);

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var path = await _folderPicker.PickFolderAsync(Loc.T("Common_PickFolderTitle")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        if (Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus(() => Loc.T("Status_FolderAlready"));
            return;
        }

        var folder = new WatchedFolderViewModel(new WatchedFolder { Path = path });
        Folders.Add(folder);
        SelectedFolder = folder;
    }

    [RelayCommand]
    private void RemoveFolder(WatchedFolderViewModel? folder)
    {
        if (folder is null) return;
        Folders.Remove(folder);
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = RuleViewModel.CreateNew();
        Rules.Add(rule);

        // Une règle vide n'a rien à dire dans une liste : on ouvre directement son flux,
        // d'où qu'on vienne.
        EditRule(rule);
    }

    [RelayCommand]
    private void RemoveRule(RuleViewModel? rule)
    {
        if (rule is null) return;

        Rules.Remove(rule);

        // Supprimer la règle ouverte ramène à la liste : il n'y a plus de flux à montrer.
        if (ReferenceEquals(rule, SelectedRule))
        {
            SelectedRule = null;
            IsFlowOpen = false;
        }
    }

    [RelayCommand]
    private void DuplicateRule(RuleViewModel? rule)
    {
        if (rule is null) return;

        var clone = rule.ToModel();
        clone.Id = Guid.NewGuid().ToString("n");
        clone.Name = Loc.F("Rules_Copy_Suffix", clone.Name);

        var copy = new RuleViewModel(clone);
        Rules.Add(copy);
    }

    [RelayCommand]
    private async Task BrowseFolderAsync(IBrowsableFolder? target)
    {
        if (target is null) return;

        var path = await _folderPicker.PickFolderAsync(target.BrowseTitle).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path)) target.FolderPath = path;
    }

    [RelayCommand]
    private async Task BrowseExecutableAsync(RunCommandActionViewModel? target)
    {
        if (target is null) return;

        var path = await _folderPicker
            .PickFileAsync(Loc.T("Common_PickProgramTitle"), Loc.T("Common_ProgramFilter"), "*.exe")
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(path)) target.Executable = path;
    }

    /// <summary>
    /// Essai à blanc sur un fichier réel : dit si la règle s'appliquerait et ce qu'elle
    /// ferait, sans toucher au disque. C'est ce qui permet d'écrire une règle de
    /// suppression sans avoir à la tester sur de vrais fichiers.
    /// </summary>
    [RelayCommand]
    private async Task TestRuleAsync()
    {
        if (SelectedRule is not { } rule) return;

        var path = await _folderPicker
            .PickFileAsync(Loc.T("Common_PickSampleFile"))
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                TestMatched = false;
                TestResult = Loc.T("Test_Gone");
                return;
            }

            var file = new FileContext(
                info.FullName,
                info.DirectoryName ?? string.Empty,
                info.Length,
                new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));

            // Moteur sans gestionnaire d'action : on évalue les conditions, on n'exécute rien.
            var engine = new RuleEngine(ConditionDispatcher.CreateDefault(), []);
            var matched = await engine.MatchesAsync(rule.ToModel(), file).ConfigureAwait(true);

            TestMatched = matched;
            TestResult = matched
                ? Loc.F("Test_Matches", DescribeOutcome(rule, file))
                : Loc.F("Test_NoMatch", info.Name);
        }
        catch (Exception ex)
        {
            TestMatched = false;
            TestResult = Loc.F("Test_Failed", ex.Message);
        }
    }

    /// <summary>Rend le résultat des actions sans les exécuter.</summary>
    private static string DescribeOutcome(RuleViewModel rule, FileContext file)
    {
        var now = DateTimeOffset.UtcNow;
        var name = file.FileName.ToString();
        var directory = file.DirectoryPath.ToString();

        var steps = new List<string>();

        foreach (var action in rule.Actions)
        {
            switch (action)
            {
                case RenameActionViewModel rename:
                {
                    var preview = new FileContext(
                        Path.Join(directory, name), directory, file.SizeBytes, file.CreatedUtc, file.ModifiedUtc);
                    name = TokenExpander.ExpandFileName(rename.Template, preview, now);
                    steps.Add(Loc.F("Test_Renamed", name));
                    break;
                }

                case MoveActionViewModel move:
                {
                    var preview = new FileContext(
                        Path.Join(directory, name), directory, file.SizeBytes, file.CreatedUtc, file.ModifiedUtc);
                    directory = TokenExpander.Expand(move.Destination, preview, now, 1, TokenScope.Path);
                    steps.Add(Loc.F("Test_Moved", Path.Join(directory, name)));
                    break;
                }

                case CopyActionViewModel copy:
                {
                    var preview = new FileContext(
                        Path.Join(directory, name), directory, file.SizeBytes, file.CreatedUtc, file.ModifiedUtc);
                    var target = TokenExpander.Expand(copy.Destination, preview, now, 1, TokenScope.Path);
                    steps.Add(Loc.F("Test_Copied", Path.Join(target, name)));
                    break;
                }

                case DeleteActionViewModel delete:
                    steps.Add(Loc.T(delete.UseRecycleBin ? "Test_Recycled" : "Test_Deleted"));
                    break;

                case RunCommandActionViewModel command:
                    steps.Add(Loc.F("Test_Command", Path.GetFileName(command.Executable)));
                    break;
            }
        }

        return steps.Count == 0
            ? Loc.T("Test_NoActions")
            : Loc.F("Test_Outcome", string.Join(Loc.T("Sum_Then"), steps));
    }

    [RelayCommand]
    private void ToggleRunning()
    {
        if (_automation.IsRunning) _automation.Stop();
        else _automation.Start();

        IsRunning = _automation.IsRunning;
        var running = IsRunning;
        SetStatus(() => Loc.T(running ? "Status_Watching" : "Status_Stopped"));
    }

    [RelayCommand]
    private void RunNow()
    {
        _automation.ApplyConfiguration(BuildConfiguration());
        _automation.RunNow();
        SetStatus(() => Loc.T("Status_RunNow"));
    }
}

/// <summary>Implémentations neutres pour l'aperçu de conception.</summary>
internal sealed class NullAutomationController : IAutomationController
{
    public bool IsRunning => false;
    public event Action<ActivityEntry>? Activity { add { } remove { } }
    public event Action? StateChanged { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public void ApplyConfiguration(AppConfiguration configuration) { }
    public void RunNow() { }
}

internal sealed class NullFolderPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public Task<string?> PickFileAsync(string title, string? filter = null, string? extension = null)
        => Task.FromResult<string?>(null);
}
