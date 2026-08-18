using System.Runtime.Versioning;
using Avalonia;
using Banog.Core.Abstractions;
using Banog.Core.Engine;
using Banog.Core.Storage;
using Banog.Host.Automation;
using Banog.Host.Platform;
using Banog.UI;
using Banog.UI.Services;
using Banog.UI.ViewModels;

namespace Banog.Host;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string InstanceMutexName = @"Local\Banog.SingleInstance.v1";
    private const string ShowWindowEventName = @"Local\Banog.ShowWindow.v1";

    private static AutomationController? _automation;

    [STAThread]
    public static int Main(string[] args)
    {
        var startInBackground = args.Any(IsBackgroundFlag);

        // Une seule instance : deux processus qui surveillent le même dossier traiteraient
        // chaque fichier deux fois. Un second lancement ne démarre donc rien : sans
        // --background, il demande à la première instance de rouvrir sa fenêtre ; avec
        // (cas du démarrage de session), il s'efface en silence.
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            if (!startInBackground) showWindow.Set();
            return 0;
        }

        var store = new JsonConfigurationStore();
        var log = new ActivityLog();

        IFileSystem fileSystem = new PhysicalFileSystem();
        IProcessRunner processRunner = new ProcessRunner();

        var engine = RuleEngine.CreateDefault(fileSystem, processRunner, SystemClock.Instance, log);

        // Le debounce vient de la configuration ; on la lit une fois ici pour dimensionner
        // le watcher, l'UI la rechargera ensuite pour l'édition.
        var configuration = store.LoadAsync().GetAwaiter().GetResult();
        var debounce = TimeSpan.FromMilliseconds(Math.Clamp(configuration.DebounceMilliseconds, 100, 30_000));

        _automation = new AutomationController(engine, fileSystem, debounce, log);
        _automation.ApplyConfiguration(configuration);
        log.Sink = entry => _automation.Publish(entry);

        var theme = new ThemeService();
        var startup = new StartupRegistration();
        // Le démarrage avec Windows est obligatoire : la clé est toujours réalignée sur
        // l'exécutable courant et lance Banog sans fenêtre.
        startup.EnsureRegistered();
        App.ThemeService = theme;
        App.InitialTheme = configuration.Theme;
        App.StartInBackground = startInBackground;
        App.Automation = _automation;
        App.ShowWindowSignal = showWindow;

        // La langue par défaut est celle de Windows : c'est l'hôte qui sait la demander.
        Banog.UI.Localization.Loc.SystemLanguageProvider = () => SystemLanguageProbe.Current;
        App.InitialLanguage = configuration.Language;
        App.ViewModelFactory = picker => new MainWindowViewModel(store, _automation, picker, theme);

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args.Where(a => !IsBackgroundFlag(a)).ToArray());
        }
        finally
        {
            _automation.Dispose();
        }
    }

    private static bool IsBackgroundFlag(string arg) =>
        arg.Equals("--background", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--daemon", StringComparison.OrdinalIgnoreCase);

    /// <summary>Point d'entrée utilisé aussi par les outils de conception Avalonia.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
