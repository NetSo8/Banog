using System.ComponentModel;
using Avalonia.Media;
using Banog.UI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Banog.UI.ViewModels.Flow;

/// <summary>
/// Nature d'une étape du flux. Pilote la forme du nœud et ce que montre l'inspecteur ;
/// le reste (titre, résumé) est porté par le nœud lui-même.
/// </summary>
public enum FlowNodeKind
{
    /// <summary>Point d'entrée : un fichier arrive dans un dossier surveillé.</summary>
    Trigger = 0,

    /// <summary>Une condition de la règle.</summary>
    Condition = 1,

    /// <summary>Le ET / OU qui réunit les conditions.</summary>
    Gate = 2,

    /// <summary>Une action, dans l'ordre où elle s'exécute.</summary>
    Action = 3,

    /// <summary>Emplacement libre : ajouter une condition.</summary>
    AddCondition = 4,

    /// <summary>Emplacement libre : ajouter une action.</summary>
    AddAction = 5,
}

/// <summary>
/// Une étape du flux, telle qu'elle est posée sur le canevas.
///
/// Le nœud ne connaît pas le moteur : il enveloppe l'éditeur existant (une
/// <see cref="ConditionViewModel"/>, une <see cref="ActionViewModel"/>) et se contente de
/// l'annoncer. L'inspecteur affiche cet éditeur tel quel, avec les mêmes DataTemplates
/// que la version en liste — aucun formulaire n'est écrit deux fois.
/// </summary>
public partial class FlowNodeViewModel : ObservableObject, IDisposable
{
    private readonly INotifyPropertyChanged? _source;

    public FlowNodeViewModel(FlowNodeKind kind, object? editor = null)
    {
        Kind = kind;
        Editor = editor;

        // Le résumé d'un nœud est celui de son éditeur : il suit la saisie, sans que la
        // disposition du flux ait à être recalculée.
        if (editor is INotifyPropertyChanged observable)
        {
            _source = observable;
            _source.PropertyChanged += OnEditorChanged;
        }
    }

    public FlowNodeKind Kind { get; }

    /// <summary>La condition ou l'action éditée, ou <c>null</c> pour les nœuds de structure.</summary>
    public object? Editor { get; }

    // ---- Disposition ------------------------------------------------------------------
    // Posées par RuleFlowLayout, jamais par la vue.

    [ObservableProperty] public partial double X { get; set; }
    [ObservableProperty] public partial double Y { get; set; }

    public double Width { get; init; } = FlowMetrics.NodeWidth;
    public double Height { get; init; } = FlowMetrics.NodeHeight;

    /// <summary>Point de sortie (bord droit, à mi-hauteur), origine des liens.</summary>
    public double OutletX => X + Width;
    public double OutletY => Y + (Height / 2);

    /// <summary>Point d'entrée (bord gauche, à mi-hauteur).</summary>
    public double InletX => X;
    public double InletY => Y + (Height / 2);

    [ObservableProperty] public partial bool IsSelected { get; set; }

    // ---- Affichage ---------------------------------------------------------------------

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Glyph { get; set; } = string.Empty;

    /// <summary>Ce que fait l'étape, en une clause. Vient de l'éditeur quand il y en a un.</summary>
    public string Subtitle => Editor switch
    {
        ConditionViewModel condition => condition.Description,
        ActionViewModel action => action.Description,
        _ => _subtitle,
    };

    private string _subtitle = string.Empty;

    public string StaticSubtitle
    {
        get => _subtitle;
        set
        {
            _subtitle = value;
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    public bool IsCondition => Kind == FlowNodeKind.Condition;
    public bool IsAction => Kind == FlowNodeKind.Action;
    public bool IsGate => Kind == FlowNodeKind.Gate;
    public bool IsPlaceholder => Kind is FlowNodeKind.AddCondition or FlowNodeKind.AddAction;
    public bool HasSubtitle => !IsGate && !IsPlaceholder;

    /// <summary>Une action irréversible se signale sur le canevas, pas seulement une fois ouverte.</summary>
    public bool IsDestructive => Editor is ActionViewModel { IsDestructive: true };

    /// <summary>Une condition inversée porte « sauf si » : sans cela le flux se lit à l'envers.</summary>
    public bool IsNegated => Editor is ConditionViewModel { Negate: true };

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsDestructive));
        OnPropertyChanged(nameof(IsNegated));
    }

    public void Dispose()
    {
        if (_source is not null) _source.PropertyChanged -= OnEditorChanged;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Un lien entre deux nœuds. La géométrie est calculée une fois, à la disposition :
/// un déplacement de vue (zoom, panoramique) ne la recalcule pas, il ne fait que
/// transformer le canevas entier.
/// </summary>
public sealed class FlowEdgeViewModel
{
    public FlowEdgeViewModel(FlowNodeViewModel from, FlowNodeViewModel to)
    {
        Geometry = BuildCurve(from.OutletX, from.OutletY, to.InletX, to.InletY);
    }

    public Geometry Geometry { get; }

    /// <summary>
    /// Courbe de Bézier horizontale : les tangentes partent à l'horizontale des deux
    /// côtés, ce qui donne des liens lisibles même quand les nœuds sont décalés en
    /// hauteur, sans jamais croiser un nœud voisin.
    /// </summary>
    private static Geometry BuildCurve(double x1, double y1, double x2, double y2)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            var reach = Math.Max(FlowMetrics.MinimumCurveReach, (x2 - x1) * 0.5);

            context.BeginFigure(new(x1, y1), isFilled: false);
            context.CubicBezierTo(new(x1 + reach, y1), new(x2 - reach, y2), new(x2, y2));
            context.EndFigure(isClosed: false);
        }

        return geometry;
    }
}

/// <summary>Dimensions du flux. Un seul endroit : la disposition et les styles s'accordent.</summary>
public static class FlowMetrics
{
    public const double NodeWidth = 232;
    public const double NodeHeight = 66;

    public const double GateWidth = 62;
    public const double GateHeight = 44;

    public const double PlaceholderHeight = 40;

    public const double ColumnGap = 78;
    public const double RowGap = 18;
    public const double Margin = 32;

    /// <summary>Longueur minimale des tangentes : évite les liens plats entre colonnes serrées.</summary>
    public const double MinimumCurveReach = 34;
}
