using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Banog.Core.Model;
using Banog.UI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Banog.UI.ViewModels.Flow;

/// <summary>
/// La règle sélectionnée, vue comme un flux : le fichier entre à gauche, traverse les
/// conditions, puis les actions dans leur ordre d'exécution.
///
/// Le graphe est reconstruit uniquement quand la <em>structure</em> change (ajout, retrait,
/// réordonnancement, ET/OU) : taper dans un champ ne recalcule rien, chaque nœud suit son
/// propre éditeur. Zoom et panoramique ne touchent pas non plus au graphe — ils
/// transforment le canevas d'un bloc.
/// </summary>
public partial class RuleFlowViewModel : ObservableObject
{
    private readonly Func<int> _watchedFolderCount;

    private RuleViewModel? _rule;

    /// <summary>Ce qui était sélectionné, pour le retrouver après une reconstruction.</summary>
    private object? _selectedEditor;
    private FlowNodeKind _selectedKind = FlowNodeKind.Trigger;

    public RuleFlowViewModel(Func<int> watchedFolderCount) => _watchedFolderCount = watchedFolderCount;

    public ObservableCollection<FlowNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<FlowEdgeViewModel> Edges { get; } = [];

    /// <summary>La règle affichée. Exposée pour l'en-tête (nom, essai, « ne pas continuer »).</summary>
    public RuleViewModel? Rule
    {
        get => _rule;
        private set
        {
            _rule = value;
            OnPropertyChanged(nameof(Rule));
            OnPropertyChanged(nameof(HasRule));
        }
    }

    public bool HasRule => _rule is not null;

    [ObservableProperty] public partial double CanvasWidth { get; set; }
    [ObservableProperty] public partial double CanvasHeight { get; set; }

    // ---- Vue (zoom / panoramique) --------------------------------------------------------

    [ObservableProperty] public partial double Zoom { get; set; } = 1;
    [ObservableProperty] public partial double OffsetX { get; set; }
    [ObservableProperty] public partial double OffsetY { get; set; }

    // ---- Sélection -----------------------------------------------------------------------

    [ObservableProperty] public partial FlowNodeViewModel? SelectedNode { get; set; }
    [ObservableProperty] public partial bool IsInspectorOpen { get; set; }

    public static ConditionKind[] ConditionKinds => RuleViewModel.ConditionKinds;
    public static ActionKind[] ActionKinds => RuleViewModel.ActionKinds;
    public static ConditionMatchMode[] MatchOptions => RuleViewModel.MatchOptions;

    public bool HasSelection => SelectedNode is not null;

    /// <summary>Vrai quand l'inspecteur doit afficher l'éditeur d'une condition ou d'une action.</summary>
    public bool ShowsEditor => SelectedNode?.Editor is not null;

    public bool ShowsTrigger => SelectedNode?.Kind == FlowNodeKind.Trigger;
    public bool ShowsGate => SelectedNode?.Kind == FlowNodeKind.Gate;
    public bool ShowsConditionKinds => SelectedNode?.Kind == FlowNodeKind.AddCondition;
    public bool ShowsActionKinds => SelectedNode?.Kind == FlowNodeKind.AddAction;

    /// <summary>La condition sélectionnée, pour le « sauf si » de l'inspecteur.</summary>
    public ConditionViewModel? SelectedCondition => SelectedNode?.Editor as ConditionViewModel;

    public bool CanRemove => SelectedNode?.Editor is not null;

    /// <summary>L'ordre des actions est leur ordre d'exécution : il se règle depuis l'inspecteur.</summary>
    public bool CanReorder => SelectedNode?.Editor is ActionViewModel && _rule is { Actions.Count: > 1 };

    public string TriggerSummary => _watchedFolderCount() switch
    {
        0 => Loc.T("State_NothingWatched"),
        1 => Loc.T("Status_Folders_One"),
        var count => Loc.F("Status_Folders_Many", count),
    };

    partial void OnSelectedNodeChanged(FlowNodeViewModel? oldValue, FlowNodeViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;

        _selectedEditor = newValue?.Editor;
        _selectedKind = newValue?.Kind ?? FlowNodeKind.Trigger;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowsEditor));
        OnPropertyChanged(nameof(ShowsTrigger));
        OnPropertyChanged(nameof(ShowsGate));
        OnPropertyChanged(nameof(ShowsConditionKinds));
        OnPropertyChanged(nameof(ShowsActionKinds));
        OnPropertyChanged(nameof(SelectedCondition));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanReorder));
    }

    // ---- Liaison à une règle ---------------------------------------------------------------

    public void Bind(RuleViewModel? rule)
    {
        if (ReferenceEquals(rule, _rule)) return;

        Detach(_rule);
        Rule = rule;
        Attach(rule);

        // Une autre règle, un autre flux : on repart d'une vue neutre. Une règle encore
        // vide s'ouvre sur « ajouter une condition » — c'est la première chose à faire.
        _selectedEditor = null;
        _selectedKind = rule is { Conditions.Count: 0 } ? FlowNodeKind.AddCondition : FlowNodeKind.Trigger;
        IsInspectorOpen = false;
        ResetView();

        Build();
    }

    private void Attach(RuleViewModel? rule)
    {
        if (rule is null) return;

        rule.Conditions.CollectionChanged += OnStructureChanged;
        rule.Actions.CollectionChanged += OnStructureChanged;
        rule.PropertyChanged += OnRuleChanged;
    }

    private void Detach(RuleViewModel? rule)
    {
        if (rule is null) return;

        rule.Conditions.CollectionChanged -= OnStructureChanged;
        rule.Actions.CollectionChanged -= OnStructureChanged;
        rule.PropertyChanged -= OnRuleChanged;
    }

    private void OnStructureChanged(object? sender, NotifyCollectionChangedEventArgs e) => Build();

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Seul le ET/OU change la forme du flux ; le reste ne concerne que les nœuds.
        if (e.PropertyName == nameof(RuleViewModel.Match)) Build();
    }

    /// <summary>Le déclencheur annonce le nombre de dossiers surveillés : il suit leur liste.</summary>
    public void RefreshTrigger()
    {
        OnPropertyChanged(nameof(TriggerSummary));

        foreach (var node in Nodes)
        {
            if (node.Kind == FlowNodeKind.Trigger) node.StaticSubtitle = TriggerSummary;
        }
    }

    // ---- Construction du graphe -------------------------------------------------------------

    private void Build()
    {
        foreach (var node in Nodes) node.Dispose();

        Nodes.Clear();
        Edges.Clear();

        if (_rule is not { } rule)
        {
            SelectedNode = null;
            CanvasWidth = 0;
            CanvasHeight = 0;
            return;
        }

        // Une colonne par étape du parcours : déclencheur, conditions, ET/OU, puis une
        // colonne par action — l'ordre de lecture est l'ordre d'exécution.
        var columns = new List<List<FlowNodeViewModel>>(4 + rule.Actions.Count);

        var trigger = new FlowNodeViewModel(FlowNodeKind.Trigger)
        {
            Title = Loc.T("Flow_Trigger_Title"),
            Glyph = "◉",
            StaticSubtitle = TriggerSummary,
        };

        columns.Add([trigger]);

        var conditions = new List<FlowNodeViewModel>(rule.Conditions.Count + 1);
        foreach (var condition in rule.Conditions)
        {
            conditions.Add(new FlowNodeViewModel(FlowNodeKind.Condition, condition)
            {
                Title = condition.DisplayName,
                Glyph = "◆",
            });
        }

        var addCondition = new FlowNodeViewModel(FlowNodeKind.AddCondition)
        {
            Title = Loc.T("Flow_AddCondition"),
            Glyph = "＋",
            Height = FlowMetrics.PlaceholderHeight,
        };

        conditions.Add(addCondition);
        columns.Add(conditions);

        // Le ET/OU n'apparaît que s'il a un sens : avec zéro ou une condition, il n'y a
        // rien à combiner et le nœud ne ferait qu'allonger le flux.
        FlowNodeViewModel? gate = null;
        if (rule.Conditions.Count > 1)
        {
            gate = new FlowNodeViewModel(FlowNodeKind.Gate)
            {
                Title = Labels.For(rule.Match),
                Width = FlowMetrics.GateWidth,
                Height = FlowMetrics.GateHeight,
            };

            columns.Add([gate]);
        }

        var actions = new List<FlowNodeViewModel>(rule.Actions.Count);
        foreach (var action in rule.Actions)
        {
            var node = new FlowNodeViewModel(FlowNodeKind.Action, action)
            {
                Title = action.DisplayName,
                Glyph = GlyphFor(action),
            };

            actions.Add(node);
            columns.Add([node]);
        }

        var addAction = new FlowNodeViewModel(FlowNodeKind.AddAction)
        {
            Title = Loc.T("Flow_AddAction"),
            Glyph = "＋",
            Height = FlowMetrics.PlaceholderHeight,
        };

        columns.Add([addAction]);

        Layout(columns);

        foreach (var column in columns)
        {
            foreach (var node in column) Nodes.Add(node);
        }

        Connect(trigger, conditions, gate, actions, addAction);
        Restore(trigger);
    }

    /// <summary>
    /// Tisse les liens : le déclencheur alimente chaque condition, les conditions se
    /// rejoignent (sur le ET/OU s'il existe), puis les actions s'enchaînent.
    /// </summary>
    private void Connect(
        FlowNodeViewModel trigger,
        List<FlowNodeViewModel> conditions,
        FlowNodeViewModel? gate,
        List<FlowNodeViewModel> actions,
        FlowNodeViewModel addAction)
    {
        // Le dernier élément de la colonne des conditions est l'emplacement d'ajout.
        var real = conditions.Count - 1;
        var addCondition = conditions[^1];

        var merge = gate ?? (actions.Count > 0 ? actions[0] : addAction);

        if (real == 0)
        {
            // Aucune condition : le flux passe par l'emplacement d'ajout, qui devient
            // l'étape suivante à remplir, au lieu de laisser un nœud flottant.
            Edges.Add(new FlowEdgeViewModel(trigger, addCondition));
            Edges.Add(new FlowEdgeViewModel(addCondition, merge));
        }
        else
        {
            for (var i = 0; i < real; i++)
            {
                Edges.Add(new FlowEdgeViewModel(trigger, conditions[i]));
                Edges.Add(new FlowEdgeViewModel(conditions[i], merge));
            }
        }

        if (gate is not null)
        {
            Edges.Add(new FlowEdgeViewModel(gate, actions.Count > 0 ? actions[0] : addAction));
        }

        for (var i = 1; i < actions.Count; i++) Edges.Add(new FlowEdgeViewModel(actions[i - 1], actions[i]));

        if (actions.Count > 0) Edges.Add(new FlowEdgeViewModel(actions[^1], addAction));
    }

    /// <summary>
    /// Place les colonnes de gauche à droite, chacune centrée sur l'axe du flux. Les
    /// positions sont calculées ici et nulle part ailleurs : rien n'est persisté, une
    /// règle importée s'affiche donc toujours proprement.
    /// </summary>
    private void Layout(List<List<FlowNodeViewModel>> columns)
    {
        var tallest = 0d;

        foreach (var column in columns) tallest = Math.Max(tallest, HeightOf(column));

        var x = FlowMetrics.Margin;

        foreach (var column in columns)
        {
            var width = 0d;
            foreach (var node in column) width = Math.Max(width, node.Width);

            var y = FlowMetrics.Margin + ((tallest - HeightOf(column)) / 2);

            foreach (var node in column)
            {
                // Les nœuds étroits (le ET/OU) restent centrés dans leur colonne.
                node.X = x + ((width - node.Width) / 2);
                node.Y = y;
                y += node.Height + FlowMetrics.RowGap;
            }

            x += width + FlowMetrics.ColumnGap;
        }

        CanvasWidth = x - FlowMetrics.ColumnGap + FlowMetrics.Margin;
        CanvasHeight = tallest + (FlowMetrics.Margin * 2);
    }

    private static double HeightOf(List<FlowNodeViewModel> column)
    {
        var height = 0d;
        foreach (var node in column) height += node.Height;

        return height + (FlowMetrics.RowGap * (column.Count - 1));
    }

    /// <summary>
    /// Retrouve la sélection après reconstruction. Retirer une étape sélectionne son
    /// emplacement d'ajout : on reste là où on travaillait, pas ramené au début.
    /// </summary>
    private void Restore(FlowNodeViewModel trigger)
    {
        if (_selectedEditor is not null)
        {
            foreach (var node in Nodes)
            {
                if (!ReferenceEquals(node.Editor, _selectedEditor)) continue;

                SelectedNode = node;
                return;
            }
        }

        foreach (var node in Nodes)
        {
            if (node.Kind != _selectedKind) continue;

            SelectedNode = node;
            return;
        }

        SelectedNode = trigger;
    }

    private static string GlyphFor(ActionViewModel action) => action switch
    {
        MoveActionViewModel => "→",
        CopyActionViewModel => "⧉",
        RenameActionViewModel => "✎",
        DeleteActionViewModel => "⌫",
        RecycleActionViewModel => "♻",
        RunCommandActionViewModel => "▶",
        _ => "•",
    };

    // ---- Édition ------------------------------------------------------------------------

    [RelayCommand]
    private void Select(FlowNodeViewModel? node)
    {
        if (node is null) return;

        SelectedNode = node;
        IsInspectorOpen = true;
    }

    [RelayCommand]
    private void CloseInspector() => IsInspectorOpen = false;

    [RelayCommand]
    private void AddCondition(ConditionKind kind)
    {
        if (_rule is not { } rule) return;

        var condition = ConditionViewModel.Create(kind);
        _selectedEditor = condition;
        rule.Conditions.Add(condition);
    }

    [RelayCommand]
    private void AddAction(ActionKind kind)
    {
        if (_rule is not { } rule) return;

        var action = ActionViewModel.Create(kind);
        _selectedEditor = action;
        rule.Actions.Add(action);
    }

    [RelayCommand]
    private void Remove()
    {
        if (_rule is not { } rule || SelectedNode is not { Editor: { } editor } node) return;

        // La sélection retombe sur l'emplacement d'ajout correspondant : la reconstruction
        // s'en charge, il suffit de dire ce qu'on cherchait.
        _selectedEditor = null;
        _selectedKind = node.IsCondition ? FlowNodeKind.AddCondition : FlowNodeKind.AddAction;

        if (editor is ConditionViewModel condition) rule.Conditions.Remove(condition);
        else if (editor is ActionViewModel action) rule.Actions.Remove(action);
    }

    [RelayCommand]
    private void MoveEarlier() => Move(-1);

    [RelayCommand]
    private void MoveLater() => Move(1);

    private void Move(int delta)
    {
        if (_rule is not { } rule || SelectedNode?.Editor is not ActionViewModel action) return;

        var index = rule.Actions.IndexOf(action);
        var target = index + delta;

        if (index < 0 || target < 0 || target >= rule.Actions.Count) return;

        _selectedEditor = action;
        rule.Actions.Move(index, target);
    }

    // ---- Vue -----------------------------------------------------------------------------

    private const double MinimumZoom = 0.4;
    private const double MaximumZoom = 2;

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(MaximumZoom, Math.Round(Zoom + 0.1, 2));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(MinimumZoom, Math.Round(Zoom - 0.1, 2));

    [RelayCommand]
    private void ResetView()
    {
        Zoom = 1;
        OffsetX = 0;
        OffsetY = 0;
    }
}
