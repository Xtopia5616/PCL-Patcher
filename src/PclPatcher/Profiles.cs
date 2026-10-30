namespace PclPatcher;

internal static partial class Program
{
    // The profile table below is the single source of truth for supported PCL
    // builds. The GUIs read it through `PclPatcher --profiles` and render the
    // oldest supported version ("2.99.0.0"), so adding a version never requires
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

    /// <summary>
    /// One shipped PCL build. <paramref name="Channel"/> distinguishes the
    /// official release line from the snapshot (快照版) line: both carry the same
    /// numeric version, so the channel has to be part of a build's identity.
    /// <paramref name="Verified"/> marks the builds this project actually patched
    /// and launched; adopted (--adopt) entries stay false until someone confirms
    /// them, so "已收录" is never mistaken for "已验证".
    /// </summary>
    private sealed record PclProfile(
        string Version,
        string Sha256,
        IReadOnlyDictionary<string, string> Fingerprints,
        string Channel = "release",
        string Built = "",
        bool Verified = true)
    {
        public string Label => Channel == "release" ? Version : $"{Version} {ChannelLabel(Channel)}";
    }

    private static string ChannelLabel(string channel) => channel switch
    {
        "release" => "正式版",
        "snapshot" => "快照版",
        "beta" => "测试版",
        _ => "未识别渠道"
    };

    // Ordered from oldest to newest; the first entry defines the displayed range.
    // 2.99.0.0 and 2.99.0.1 exist twice on purpose: the release build and the
    // snapshot share all seven fingerprints but are different binaries.
    private static readonly PclProfile[] BuiltInProfiles =
    {
        new(
            "2.99.0.0",
            "6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386",
            new Dictionary<string, string>
            {
                ["PCL.PageDownloadLeft::.ctor/0"] = "ca191eb64d73ba28",
                ["PCL.PageDownloadInstall::LoaderInit/0"] = "7b1ab7c438aa084f",
                ["PCL.ModModpack::ModpackInstall/3"] = "4943749626a46f7f",
                ["PCL.PageSelectLeft::McFolderListUI/0"] = "50ae28338eedcf07",
                ["PCL.PageDownloadResourceDetail::InitializeStackResourceVersions/1"] = "4d8b2fe8b97c1662",
                ["PCL.PageInstanceModDisabled::BtnDownload_Loaded/0"] = "b8d0ed182ea307bb",
                ["PCL.PageLaunchLeft::RefreshButtonsUI/0"] = "278163d78b8377b5"
            },
            Built: "2026-10-20"),
        new(
            "2.99.0.0",
            "5b64d6d540c376390470cea3ab99d47661e8bca81be2ba647ea83e7d2e9428fa",
            new Dictionary<string, string>
            {
                ["PCL.PageDownloadLeft::.ctor/0"] = "ca191eb64d73ba28",
                ["PCL.PageDownloadInstall::LoaderInit/0"] = "7b1ab7c438aa084f",
                ["PCL.ModModpack::ModpackInstall/3"] = "4943749626a46f7f",
                ["PCL.PageSelectLeft::McFolderListUI/0"] = "50ae28338eedcf07",
                ["PCL.PageDownloadResourceDetail::InitializeStackResourceVersions/1"] = "4d8b2fe8b97c1662",
                ["PCL.PageInstanceModDisabled::BtnDownload_Loaded/0"] = "b8d0ed182ea307bb",
                ["PCL.PageLaunchLeft::RefreshButtonsUI/0"] = "278163d78b8377b5"
            },
            Channel: "snapshot",
            Built: "2026-10-29"),
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
            },
            Built: "2026-10-25"),
        new(
            "2.99.0.1",
            "2749aa5de361ff35a857dd0ca6ba4b0c8aecaa68fedeac413f54ce3899dba453",
            new Dictionary<string, string>
            {
                ["PCL.PageDownloadLeft::.ctor/0"] = "2c6c63d4e200f5ad",
                ["PCL.PageDownloadInstall::LoaderInit/0"] = "7b1ab7c438aa084f",
                ["PCL.ModModpack::ModpackInstall/3"] = "ac01defe0958b4e5",
                ["PCL.PageSelectLeft::McFolderListUI/0"] = "65a2299afca76eda",
                ["PCL.PageDownloadResourceDetail::InitializeStackResourceVersions/1"] = "4d8b2fe8b97c1662",
                ["PCL.PageInstanceModDisabled::BtnDownload_Loaded/0"] = "2b04d8e7d19c7058",
                ["PCL.PageLaunchLeft::RefreshButtonsUI/0"] = "0252d318cd8fb776"
            },
            Channel: "snapshot",
            Built: "2026-10-30")
    };

    /// <summary>
    /// Built-in profiles plus the ones adopted at runtime. The external file is
    /// what makes a new snapshot usable without rebuilding the tool: run
    /// <c>--adopt --save</c> on it once, and every later run (CLI and GUI) sees it.
    /// </summary>
    private static readonly PclProfile[] Profiles = LoadProfiles();

    private static string ExternalProfilePath
    {
        get
        {
            // PCLPATCHER_PROFILES lets a portable copy (or a test) keep its adopted
            // builds next to itself instead of in %LOCALAPPDATA%.
            var custom = Environment.GetEnvironmentVariable("PCLPATCHER_PROFILES");
            if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PclPatcher", "profiles.tsv");
        }
    }

    private static PclProfile[] LoadProfiles()
    {
        var profiles = new List<PclProfile>(BuiltInProfiles);
        foreach (var profile in ReadExternalProfiles())
        {
            if (profiles.Any(p => string.Equals(p.Sha256, profile.Sha256, StringComparison.OrdinalIgnoreCase)))
                continue;
            profiles.Add(profile);
        }

        profiles.Sort((a, b) =>
        {
            var byVersion = Version.TryParse(a.Version, out var left) && Version.TryParse(b.Version, out var right)
                ? left.CompareTo(right)
                : string.CompareOrdinal(a.Version, b.Version);
            return byVersion != 0 ? byVersion : string.CompareOrdinal(a.Channel, b.Channel);
        });
        return profiles.ToArray();
    }

    // One tab separated line per adopted build:
    //   build <tab> version <tab> sha256 <tab> channel <tab> built <tab> key=fp;key=fp;...
    private static IEnumerable<PclProfile> ReadExternalProfiles()
    {
        string[] lines;
        try
        {
            if (!File.Exists(ExternalProfilePath)) yield break;
            lines = File.ReadAllLines(ExternalProfilePath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            var fields = line.Split('\t');
            if (fields.Length < 6 || fields[0] != "build") continue;

            var version = fields[1].Trim();
            var sha = fields[2].Trim().ToLowerInvariant();
            if (version.Length == 0 || sha.Length != 64 || !sha.All(Uri.IsHexDigit)) continue;

            var fingerprints = new Dictionary<string, string>();
            foreach (var pair in fields[5].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = pair.Substring(0, eq).Trim();
                var value = pair.Substring(eq + 1).Trim();
                if (key.Length > 0 && value.Length == 16) fingerprints[key] = value;
            }
            if (fingerprints.Count == 0) continue;

            yield return new PclProfile(version, sha, fingerprints,
                fields[3].Trim().Length == 0 ? "release" : fields[3].Trim(),
                fields[4].Trim(),
                Verified: false);
        }
    }

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
            Console.WriteLine($"fp     : all {profile.Fingerprints.Count} known method fingerprints match");
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
