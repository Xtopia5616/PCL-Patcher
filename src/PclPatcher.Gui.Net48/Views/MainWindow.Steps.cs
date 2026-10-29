namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Per-step status of the selected file
    // ------------------------------------------------------------------

    /// <summary>
    /// Fills the step list from <c>PclPatcher --probe</c> whenever the selected
    /// file or the option set changes. The panel used to stay empty until a run
    /// started, so a patched file looked like "nothing to do" and a build with
    /// missing anchors looked the same as a healthy one. The patcher does the
    /// probing: the GUI only renders its report.
    /// </summary>
    private async Task RefreshStepProbeAsync(string path)
    {
        var generation = ++_probeGeneration;
        if (_steps is null) return;

        // A run drives the rows from stdout; a probe finishing mid-run must never
        // overwrite the live steps.
        if (_busy) return;

        if (!_catalog.SupportsProbe)
        {
            // Never pass --probe to an older patcher: it ignores unknown switches
            // and would start a real in-place patch instead of just reporting.
            ClearStepProbe();
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || _patcherPath is null)
        {
            ClearStepProbe();
            return;
        }

        if (_stepsCount is not null) _stepsCount.Text = "探测中…";

        var host = _patcherPath;
        var args = new List<string>(_patcherPrefix) { path, "--probe" };
        if (_linkUnlockBox.Checked != true) args.Add("--download-only");
        if (_noUpdateBox.Checked == true) args.Add("--no-update");

        List<PclStepProbeResult> results;
        try
        {
            var output = await Task.Run(() => RunPatcherCommand(host, args)).ConfigureAwait(true);
            if (!IsLoaded || generation != _probeGeneration || _busy) return;
            results = PclStepProbe.Parse(output);
        }
        catch
        {
            if (IsLoaded && generation == _probeGeneration) ClearStepProbe();
            return;
        }

        if (!IsLoaded || generation != _probeGeneration || _busy) return;
        if (results.Count == 0)
        {
            // Not a .NET/PCL binary or an unreadable report: do not leave the
            // previous file's rows on screen.
            ClearStepProbe();
            return;
        }

        _steps.ShowProbe(results);
        UpdateStepCount();
    }

    /// <summary>Clears the probe results when the source box no longer holds a file.</summary>
    private void ClearStepProbe()
    {
        _probeGeneration++;
        if (!IsLoaded) return;
        _steps?.Reset();
        UpdateStepCount();
    }

    private void UpdateStepCount()
    {
        if (_stepsCount is null || _steps is null) return;
        _stepsCount.Text = _steps.Completed + "/" + _steps.ActiveCount;
    }

    private string CurrentSource()
        => _sourceBox?.Text?.Trim().Trim('"') ?? string.Empty;
}
