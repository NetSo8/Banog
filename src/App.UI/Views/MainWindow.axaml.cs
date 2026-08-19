using Avalonia;
using Avalonia.Controls;

namespace Banog.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// La fenêtre est dimensionnée en unités logiques : à 125 % d'échelle, la
    /// largeur déclarée dépasse un écran 1920×1080 « bon marché » (1536×864), et
    /// la colonne de droite — l'inspecteur du flux — était coupée par le bord de
    /// l'écran. On replie la fenêtre sur la zone de travail au premier affichage :
    /// toujours entière, quelle que soit l'échelle.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Screens.ScreenFromWindow(this) is not { } screen) return;

        // Marge autour de la fenêtre, et place pour les bordures : la zone de
        // travail est en pixels physiques, la fenêtre en unités logiques.
        const int margin = 14;
        var scale = screen.Scaling;
        var availableWidth = (screen.WorkingArea.Width - (2 * margin)) / scale;
        var availableHeight = (screen.WorkingArea.Height - (2 * margin)) / scale;

        if (availableWidth >= Width && availableHeight >= Height) return;

        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);

        Position = new PixelPoint(
            screen.WorkingArea.X + (int)((screen.WorkingArea.Width - (Width * scale)) / 2),
            screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - (Height * scale)) / 2));
    }

    /// <summary>
    /// Fermer la fenêtre ne quitte pas : la surveillance continue en arrière-plan et
    /// l'icône de la zone de notification la ramène. Seul « Quitter » du plateau arrête
    /// l'application — les fermetures qu'il déclenche portent un autre motif et passent.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
