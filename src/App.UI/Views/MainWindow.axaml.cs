using Avalonia.Controls;

namespace Banog.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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
