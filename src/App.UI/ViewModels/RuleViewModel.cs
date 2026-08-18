using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using Banog.Core.Model;
using Banog.UI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Banog.UI.ViewModels;

public partial class RuleViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial ConditionMatchMode Match { get; set; }
    [ObservableProperty] public partial bool StopProcessingOnMatch { get; set; }

    [ObservableProperty] public partial ConditionKind NewConditionKind { get; set; } = ConditionKind.Extension;
    [ObservableProperty] public partial ActionKind NewActionKind { get; set; } = ActionKind.Move;

    /// <summary>
    /// Nombre de fichiers traités par cette règle depuis l'ouverture de l'application.
    /// Compteur d'affichage pour le mode surveillance : rien n'est persisté.
    /// </summary>
    [ObservableProperty] public partial int TriggerCount { get; set; }

    public ObservableCollection<ConditionViewModel> Conditions { get; } = [];
    public ObservableCollection<ActionViewModel> Actions { get; } = [];

    public static ConditionMatchMode[] MatchOptions { get; } = Enum.GetValues<ConditionMatchMode>();
    public static ConditionKind[] ConditionKinds { get; } = Enum.GetValues<ConditionKind>();
    public static ActionKind[] ActionKinds { get; } = Enum.GetValues<ActionKind>();

    private readonly int _order;

    public RuleViewModel(Rule model)
    {
        Id = model.Id;
        Name = model.Name;
        Enabled = model.Enabled;
        Match = model.Match;
        StopProcessingOnMatch = model.StopProcessingOnMatch;
        _order = model.Order;

        foreach (var condition in model.Conditions) Conditions.Add(ConditionViewModel.FromModel(condition));
        foreach (var action in model.Actions) Actions.Add(ActionViewModel.FromModel(action));

        Track(Conditions);
        Track(Actions);
    }

    /// <summary>
    /// Une règle neuve est vide : ni condition ni action. L'utilisateur la compose
    /// lui-même, guidé par le flux — imposer une action « déplacer » par défaut laissait
    /// croire que la règle était déjà prête.
    /// </summary>
    public static RuleViewModel CreateNew() => new(new Rule
    {
        Name = Loc.T("Rules_NewRule_Name"),
    });

    // ---- Résumé lisible ------------------------------------------------------------

    /// <summary>
    /// Phrase affichée sous le nom dans la liste des règles : « si … alors … ».
    /// Sans elle, la liste n'affiche que des noms et il faut ouvrir chaque règle pour
    /// savoir ce qu'elle fait.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Conditions.Count == 0) return Loc.T("Sum_NoConditions");

            var builder = new StringBuilder();
            var separator = Loc.T(Match == ConditionMatchMode.All ? "Sum_And" : "Sum_Or");

            for (var i = 0; i < Conditions.Count; i++)
            {
                if (i > 0) builder.Append(separator);
                builder.Append(Conditions[i].Description);
            }

            builder.Append(Loc.T("Sum_Arrow"));

            if (Actions.Count == 0)
            {
                builder.Append(Loc.T("Sum_NoAction"));
            }
            else
            {
                for (var i = 0; i < Actions.Count; i++)
                {
                    if (i > 0) builder.Append(Loc.T("Sum_Then"));
                    builder.Append(Actions[i].Description);
                }
            }

            return builder.ToString();
        }
    }

    public bool HasConditions => Conditions.Count > 0;
    public bool HasActions => Actions.Count > 0;

    /// <summary>Ce que la règle a fait depuis le démarrage, en toutes lettres.</summary>
    public string TriggerLabel => TriggerCount switch
    {
        0 => Loc.T("Trigger_None"),
        1 => Loc.T("Trigger_One"),
        _ => Loc.F("Trigger_Many", TriggerCount),
    };

    public string StateLabel => Loc.T(Enabled ? "Rule_Active" : "Rule_Disabled");

    partial void OnTriggerCountChanged(int value) => OnPropertyChanged(nameof(TriggerLabel));

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(StateLabel));

    /// <summary>
    /// Le résumé dépend du contenu des deux collections et de l'état de chaque élément.
    /// On suit donc à la fois les ajouts/retraits et les modifications internes.
    /// </summary>
    private void Track(INotifyCollectionChanged collection)
    {
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

            RaiseSummaryChanged();
        };

        foreach (INotifyPropertyChanged item in (System.Collections.IEnumerable)collection)
        {
            item.PropertyChanged += OnChildChanged;
        }
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => RaiseSummaryChanged();

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasConditions));
        OnPropertyChanged(nameof(HasActions));
    }

    partial void OnMatchChanged(ConditionMatchMode value) => RaiseSummaryChanged();

    // ---- Édition -------------------------------------------------------------------

    [RelayCommand]
    private void AddCondition() => Conditions.Add(ConditionViewModel.Create(NewConditionKind));

    [RelayCommand]
    private void RemoveCondition(ConditionViewModel condition) => Conditions.Remove(condition);

    [RelayCommand]
    private void AddAction() => Actions.Add(ActionViewModel.Create(NewActionKind));

    [RelayCommand]
    private void RemoveAction(ActionViewModel action) => Actions.Remove(action);

    public Rule ToModel(int order) => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Match = Match,
        StopProcessingOnMatch = StopProcessingOnMatch,
        Order = order,
        Conditions = [.. Conditions.Select(c => c.ToModel())],
        Actions = [.. Actions.Select(a => a.ToModel())],
    };

    public Rule ToModel() => ToModel(_order);
}

public partial class WatchedFolderViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty] public partial string Path { get; set; }
    [ObservableProperty] public partial bool IncludeSubfolders { get; set; }
    [ObservableProperty] public partial bool Enabled { get; set; }

    public WatchedFolderViewModel(WatchedFolder model)
    {
        Id = model.Id;
        Path = model.Path;
        IncludeSubfolders = model.IncludeSubfolders;
        Enabled = model.Enabled;
    }

    /// <summary>Dernier segment du chemin, pour l'affichage. Le chemin complet reste en info-bulle.</summary>
    public string ShortName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path)) return Loc.T("Common_EmptyFolder");
            var trimmed = Path.TrimEnd('\\', '/');
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    partial void OnPathChanged(string value) => OnPropertyChanged(nameof(ShortName));

    public WatchedFolder ToModel() => new()
    {
        Id = Id,
        Path = Path,
        IncludeSubfolders = IncludeSubfolders,
        Enabled = Enabled,
    };
}
