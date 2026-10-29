#nullable enable

namespace PclPatcher.Gui.Net48;

/// <summary>One row of the patcher's tab separated <c>--probe</c> report.</summary>
internal sealed class PclStepProbeResult
{
    public string Key = string.Empty;
    public string Reason = string.Empty;
    public StepState State;
}

/// <summary>
/// Parses the per-step report the patcher prints for the selected file. The GUI
/// never inspects IL itself: the patcher owns the patterns, so this list cannot
/// drift from what a patch run would actually do.
/// </summary>
internal static class PclStepProbe
{
    public static List<PclStepProbeResult> Parse(string output)
    {
        var results = new List<PclStepProbeResult>();
        if (string.IsNullOrEmpty(output)) return results;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("patch\t", StringComparison.Ordinal)) continue;

            var fields = line.Split('\t');
            if (fields.Length < 3) continue;

            StepState state;
            switch (fields[2])
            {
                case "ready":
                    state = StepState.Ready;
                    break;
                case "already":
                    state = StepState.Already;
                    break;
                case "missing":
                    state = StepState.Skip;
                    break;
                case "error":
                    state = StepState.Error;
                    break;
                default:
                    continue;
            }

            results.Add(new PclStepProbeResult
            {
                Key = fields[1],
                State = state,
                Reason = fields.Length > 3 ? fields[3].Trim() : string.Empty
            });
        }

        return results;
    }
}
