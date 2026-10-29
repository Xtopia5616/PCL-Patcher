namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // First-run disclaimer gate
    // ------------------------------------------------------------------

    /// <summary>
    /// Blocks the main window until the current disclaimer revision has been
    /// acknowledged. Returning false means the user declined, and the caller
    /// must not continue setting up the page.
    /// </summary>
    private bool EnsureDisclaimerAccepted()
    {
        if (_settings.HasAcceptedDisclaimer(DisclaimerWindow.Version)) return true;

        var window = new DisclaimerWindow { Owner = this };
        if (window.ShowDialog() != true)
        {
            Close();
            return false;
        }

        // Persisted so later launches (and the 关于 page) know this revision was
        // seen. A failed write only means the notice appears again next time.
        _settings.AcceptDisclaimer(DisclaimerWindow.Version);
        return true;
    }

    /// <summary>Re-opens the notice in read-only mode from the 关于 page.</summary>
    private void ShowDisclaimerReadOnly()
    {
        var window = new DisclaimerWindow(true, _settings.DisclaimerAcceptedAt) { Owner = this };
        window.ShowDialog();
    }
}
