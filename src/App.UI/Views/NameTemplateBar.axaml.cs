using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Banog.UI.ViewModels;

namespace Banog.UI.Views;

/// <summary>
/// La barre des morceaux d'un nom : les pastilles s'ajoutent, se retirent, et se
/// réordonnent au glisser-déposer. Le code n'existe que pour ce geste — tout le reste
/// est du XAML relié au viewmodel.
/// </summary>
public partial class NameTemplateBar : UserControl
{
    private const double DragThreshold = 6;

    private TemplatePartViewModel? _draggedPart;
    private Border? _draggedVisual;
    private Control? _dropOverChip;
    private Point _pressPoint;
    private bool _dragging;
    private int _insertion = -1;

    public NameTemplateBar() => InitializeComponent();

    private void Chip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border chip || chip.DataContext is not TemplatePartViewModel part) return;
        if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;

        // Une pression dans un champ à éditer n'est pas un début de glissement.
        if (e.Source is TextBox or ComboBox or Button) return;

        _draggedPart = part;
        _draggedVisual = chip.Child as Border;
        _pressPoint = e.GetPosition(this);
        _dragging = false;
        _insertion = -1;

        e.Pointer.Capture(chip);
    }

    private void Chip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedPart is null || _draggedVisual is null) return;

        var position = e.GetPosition(this);

        if (!_dragging)
        {
            if (Math.Abs(position.X - _pressPoint.X) + Math.Abs(position.Y - _pressPoint.Y) < DragThreshold) return;

            _dragging = true;
            _draggedVisual.Classes.Add("dragging");
            Cursor = new Cursor(StandardCursorType.DragMove);
        }

        UpdateInsertion(position);
    }

    private void Chip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedPart is null) return;

        UpdateInsertion(e.GetPosition(this));
        EndDrag();
    }

    private void Chip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    /// <summary>Termine le geste. Le dépôt se fait à la dernière position retenue, quel que
    /// soit l'événement qui a clos le geste : le relâchement, ou une capture volée.</summary>
    private void EndDrag()
    {
        var part = _draggedPart;
        var insertion = _insertion;
        var dragging = _dragging;

        _draggedVisual?.Classes.Remove("dragging");
        ClearDropOver();

        _draggedPart = null;
        _draggedVisual = null;
        _dragging = false;
        _insertion = -1;
        Cursor = Cursor.Default;

        if (dragging && part is not null && DataContext is NameTemplateBuilderViewModel viewModel)
        {
            viewModel.MovePartTo(part, insertion);
        }
    }

    private void UpdateInsertion(Point position)
    {
        var insertion = InsertionAt(position);
        if (insertion == _insertion) return;

        _insertion = insertion;
        ClearDropOver();

        // On éclaire la pastille qui recevrait le dépôt : celle qui précède la position.
        var target = insertion == 0 ? 0 : insertion - 1;
        if (target < 0 || target >= PartsList.ItemCount) return;

        if (PartsList.ContainerFromIndex(target) is ContentPresenter { Child: { } child })
        {
            _dropOverChip = child;
            child.Classes.Add("dropTarget");
        }
    }

    private void ClearDropOver()
    {
        _dropOverChip?.Classes.Remove("dropTarget");
        _dropOverChip = null;
    }

    /// <summary>L'index d'insertion sous le pointeur : avant la pastille survolée à gauche
    /// de son centre, après elle à droite. Hors de toute pastille, on garde le dernier.</summary>
    private int InsertionAt(Point position)
    {
        for (var i = 0; i < PartsList.ItemCount; i++)
        {
            if (PartsList.ContainerFromIndex(i) is not ContentPresenter { Child: { } child } presenter) continue;

            var origin = presenter.TranslatePoint(default, this) ?? default;
            var bounds = new Rect(origin, child.Bounds.Size);
            if (!bounds.Contains(position)) continue;

            return position.X < bounds.X + (bounds.Width / 2) ? i : i + 1;
        }

        return _insertion >= 0 ? _insertion : (DataContext as NameTemplateBuilderViewModel)?.Parts.Count ?? 0;
    }
}
