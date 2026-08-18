using System.Collections.ObjectModel;
using Banog.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Banog.UI.ViewModels;

/// <summary>
/// Une ligne du sélecteur de types. La coche est portée par la ligne pour que le
/// sélecteur reste ouvert pendant qu'on en choisit plusieurs : cocher trois types ne
/// doit pas demander de rouvrir la liste trois fois.
/// </summary>
public partial class FileTypeItemViewModel : ObservableObject
{
    private readonly FileTypePickerViewModel _owner;

    public FileTypeOption Option { get; }

    public string Badge => Option.Badge;
    public string Label => Option.Label;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    public FileTypeItemViewModel(FileTypeOption option, FileTypePickerViewModel owner, bool isSelected)
    {
        Option = option;
        _owner = owner;
        IsSelected = isSelected;
    }

    [RelayCommand]
    private void Toggle() => _owner.Toggle(Option);
}

/// <summary>Une catégorie du sélecteur, avec son « tout ajouter ».</summary>
public partial class FileTypeGroupViewModel : ObservableObject
{
    private readonly FileTypePickerViewModel _owner;

    public string CategoryKey { get; }
    public string Category => Localization.Loc.T(CategoryKey);

    public ObservableCollection<FileTypeItemViewModel> Items { get; } = [];

    public FileTypeGroupViewModel(string categoryKey, FileTypePickerViewModel owner)
    {
        CategoryKey = categoryKey;
        _owner = owner;
    }

    /// <summary>Ajoute la catégorie entière : « toutes les images » est une intention courante.</summary>
    [RelayCommand]
    private void AddAll() => _owner.AddAll(Items.Select(i => i.Option));
}

/// <summary>
/// Choix des types de fichiers d'une règle.
///
/// Remplace la liste « pdf, png, jpg » tapée à la main : les types choisis sont des
/// pastilles qu'on retire d'un clic, et l'ajout passe par une recherche qui accepte
/// aussi bien « pdf » que « photo » ou « vidéo ». Une extension absente du catalogue
/// reste ajoutable telle quelle — l'interface guide sans enfermer.
/// </summary>
public partial class FileTypePickerViewModel : ObservableObject
{
    /// <summary>Types retenus, dans l'ordre où ils ont été ajoutés.</summary>
    public ObservableCollection<FileTypeOption> Selected { get; } = [];

    /// <summary>Résultats de la recherche, groupés par catégorie.</summary>
    public ObservableCollection<FileTypeGroupViewModel> Results { get; } = [];

    [ObservableProperty] public partial string Search { get; set; } = string.Empty;

    /// <summary>Le sélecteur reste replié tant qu'on ne cherche pas à ajouter un type.</summary>
    [ObservableProperty] public partial bool IsOpen { get; set; }

    /// <summary>Signalé au parent (condition) pour rafraîchir son résumé et l'état « non enregistré ».</summary>
    public event Action? SelectionChanged;

    public FileTypePickerViewModel(IEnumerable<string> extensions)
    {
        foreach (var extension in extensions)
        {
            var normalized = FileTypeCatalog.NormalizeExtension(extension);
            if (normalized.Length == 0) continue;
            if (Selected.Any(o => o.Extension == normalized)) continue;

            Selected.Add(FileTypeCatalog.Resolve(normalized));
        }

        Selected.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionLabel));
            SelectionChanged?.Invoke();
        };

        Rebuild();
    }

    public bool HasSelection => Selected.Count > 0;

    /// <summary>Ce que le bouton d'ouverture annonce quand la liste est repliée.</summary>
    public string SelectionLabel => Localization.Loc.T(Selected.Count switch
    {
        0 => "Types_Pick",
        1 => "Types_AddAnother",
        _ => "Types_Add",
    });

    public IEnumerable<string> Extensions => Selected.Select(o => o.Extension);

    /// <summary>
    /// L'extension tapée dans la recherche, si elle ressemble à une extension absente du
    /// catalogue. C'est la porte de sortie : un « .parquet » reste utilisable.
    /// </summary>
    public string CustomExtension
    {
        get
        {
            var candidate = FileTypeCatalog.NormalizeExtension(Search);

            if (candidate.Length is 0 or > 12) return string.Empty;
            if (!candidate.All(char.IsLetterOrDigit)) return string.Empty;
            if (Selected.Any(o => o.Extension == candidate)) return string.Empty;
            if (FileTypeCatalog.Find(candidate) is not null) return string.Empty;

            return candidate;
        }
    }

    /// <summary>
    /// L'ajout libre n'est proposé que si le catalogue n'a rien à offrir : tant qu'une
    /// recherche donne des résultats, la porte de sortie n'est qu'un bruit de plus.
    /// </summary>
    public bool CanAddCustom => CustomExtension.Length > 0 && Results.Count == 0;

    public string CustomLabel => Localization.Loc.F("Types_AddCustom", CustomExtension);

    public bool HasResults => Results.Count > 0;

    partial void OnSearchChanged(string value)
    {
        // Rebuild d'abord : « peut-on ajouter à la main » dépend du nombre de résultats.
        Rebuild();
        OnPropertyChanged(nameof(CustomExtension));
        OnPropertyChanged(nameof(CanAddCustom));
        OnPropertyChanged(nameof(CustomLabel));
    }

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
        Search = string.Empty;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Search = string.Empty;
    }

    [RelayCommand]
    private void Remove(FileTypeOption? option)
    {
        if (option is null) return;

        Selected.Remove(option);
        MarkSelection(option, selected: false);
    }

    [RelayCommand]
    private void Clear()
    {
        var removed = Selected.ToArray();
        Selected.Clear();
        foreach (var option in removed) MarkSelection(option, selected: false);
    }

    [RelayCommand]
    private void AddCustom()
    {
        if (CustomExtension is not { Length: > 0 } extension) return;

        Selected.Add(FileTypeCatalog.Resolve(extension));
        Search = string.Empty;
    }

    public void Toggle(FileTypeOption option)
    {
        var existing = Selected.FirstOrDefault(o => o.Extension == option.Extension);

        if (existing is not null)
        {
            Selected.Remove(existing);
            MarkSelection(option, selected: false);
            return;
        }

        Selected.Add(option);
        MarkSelection(option, selected: true);
    }

    public void AddAll(IEnumerable<FileTypeOption> options)
    {
        foreach (var option in options)
        {
            if (Selected.Any(o => o.Extension == option.Extension)) continue;

            Selected.Add(option);
            MarkSelection(option, selected: true);
        }
    }

    /// <summary>Garde les coches de la liste en accord avec les pastilles.</summary>
    private void MarkSelection(FileTypeOption option, bool selected)
    {
        foreach (var group in Results)
        {
            foreach (var item in group.Items)
            {
                if (item.Option.Extension == option.Extension) item.IsSelected = selected;
            }
        }
    }

    private void Rebuild()
    {
        Results.Clear();

        var query = FileTypeCatalog.Normalize(Search.Trim());
        var chosen = Selected.Select(o => o.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var category in FileTypeCatalog.Categories)
        {
            var group = new FileTypeGroupViewModel(category, this);

            foreach (var option in FileTypeCatalog.All)
            {
                if (option.CategoryKey != category) continue;
                if (query.Length > 0 && !option.SearchIndex.Contains(query, StringComparison.Ordinal)) continue;

                group.Items.Add(new FileTypeItemViewModel(option, this, chosen.Contains(option.Extension)));
            }

            if (group.Items.Count > 0) Results.Add(group);
        }

        OnPropertyChanged(nameof(HasResults));
    }
}
