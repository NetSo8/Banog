using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Banog.Core.Model;
using Banog.UI.Localization;
using Banog.UI.Services;
using Banog.UI.ViewModels;
using Banog.UI.Views;

namespace Banog.UI;

public sealed partial class App : Application
{
    /// <summary>
    /// Fabrique injectée par l'hôte. Le picker est fourni par la vue une fois la fenêtre
    /// créée : c'est lui qui porte le StorageProvider d'Avalonia.
    /// </summary>
    public static Func<IFolderPicker, MainWindowViewModel>? ViewModelFactory { get; set; }

    /// <summary>Service d'apparence partagé, fourni par l'hôte.</summary>
    public static IThemeService? ThemeService { get; set; }

    /// <summary>
    /// Préférence lue sur disque par l'hôte. Elle est appliquée avant la création de la
    /// fenêtre : sans cela, une app réglée en clair apparaîtrait une fraction de seconde
    /// en sombre au démarrage.
    /// </summary>
    public static ThemePreference InitialTheme { get; set; } = ThemePreference.System;

    /// <summary>Langue lue sur disque par l'hôte, appliquée avant la première fenêtre.</summary>
    public static LanguagePreference InitialLanguage { get; set; } = LanguagePreference.System;

    /// <summary>Moteur en marche, fourni par l'hôte : le plateau le pilote sans fenêtre.</summary>
    public static IAutomationController? Automation { get; set; }

    /// <summary>
    /// Vrai quand Banog a été lancé en arrière-plan (<c>--background</c>) : pas de fenêtre
    /// au démarrage, la surveillance part immédiatement et seule la zone de notification
    /// témoigne de l'application.
    /// </summary>
    public static bool StartInBackground { get; set; }

    /// <summary>
    /// Signal posé par une seconde instance pour demander la réouverture de la fenêtre.
    /// La première instance l'écoute ; les suivantes s'effacent après l'avoir levé.
    /// </summary>
    public static EventWaitHandle? ShowWindowSignal { get; set; }

    private MainWindowViewModel? _viewModel;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private TrayIcon? _tray;
    private NativeMenuItem? _trayToggle;
    private DispatcherTimer? _showSignalTimer;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ThemeService?.Apply(InitialTheme);
        Loc.Apply(InitialLanguage);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            // Fermer la fenêtre ne quitte plus : la surveillance continue sous l'icône de
            // la zone de notification, et seul « Quitter » du plateau arrête l'application.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTray(desktop);

            if (StartInBackground)
            {
                Automation?.Start();
            }
            else
            {
                desktop.MainWindow = CreateWindow(desktop);
            }

            // Changer de langue reconstruit la fenêtre sur le même viewmodel : tout ce qui
            // est traduit à l'affichage — libellés d'énumérations, résumés de règles — est
            // relu, sans que l'utilisateur ait à redémarrer ni à perdre son travail. Le
            // plateau est réécrit dans la même occasion.
            Loc.Changed += () =>
            {
                RefreshTrayTexts();

                if (_viewModel is null) return;

                var previous = desktop.MainWindow;
                desktop.MainWindow = CreateWindow(desktop, _viewModel);
                desktop.MainWindow.Show();
                previous?.Close();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Construit la fenêtre et, la première fois seulement, le viewmodel : le picker
    /// dépend de la fenêtre, mais l'état d'édition ne doit pas dépendre de la langue.
    /// </summary>
    private MainWindow CreateWindow(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel? existing = null)
    {
        var window = new MainWindow();
        var picker = new StorageProviderFolderPicker(window);

        // La même image sert au plateau et à la fenêtre : une seule icône à dessiner.
        window.Icon = TrayIconImage.Create();

        if (existing is null)
        {
            _viewModel = ViewModelFactory?.Invoke(picker) ?? new MainWindowViewModel();
            window.Opened += async (_, _) => await _viewModel.LoadAsync();
        }
        else
        {
            // Le sélecteur de dossiers s'adosse à la nouvelle fenêtre : l'ancienne va
            // disparaître, une boîte de dialogue accrochée à elle ne s'ouvrirait plus.
            _viewModel = existing;
            existing.Rebind(picker);
        }

        window.DataContext = _viewModel;
        return window;
    }

    // ---- Zone de notification -------------------------------------------------------------

    /// <summary>
    /// Le plateau est le seul visage de l'application fenêtre fermée : il ouvre, il pilote
    /// la surveillance, il quitte. Le tout tient en menu natif, sans aucune vue.
    /// </summary>
    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem { Header = Loc.T("Tray_Open") };
        open.Click += (_, _) => ShowWindow();

        // Le libellé dit l'action, pas l'état : « Démarrer » quand en pause, l'inverse sinon.
        _trayToggle = new NativeMenuItem();
        _trayToggle.Click += (_, _) => ToggleRunning();

        var quit = new NativeMenuItem { Header = Loc.T("Tray_Quit") };
        quit.Click += async (_, _) =>
        {
            if (_viewModel is not null) await _viewModel.SavePendingAsync();
            desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = TrayIconImage.Create(),
            Menu = menu,
        };
        _tray.Clicked += (_, _) => ShowWindow();

        TrayIcon.GetIcons(this)?.Add(_tray);

        if (Automation is { } automation) automation.StateChanged += RefreshTrayTexts;

        RefreshTrayTexts();

        // Une seconde instance demande « montre-toi » par un événement nommé : on l'écoute
        // sans thread dédié, d'un coup d'œil périodique sur l'événement.
        if (ShowWindowSignal is not null)
        {
            _showSignalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _showSignalTimer.Tick += (_, _) =>
            {
                if (ShowWindowSignal.WaitOne(0)) ShowWindow();
            };
            _showSignalTimer.Start();
        }
    }

    private void ToggleRunning()
    {
        if (Automation is not { } automation) return;

        if (automation.IsRunning) automation.Stop();
        else automation.Start();
    }

    private void RefreshTrayTexts()
    {
        if (_tray is null) return;

        _tray.ToolTipText = $"Banog — {Loc.T(Automation is { IsRunning: true } ? "State_Running" : "State_Paused")}";

        if (_trayToggle is not null)
        {
            _trayToggle.Header = Loc.T(Automation is { IsRunning: true } ? "Side_Pause" : "Side_Start");
        }
    }

    /// <summary>Rouvre la fenêtre, la crée au premier passage : en arrière-plan, elle n'existe pas encore.</summary>
    private void ShowWindow()
    {
        if (_desktop is null) return;

        _desktop.MainWindow ??= CreateWindow(_desktop);

        var window = _desktop.MainWindow;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
