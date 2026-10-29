#nullable enable

namespace PclPatcher.Gui.Net48;

/// <summary>
/// One PCL build the patcher has a profile for. Produced by
/// <c>PclPatcher --profiles</c>; the GUI never keeps a private copy.
/// </summary>
internal sealed class PclBuildProfile
{
    public PclBuildProfile(string version, string sha256, int fingerprintCount)
    {
        Version = version;
        Sha256 = sha256;
        FingerprintCount = fingerprintCount;
    }

    public string Version { get; }

    public string Sha256 { get; }

    public int FingerprintCount { get; }
}

/// <summary>
/// Supported PCL builds, read from <c>PclPatcher --profiles</c>. The UI renders
/// a range label (for example <c>2.99.0.1+</c>) instead of naming a single build,
/// so adding a profile in the patcher is enough to update every screen.
/// </summary>
internal sealed class PclVersionCatalog
{
    private const string ProfileTag = "profile";
    private const string UnknownLabel = "未检测到版本";

    private readonly List<PclBuildProfile> _profiles;

    private PclVersionCatalog(List<PclBuildProfile> profiles)
    {
        _profiles = profiles;
    }

    public static PclVersionCatalog Empty { get; } = new PclVersionCatalog(new List<PclBuildProfile>());

    public IReadOnlyList<PclBuildProfile> Profiles => _profiles;

    public bool IsLoaded => _profiles.Count > 0;

    public string MinimumVersion => IsLoaded ? _profiles[0].Version : string.Empty;

    /// <summary>Compact label for the version row and badges, e.g. "2.99.0.1+".</summary>
    public string SupportLabel => IsLoaded ? MinimumVersion + "+" : UnknownLabel;

    /// <summary>Human readable range, e.g. "2.99.0.1 及更高版本".</summary>
    public string SupportDescription => IsLoaded ? MinimumVersion + " 及更高版本" : "受支持的 PCL 版本";

    public string DetailText
    {
        get
        {
            if (!IsLoaded)
                return "未能从 PclPatcher 读取版本列表；补丁时仍会由 patcher 校验 SHA-256 与方法指纹。";

            var versions = string.Join("、", _profiles.ConvertAll(p => p.Version).ToArray());
            return "支持 " + SupportDescription + "。已收录：" + versions + "。";
        }
    }

    public bool Matches(string? sha256) => Find(sha256) is not null;

    public PclBuildProfile? Find(string? sha256)
    {
        if (!IsLoaded || string.IsNullOrEmpty(sha256)) return null;
        foreach (var profile in _profiles)
        {
            if (string.Equals(profile.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                return profile;
        }
        return null;
    }

    /// <summary>
    /// Parses the tab separated output of <c>PclPatcher --profiles</c>. Unknown
    /// lines are ignored so the patcher can add headers or fields later without
    /// breaking older GUIs.
    /// </summary>
    public static PclVersionCatalog Parse(string? output)
    {
        if (string.IsNullOrEmpty(output)) return Empty;

        var profiles = new List<PclBuildProfile>();
        foreach (var raw in output!.Split('\n'))
        {
            var fields = raw.TrimEnd('\r').Split('\t');
            if (fields.Length < 4) continue;
            if (!string.Equals(fields[0], ProfileTag, StringComparison.Ordinal)) continue;

            var version = fields[1].Trim();
            var sha256 = fields[2].Trim();
            if (version.Length == 0 || sha256.Length != 64) continue;

            int.TryParse(fields[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var fingerprints);
            profiles.Add(new PclBuildProfile(version, sha256, fingerprints));
        }

        profiles.Sort((a, b) => CompareVersions(a.Version, b.Version));
        return profiles.Count == 0 ? Empty : new PclVersionCatalog(profiles);
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(left, out var a) && Version.TryParse(right, out var b))
            return a.CompareTo(b);
        return string.CompareOrdinal(left, right);
    }
}
