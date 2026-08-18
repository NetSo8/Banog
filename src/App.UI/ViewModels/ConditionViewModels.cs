using CommunityToolkit.Mvvm.ComponentModel;
using Banog.Core.Model;
using Banog.UI.Localization;

namespace Banog.UI.ViewModels;

/// <summary>
/// Surface d'édition d'une condition. Chaque type de condition a son viewmodel et son
/// DataTemplate : ajouter une condition (contenu, OCR) revient à ajouter une paire
/// viewmodel + template, sans toucher au reste de l'UI ni au moteur.
/// </summary>
public abstract partial class ConditionViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool Negate { get; set; }

    /// <summary>Libellé affiché dans le sélecteur de type.</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Résumé en une clause, tel qu'il apparaît dans la liste des règles.
    /// Permet de lire ce que fait une règle sans l'ouvrir.
    /// </summary>
    public string Description => Negate ? Loc.F("Sum_Unless", Clause) : Clause;

    /// <summary>
    /// La clause affirmative. La négation est rendue une fois pour toutes par « sauf si »
    /// devant : conjuguer chaque verbe au négatif ne se traduit pas d'une langue à l'autre.
    /// </summary>
    protected abstract string Clause { get; }

    public abstract RuleCondition ToModel();

    public static ConditionViewModel FromModel(RuleCondition condition) => condition switch
    {
        ExtensionCondition c => new ExtensionConditionViewModel(c),
        NameCondition c => new NameConditionViewModel(c),
        DateCondition c => new DateConditionViewModel(c),
        SizeCondition c => new SizeConditionViewModel(c),
        SourceFolderCondition c => new SourceFolderConditionViewModel(c),
        // Les groupes imbriqués existent dans le format mais ne sont pas éditables en v1 :
        // le ET/OU au niveau de la règle couvre les cas courants.
        _ => new UnsupportedConditionViewModel(condition),
    };

    public static ConditionViewModel Create(ConditionKind kind) => kind switch
    {
        ConditionKind.Extension => new ExtensionConditionViewModel(new ExtensionCondition()),
        ConditionKind.Name => new NameConditionViewModel(new NameCondition()),
        ConditionKind.Date => new DateConditionViewModel(new DateCondition()),
        ConditionKind.Size => new SizeConditionViewModel(new SizeCondition()),
        ConditionKind.SourceFolder => new SourceFolderConditionViewModel(new SourceFolderCondition()),
        _ => new ExtensionConditionViewModel(new ExtensionCondition()),
    };
}

public enum ConditionKind
{
    Extension,
    Name,
    Date,
    Size,
    SourceFolder,
}

public partial class ExtensionConditionViewModel : ConditionViewModel
{
    public override string DisplayName => Loc.T("Cond_Type");

    protected override string Clause
    {
        get
        {
            var list = Types.HasSelection ? string.Join(", ", Types.Extensions) : Loc.T("Desc_Placeholder");
            return Loc.F(Match == ExtensionMatch.IsOneOf ? "Desc_Type" : "Desc_Type_Not", list);
        }
    }

    [ObservableProperty]
    public partial ExtensionMatch Match { get; set; }

    /// <summary>
    /// Les types retenus. La liste « pdf, png, jpg » tapée à la main a laissé place à un
    /// choix guidé : voir <see cref="FileTypePickerViewModel"/>.
    /// </summary>
    public FileTypePickerViewModel Types { get; }

    public ExtensionConditionViewModel(ExtensionCondition model)
    {
        Negate = model.Negate;
        Match = model.Match;
        Types = new FileTypePickerViewModel(model.Extensions);

        // Le résumé de la règle et le témoin « non enregistré » suivent le contenu du
        // sélecteur, qui est un objet distinct.
        Types.SelectionChanged += () => OnPropertyChanged(nameof(Description));
    }

    public static ExtensionMatch[] MatchOptions { get; } = Enum.GetValues<ExtensionMatch>();

    public override RuleCondition ToModel() => new ExtensionCondition
    {
        Negate = Negate,
        Match = Match,
        Extensions = [.. Types.Extensions],
    };
}

public partial class NameConditionViewModel : ConditionViewModel
{
    public override string DisplayName => Loc.T("Cond_Name");

    protected override string Clause => Loc.F("Desc_Name", Labels.For(Mode), Value);

    [ObservableProperty] public partial NameTarget Target { get; set; }
    [ObservableProperty] public partial TextMatchMode Mode { get; set; }
    [ObservableProperty] public partial string Value { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CaseSensitive { get; set; }

    public NameConditionViewModel(NameCondition model)
    {
        Negate = model.Negate;
        Target = model.Target;
        Mode = model.Mode;
        Value = model.Value;
        CaseSensitive = model.CaseSensitive;
    }

    public static NameTarget[] TargetOptions { get; } = Enum.GetValues<NameTarget>();
    public static TextMatchMode[] ModeOptions { get; } = Enum.GetValues<TextMatchMode>();

    public override RuleCondition ToModel() => new NameCondition
    {
        Negate = Negate,
        Target = Target,
        Mode = Mode,
        Value = Value,
        CaseSensitive = CaseSensitive,
    };
}

public partial class DateConditionViewModel : ConditionViewModel
{
    public override string DisplayName => Loc.T("Cond_Date");

    protected override string Clause => UsesDuration
        ? Loc.F("Desc_Date_Duration",
            Labels.For(Field), Labels.For(Comparison), Amount.ToString("0.##"), Labels.For(Unit))
        : Loc.F("Desc_Date_Instant",
            Labels.For(Field), Labels.For(Comparison), Instant?.ToLocalTime().ToString("d") ?? "…");

    [ObservableProperty] public partial DateField Field { get; set; }
    [ObservableProperty] public partial DateComparison Comparison { get; set; }
    [ObservableProperty] public partial double Amount { get; set; }
    [ObservableProperty] public partial TimeUnit Unit { get; set; }
    [ObservableProperty] public partial DateTimeOffset? Instant { get; set; }

    public DateConditionViewModel(DateCondition model)
    {
        Negate = model.Negate;
        Field = model.Field;
        Comparison = model.Comparison;
        Amount = model.Amount;
        Unit = model.Unit;
        Instant = model.Instant;
    }

    /// <summary>Vrai pour OlderThan/NewerThan : l'UI bascule entre durée et date absolue.</summary>
    public bool UsesDuration => Comparison is DateComparison.OlderThan or DateComparison.NewerThan;

    partial void OnComparisonChanged(DateComparison value)
    {
        OnPropertyChanged(nameof(UsesDuration));
        OnPropertyChanged(nameof(UsesInstant));
    }

    public bool UsesInstant => !UsesDuration;

    public static DateField[] FieldOptions { get; } = Enum.GetValues<DateField>();
    public static DateComparison[] ComparisonOptions { get; } = Enum.GetValues<DateComparison>();
    public static TimeUnit[] UnitOptions { get; } = Enum.GetValues<TimeUnit>();

    public override RuleCondition ToModel() => new DateCondition
    {
        Negate = Negate,
        Field = Field,
        Comparison = Comparison,
        Amount = Amount,
        Unit = Unit,
        Instant = Instant,
    };
}

public partial class SizeConditionViewModel : ConditionViewModel
{
    public override string DisplayName => Loc.T("Cond_Size");

    protected override string Clause =>
        Loc.F("Desc_Size", Labels.For(Comparison), Value.ToString("0.##"), Labels.For(Unit));

    [ObservableProperty] public partial NumericComparison Comparison { get; set; }
    [ObservableProperty] public partial double Value { get; set; }
    [ObservableProperty] public partial SizeUnit Unit { get; set; }

    public SizeConditionViewModel(SizeCondition model)
    {
        Negate = model.Negate;
        Comparison = model.Comparison;
        Value = model.Value;
        Unit = model.Unit;
    }

    public static NumericComparison[] ComparisonOptions { get; } = Enum.GetValues<NumericComparison>();
    public static SizeUnit[] UnitOptions { get; } = Enum.GetValues<SizeUnit>();

    public override RuleCondition ToModel() => new SizeCondition
    {
        Negate = Negate,
        Comparison = Comparison,
        Value = Value,
        Unit = Unit,
    };
}

public partial class SourceFolderConditionViewModel : ConditionViewModel, Services.IBrowsableFolder
{
    public override string DisplayName => Loc.T("Cond_Source");

    protected override string Clause => Loc.F("Desc_Source", string.IsNullOrWhiteSpace(Path)
        ? Loc.T("Desc_Placeholder")
        : System.IO.Path.GetFileName(Path.TrimEnd('\\')));

    [ObservableProperty] public partial string Path { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IncludeSubfolders { get; set; }

    /// <summary>Le dossier retenu, ou l'invitation à en choisir un : il ne se tape pas.</summary>
    public string PathDisplay => string.IsNullOrWhiteSpace(Path) ? Loc.T("Common_ChooseFolder") : Path;

    partial void OnPathChanged(string value) => OnPropertyChanged(nameof(PathDisplay));

    public string BrowseTitle => Loc.T("Common_PickSourceFolder");

    public string FolderPath
    {
        get => Path;
        set => Path = value;
    }

    public SourceFolderConditionViewModel(SourceFolderCondition model)
    {
        Negate = model.Negate;
        Path = model.Path;
        IncludeSubfolders = model.IncludeSubfolders;
    }

    public override RuleCondition ToModel() => new SourceFolderCondition
    {
        Negate = Negate,
        Path = Path,
        IncludeSubfolders = IncludeSubfolders,
    };
}

/// <summary>
/// Condition d'un type que cette version ne sait pas éditer (fichier écrit par une version
/// plus récente). Elle est conservée telle quelle : ouvrir un fichier de règles récent
/// ne doit jamais en détruire le contenu.
/// </summary>
public sealed class UnsupportedConditionViewModel(RuleCondition model) : ConditionViewModel
{
    public override string DisplayName => Loc.F("Desc_NotEditable", model.Type);
    protected override string Clause => Loc.F("Desc_Unsupported_Cond", model.Type);
    public override RuleCondition ToModel() => model;
}
