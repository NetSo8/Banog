using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Banog.UI.Views;

/// <summary>
/// Le plan de travail du flux : il ne connaît ni nœuds ni liens, il déplace et met à
/// l'échelle ce qu'on lui donne.
///
/// Zoom et panoramique passent par une transformation du contenu, pas par une
/// recomposition : quel que soit le nombre de nœuds, faire glisser le flux ne coûte
/// qu'une matrice. Les trois valeurs sont liées au viewmodel, ce qui permet aux boutons
/// de zoom d'agir sans que la vue ait à exposer de code.
/// </summary>
public sealed class FlowSurface : Border
{
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<FlowSurface, double>(
            nameof(Zoom), defaultValue: 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> OffsetXProperty =
        AvaloniaProperty.Register<FlowSurface, double>(
            nameof(OffsetX), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> OffsetYProperty =
        AvaloniaProperty.Register<FlowSurface, double>(
            nameof(OffsetY), defaultBindingMode: BindingMode.TwoWay);

    private const double MinimumZoom = 0.4;
    private const double MaximumZoom = 2;

    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _translate = new();
    private readonly TransformGroup _transform = new();

    private Point _grabbedAt;
    private bool _panning;

    /// <summary>Taille de contenu déjà cadrée : sert à ne recadrer que sur un vrai changement.</summary>
    private Size _fitted;

    public FlowSurface()
    {
        ClipToBounds = true;
        Focusable = true;

        _transform.Children.Add(_scale);
        _transform.Children.Add(_translate);

        Apply();
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double OffsetX
    {
        get => GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    public double OffsetY
    {
        get => GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>
    /// La surface est une fenêtre sur le flux, pas un conteneur qui s'adapte à lui : elle
    /// prend la place qu'on lui donne et ne demande rien. Sans cela, un flux large ferait
    /// gonfler la colonne qui l'accueille jusqu'à pousser le reste de la page dehors.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(Size.Infinity);

        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    /// <summary>Le contenu est posé à sa taille propre ; c'est la transformation qui le déplace.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child) return finalSize;

        child.Arrange(new Rect(child.DesiredSize));
        Fit(child.DesiredSize, finalSize);

        return finalSize;
    }

    /// <summary>
    /// Cadre le flux quand son contenu change — une autre règle, une étape ajoutée.
    /// Un flux plus large que la vue est réduit puis centré, plutôt que d'obliger à le
    /// chercher à la molette. Le zoom choisi à la main est conservé tant que le contenu
    /// ne change pas.
    /// </summary>
    private void Fit(Size content, Size viewport)
    {
        if (content.Width <= 0 || content.Height <= 0) return;
        if (content == _fitted) return;

        _fitted = content;

        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        var scale = Math.Min(1, Math.Min(viewport.Width / content.Width, viewport.Height / content.Height));

        Zoom = Math.Max(MinimumZoom, scale);
        OffsetX = Math.Max(0, (viewport.Width - (content.Width * Zoom)) / 2);
        OffsetY = Math.Max(0, (viewport.Height - (content.Height * Zoom)) / 2);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ZoomProperty
            || change.Property == OffsetXProperty
            || change.Property == OffsetYProperty
            || change.Property == ChildProperty)
        {
            Apply();
        }
    }

    private void Apply()
    {
        _scale.ScaleX = Zoom;
        _scale.ScaleY = Zoom;
        _translate.X = OffsetX;
        _translate.Y = OffsetY;

        if (Child is not { } child) return;

        child.RenderTransformOrigin = RelativePoint.TopLeft;
        child.RenderTransform = _transform;
    }

    /// <summary>
    /// Zoom centré sur le pointeur : ce qui est sous le curseur y reste. Zoomer sur le
    /// centre de la fenêtre obligerait à repositionner le flux après chaque cran.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var wanted = Zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1);
        var zoom = Math.Clamp(wanted, MinimumZoom, MaximumZoom);

        if (Math.Abs(zoom - Zoom) < 0.0001)
        {
            e.Handled = true;
            return;
        }

        var pointer = e.GetPosition(this);
        var world = new Point((pointer.X - OffsetX) / Zoom, (pointer.Y - OffsetY) / Zoom);

        Zoom = zoom;
        OffsetX = pointer.X - (world.X * zoom);
        OffsetY = pointer.Y - (world.Y * zoom);

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Un clic sur un nœud est déjà traité par le nœud : seul le fond fait glisser.
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;

        _grabbedAt = e.GetPosition(this);
        _panning = true;

        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_panning) return;

        var position = e.GetPosition(this);

        OffsetX += position.X - _grabbedAt.X;
        OffsetY += position.Y - _grabbedAt.Y;

        _grabbedAt = position;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_panning) return;

        _panning = false;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
    }
}
