using CommunityToolkit.Mvvm.ComponentModel;
using Banog.Core.Model;
using Banog.UI.Localization;

namespace Banog.UI.ViewModels;

public abstract class ActionViewModel : ObservableObject
{
    public abstract string DisplayName { get; }

    /// <summary>Résumé en une clause, pour la liste des règles.</summary>
    public abstract string Description { get; }

    /// <summary>Vrai pour les actions irréversibles : la vue les signale visuellement.</summary>
    public virtual bool IsDestructive => false;

    public abstract RuleAction ToModel();

    public static ActionViewModel FromModel(RuleAction action) => action switch
    {
        MoveAction a => new MoveActionViewModel(a),
        CopyAction a => new CopyActionViewModel(a),
        RenameAction a => new RenameActionViewModel(a),
        DeleteAction a => new DeleteActionViewModel(a),
        RecycleAction a => new RecycleActionViewModel(a),
        RunCommandAction a => new RunCommandActionViewModel(a),
        _ => new UnsupportedActionViewModel(action),
    };

    public static ActionViewModel Create(ActionKind kind) => kind switch
    {
        ActionKind.Move => new MoveActionViewModel(new MoveAction()),
        ActionKind.Copy => new CopyActionViewModel(new CopyAction()),
        ActionKind.Rename => new RenameActionViewModel(new RenameAction()),
        ActionKind.Delete => new DeleteActionViewModel(new DeleteAction()),
        ActionKind.Recycle => new RecycleActionViewModel(new RecycleAction()),
        ActionKind.RunCommand => new RunCommandActionViewModel(new RunCommandAction()),
        _ => new MoveActionViewModel(new MoveAction()),
    };

    public static ConflictPolicy[] ConflictOptions { get; } = Enum.GetValues<ConflictPolicy>();
}

public enum ActionKind
{
    Move,
    Copy,
    Rename,
    Delete,
    Recycle,
    RunCommand,
}

/// <summary>
/// Découpe une destination en deux morceaux que l'interface sait présenter séparément :
/// le dossier qu'on choisit au parcours, et le sous-dossier composé de jetons.
///
/// « D:\Archives\{date:yyyy}\factures » se lit « dans D:\Archives, ranger sous
/// 2026\factures » — deux questions distinctes, posées séparément.
/// </summary>
internal static class DestinationTemplate
{
    private static readonly char[] Separators = ['\\', '/'];

    public static (string Base, string Subfolder) Split(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return (string.Empty, string.Empty);

        var token = destination.IndexOf('{');
        if (token < 0) return (destination, string.Empty);

        var cut = destination.LastIndexOfAny(Separators, token);
        return cut < 0
            ? (string.Empty, destination)
            : (destination[..cut], destination[(cut + 1)..]);
    }

    public static string Join(string basePath, string subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder)) return basePath;
        if (string.IsNullOrWhiteSpace(basePath)) return subfolder;

        return $"{basePath.TrimEnd(Separators)}\\{subfolder}";
    }
}

/// <summary>
/// Socle commun de « Déplacer » et « Copier » : un dossier choisi au parcours, et un
/// sous-dossier composé de pastilles.
/// </summary>
public abstract partial class FolderDestinationActionViewModel : ActionViewModel, Services.IBrowsableFolder
{
    public string BrowseTitle => Loc.T("Common_PickDestination");

    public string FolderPath
    {
        get => BasePath;
        set => BasePath = value;
    }

    /// <summary>Le dossier fixe, celui qu'on choisit une fois.</summary>
    [ObservableProperty] public partial string BasePath { get; set; } = string.Empty;

    [ObservableProperty] public partial bool CreateDirectories { get; set; }
    [ObservableProperty] public partial ConflictPolicy OnConflict { get; set; }

    /// <summary>Le sous-dossier, éventuellement composé de la date, du dossier d'origine…</summary>
    public NameTemplateBuilderViewModel Subfolder { get; }

    protected FolderDestinationActionViewModel(string destination, bool createDirectories, ConflictPolicy onConflict)
    {
        var (basePath, subfolder) = DestinationTemplate.Split(destination);

        // Le sous-dossier d'abord : affecter BasePath déclenche OnBasePathChanged, qui
        // s'adresse déjà à lui.
        Subfolder = new NameTemplateBuilderViewModel(TemplateTarget.Folder, subfolder)
        {
            PreviewPrefix = basePath,
        };

        BasePath = basePath;
        CreateDirectories = createDirectories;
        OnConflict = onConflict;

        Subfolder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(NameTemplateBuilderViewModel.Template)) return;

            OnPropertyChanged(nameof(Destination));
            OnPropertyChanged(nameof(Description));
        };
    }

    /// <summary>Ce que le moteur reçoit : le dossier et le sous-dossier réunis.</summary>
    public string Destination => DestinationTemplate.Join(BasePath, Subfolder.Template);

    public bool HasBasePath => !string.IsNullOrWhiteSpace(BasePath);

    /// <summary>
    /// Ce qu'affiche le bouton de choix : le dossier retenu, ou l'invitation à en choisir
    /// un. Le chemin ne se tape pas — un chemin écrit à la main est une source d'erreurs
    /// qu'on ne découvre qu'au moment où un fichier part au mauvais endroit.
    /// </summary>
    public string BasePathDisplay => HasBasePath ? BasePath : Loc.T("Common_ChooseFolder");

    /// <summary>
    /// Résumé de la destination pour la liste des règles : le dossier tel qu'il serait
    /// vraiment, pas le gabarit. Personne n'a à lire « {date:yyyy} » pour savoir où
    /// vont ses fichiers.
    /// </summary>
    protected string DescribeDestination(string key)
    {
        var destination = Subfolder.Preview;
        return Loc.F(key, destination.Length == 0 ? Loc.T("Desc_Placeholder") : destination);
    }

    partial void OnBasePathChanged(string value)
    {
        Subfolder.PreviewPrefix = value;

        OnPropertyChanged(nameof(Destination));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasBasePath));
        OnPropertyChanged(nameof(BasePathDisplay));
    }
}

public sealed class MoveActionViewModel : FolderDestinationActionViewModel
{
    public override string DisplayName => Loc.T("Act_MoveKind");

    public override string Description => DescribeDestination("Desc_Move");

    public MoveActionViewModel(MoveAction model)
        : base(model.Destination, model.CreateDirectories, model.OnConflict)
    {
    }

    public override RuleAction ToModel() => new MoveAction
    {
        Destination = Destination,
        CreateDirectories = CreateDirectories,
        OnConflict = OnConflict,
    };
}

public sealed class CopyActionViewModel : FolderDestinationActionViewModel
{
    public override string DisplayName => Loc.T("Act_CopyKind");

    public override string Description => DescribeDestination("Desc_Copy");

    public CopyActionViewModel(CopyAction model)
        : base(model.Destination, model.CreateDirectories, model.OnConflict)
    {
    }

    public override RuleAction ToModel() => new CopyAction
    {
        Destination = Destination,
        CreateDirectories = CreateDirectories,
        OnConflict = OnConflict,
    };
}

public partial class RenameActionViewModel : ActionViewModel
{
    public override string DisplayName => Loc.T("Act_Rename");

    // « par ex. » compte : sans lui, le résumé se lit comme si tous les fichiers
    // prenaient ce nom-là.
    public override string Description => Template.Length > 0
        ? Loc.F("Desc_Rename", Builder.Preview)
        : Loc.T("Desc_Rename_Plain");

    /// <summary>
    /// Le gabarit lu par le moteur. Il n'est plus tapé : <see cref="Builder"/> l'écrit à
    /// partir des pastilles, et le relit quand il vient du fichier de configuration.
    /// </summary>
    [ObservableProperty] public partial string Template { get; set; } = string.Empty;

    [ObservableProperty] public partial ConflictPolicy OnConflict { get; set; }

    public NameTemplateBuilderViewModel Builder { get; }

    public RenameActionViewModel(RenameAction model)
    {
        Template = model.Template;
        OnConflict = model.OnConflict;

        Builder = new NameTemplateBuilderViewModel(TemplateTarget.FileName, model.Template);
        Builder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(NameTemplateBuilderViewModel.Template)) return;

            Template = Builder.Template;
            OnPropertyChanged(nameof(Description));
        };
    }

    public override RuleAction ToModel() => new RenameAction
    {
        Template = Template,
        OnConflict = OnConflict,
    };
}

public partial class DeleteActionViewModel : ActionViewModel
{
    public override string DisplayName => Loc.T("Act_DeleteKind");

    public override string Description => Loc.T(UseRecycleBin ? "Desc_Recycle" : "Desc_Delete");

    /// <summary>Seule la suppression sans corbeille est réellement sans retour.</summary>
    public override bool IsDestructive => !UseRecycleBin;

    [ObservableProperty] public partial bool UseRecycleBin { get; set; }

    partial void OnUseRecycleBinChanged(bool value) => OnPropertyChanged(nameof(IsDestructive));

    public DeleteActionViewModel(DeleteAction model) => UseRecycleBin = model.UseRecycleBin;

    public override RuleAction ToModel() => new DeleteAction { UseRecycleBin = UseRecycleBin };
}

public sealed class RecycleActionViewModel : ActionViewModel
{
    public override string DisplayName => Loc.T("Act_RecycleKind");
    public override string Description => Loc.T("Desc_Recycle");

    public RecycleActionViewModel(RecycleAction model) { }

    public override RuleAction ToModel() => new RecycleAction();
}

public partial class RunCommandActionViewModel : ActionViewModel
{
    public override string DisplayName => Loc.T("Act_RunKind");

    public override string Description => Loc.F("Desc_Run", string.IsNullOrWhiteSpace(Executable)
        ? Loc.T("Desc_Placeholder")
        : System.IO.Path.GetFileName(Executable));

    [ObservableProperty] public partial string Executable { get; set; } = string.Empty;

    /// <summary>Le programme retenu, ou l'invitation à en choisir un. Voir le choix de dossier.</summary>
    public string ExecutableDisplay => string.IsNullOrWhiteSpace(Executable)
        ? Loc.T("Common_ChooseProgram")
        : Executable;

    partial void OnExecutableChanged(string value) => OnPropertyChanged(nameof(ExecutableDisplay));

    [ObservableProperty] public partial string Arguments { get; set; } = string.Empty;
    [ObservableProperty] public partial string WorkingDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial bool WaitForExit { get; set; }

    public RunCommandActionViewModel(RunCommandAction model)
    {
        Executable = model.Executable;
        Arguments = model.Arguments;
        WorkingDirectory = model.WorkingDirectory ?? string.Empty;
        WaitForExit = model.WaitForExit;
    }

    public override RuleAction ToModel() => new RunCommandAction
    {
        Executable = Executable,
        Arguments = Arguments,
        WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory,
        WaitForExit = WaitForExit,
    };
}

/// <summary>Action inconnue de cette version, préservée à l'identique au réenregistrement.</summary>
public sealed class UnsupportedActionViewModel(RuleAction model) : ActionViewModel
{
    public override string DisplayName => Loc.F("Desc_NotEditable", model.Type);
    public override string Description => Loc.F("Desc_Unsupported_Act", model.Type);
    public override RuleAction ToModel() => model;
}
