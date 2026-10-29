#nullable enable

namespace PclPatcher.Gui.Net48;

/// <summary>
/// One PCL build the patcher has a profile for. Produced by
/// <c>PclPatcher --profiles</c>; the GUI never keeps a private copy.
/// </summary>
internal sealed class PclBuildProfile
{
    public PclBuildProfile(string version, string sha256, int fingerprintCount, string channel, string label,
        bool verified, string built)
    {
        Version = version;
        Sha256 = sha256;
        FingerprintCount = fingerprintCount;
        Channel = channel;
        Label = string.IsNullOrEmpty(label) ? version : label;
        Verified = verified;
        Built = built;
    }

    public string Version { get; }

    public string Sha256 { get; }

    public int FingerprintCount { get; }

    /// <summary>"release", "snapshot" or "beta".</summary>
    public string Channel { get; }

    /// <summary>Display name, e.g. "2.99.0.0 快照版".</summary>
    public string Label { get; }

    /// <summary>True when this project patched and launched the build itself.</summary>
    public bool Verified { get; }

    public string Built { get; }

    /// <summary>First 12 hex chars, for compact UI rows.</summary>
    public string ShortHash => Sha256.Length >= 12 ? Sha256.Substring(0, 12) : Sha256;
}

/// <summary>
/// Supported PCL builds, read from <c>PclPatcher --profiles</c>. The UI shows the
/// oldest supported build (for example <c>2.99.0.0</c>) and keeps the “and newer”
/// wording in the tooltip, so adding a profile in the patcher updates every screen.
/// </summary>
internal sealed class PclVersionCatalog
{
    private const string ProfileTag = "profile";
    private const string FeatureTag = "feature";
    private const string UnknownLabel = "未检测到版本";
    public const string UnknownBuildLabel = "未识别版本";

    private readonly List<PclBuildProfile> _profiles;

    private PclVersionCatalog(List<PclBuildProfile> profiles, bool supportsProbe)
    {
        _profiles = profiles;
        SupportsProbe = supportsProbe;
    }

    public static PclVersionCatalog Empty { get; } = new PclVersionCatalog(new List<PclBuildProfile>(), false);

    public IReadOnlyList<PclBuildProfile> Profiles => _profiles;

    public bool IsLoaded => _profiles.Count > 0;

    /// <summary>
    /// True when the patcher advertises <c>--probe</c>. An older patcher would
    /// ignore the unknown switch and start a real in-place patch, so the GUI only
    /// asks for per-step states after this handshake.
    /// </summary>
    public bool SupportsProbe { get; }

    public string MinimumVersion => IsLoaded ? _profiles[0].Version : string.Empty;

    /// <summary>Compact label for the version row and badges, e.g. "2.99.0.0+".</summary>
    public string SupportLabel => IsLoaded ? MinimumVersion + "+" : UnknownLabel;

    /// <summary>
    /// Which build label the version row should show for a source file. The
    /// plain hash wins; a patched copy no longer matches any profile, so the
    /// matching <c>.bak</c> hash decides. Anything else is explicitly unknown.
    /// </summary>
    public string SelectLabel(string? sha256, string? backupSha256)
    {
        if (!IsLoaded) return string.Empty;
        var profile = Find(sha256) ?? Find(backupSha256);
        return profile?.Label ?? UnknownBuildLabel;
    }

    /// <summary>Human readable range, e.g. "2.99.0.0+".</summary>
    public string SupportDescription => IsLoaded ? MinimumVersion + "+" : "受支持的 PCL 版本";

    public string DetailText
    {
        get
        {
            if (!IsLoaded)
                return "未能从 PclPatcher 读取版本列表；补丁时仍会由 patcher 校验 SHA-256 与方法指纹。";

            var versions = string.Join("、", _profiles.ConvertAll(p => p.Label).ToArray());
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
    /// lines and extra trailing fields are ignored so the patcher can add headers
    /// or fields later without breaking older GUIs.
    /// </summary>
    public static PclVersionCatalog Parse(string? output)
    {
        if (string.IsNullOrEmpty(output)) return Empty;

        var profiles = new List<PclBuildProfile>();
        var supportsProbe = false;
        foreach (var raw in output!.Split('\n'))
        {
            var fields = raw.TrimEnd('\r').Split('\t');
            if (fields.Length > 1 && string.Equals(fields[0], FeatureTag, StringComparison.Ordinal))
            {
                if (string.Equals(fields[1].Trim(), "probe", StringComparison.OrdinalIgnoreCase))
                    supportsProbe = true;
                continue;
            }
            if (fields.Length < 4) continue;
            if (!string.Equals(fields[0], ProfileTag, StringComparison.Ordinal)) continue;

            var version = fields[1].Trim();
            var sha256 = fields[2].Trim();
            if (version.Length == 0 || sha256.Length != 64) continue;

            int.TryParse(fields[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var fingerprints);
            var channel = fields.Length > 4 ? fields[4].Trim() : "release";
            var label = fields.Length > 5 ? fields[5].Trim() : version;
            var verified = fields.Length > 6 && fields[6].Trim() == "1";
            var built = fields.Length > 7 ? fields[7].Trim() : string.Empty;
            profiles.Add(new PclBuildProfile(version, sha256, fingerprints, channel, label, verified, built));
        }

        profiles.Sort((a, b) => CompareVersions(a, b));
        return profiles.Count == 0 ? Empty : new PclVersionCatalog(profiles, supportsProbe);
    }

    private static int CompareVersions(PclBuildProfile left, PclBuildProfile right)
    {
        int byVersion;
        if (Version.TryParse(left.Version, out var a) && Version.TryParse(right.Version, out var b))
            byVersion = a.CompareTo(b);
        else
            byVersion = string.CompareOrdinal(left.Version, right.Version);

        // Same numeric version: keep the release build ahead of the snapshot.
        if (byVersion != 0) return byVersion;
        return string.CompareOrdinal(ChannelRank(left.Channel), ChannelRank(right.Channel));
    }

    private static string ChannelRank(string channel) => channel switch
    {
        "release" => "0",
        "snapshot" => "1",
        "beta" => "2",
        _ => "3"
    };
}
