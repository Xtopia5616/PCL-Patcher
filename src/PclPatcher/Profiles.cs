namespace PclPatcher;

internal static partial class Program
{
    // The profile table below is the single source of truth for supported PCL
    // builds. The GUIs read it through `PclPatcher --profiles` and render the
    // oldest supported version ("2.99.0.1"), so adding a version never requires
    // touching UI code. The "+" range wording stays in CLI output.
    private static string KnownVersion => Profiles[0].Version;
    private static string SupportedRange => KnownVersion + "+";

    private enum PatchState
    {
        Ready,
        AlreadyPatched,
        Missing
    }

    private sealed record ProbeResult(PatchState State, string Reason);

    private sealed record PclProfile(string Version, string Sha256, IReadOnlyDictionary<string, string> Fingerprints);

    // Ordered from oldest to newest; the first entry defines the displayed range.
    private static readonly PclProfile[] Profiles =
    {
        new(
            "2.99.0.1",
            "0db34e19d9eeb6d496d20fabe786ec16bbd36ba6b0ccdc8d29cb7e909c114397",
            new Dictionary<string, string>
            {
                ["PCL.PageDownloadLeft::.ctor/0"] = "2c6c63d4e200f5ad",
                ["PCL.PageDownloadInstall::LoaderInit/0"] = "7b1ab7c438aa084f",
                ["PCL.ModModpack::ModpackInstall/3"] = "ac01defe0958b4e5",
                ["PCL.PageSelectLeft::McFolderListUI/0"] = "65a2299afca76eda",
                ["PCL.PageDownloadResourceDetail::InitializeStackResourceVersions/1"] = "4d8b2fe8b97c1662",
                ["PCL.PageInstanceModDisabled::BtnDownload_Loaded/0"] = "2b04d8e7d19c7058",
                ["PCL.PageLaunchLeft::RefreshButtonsUI/0"] = "0252d318cd8fb776"
            })
    };

    private sealed record PatchStep(
        string Name,
        bool Required,
        Func<ModuleDef, ProbeResult> Probe,
        Func<ModuleDef, int> Apply,
        Action<ModuleDef> Verify);

    private static void CheckFingerprints(ModuleDef module, PclProfile profile, bool allowDrift)
    {
        var drift = new List<string>();
        foreach (var (key, expected) in profile.Fingerprints)
        {
            var parts = key.Split("::");
            var methodPart = parts[1].Split('/');
            var type = module.Find(parts[0], false);
            var method = type?.Methods.FirstOrDefault(m =>
                m.Name == methodPart[0] && m.MethodSig.Params.Count == int.Parse(methodPart[1]));
            if (method?.Body is null)
            {
                drift.Add($"{key} missing");
                continue;
            }

            var actual = Fingerprint(method);
            if (actual == expected) continue;
            drift.Add($"{key} expected {expected}, got {actual}");
            Console.WriteLine($"fp     : {key,-55} DRIFT");
        }

        if (drift.Count == 0)
        {
            Console.WriteLine("fp     : all known method fingerprints match");
            return;
        }

        foreach (var line in drift) Console.WriteLine("         " + line);
        if (allowDrift)
        {
            Console.WriteLine("WARN   : method fingerprint drift; continuing because --force/--dry-run");
            return;
        }

        throw new InvalidOperationException("method fingerprint drift; pass --force to try anyway");
    }
}
