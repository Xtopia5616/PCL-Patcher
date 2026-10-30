using System.Collections.Concurrent;

namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // Build identity: release vs snapshot, hash vs structural fingerprints
    // ---------------------------------------------------------------------

    /// <summary>
    /// A PCL build found in the source file. <paramref name="Match"/> is one of
    /// <c>exact</c> (sha256 in the profile table), <c>structural</c> (all method
    /// fingerprints match a profile, hash unknown), <c>patched</c> (source is a
    /// patched copy whose .bak is a known build), <c>forced</c> (--force bypassed
    /// the checks) or <c>unknown</c>.
    /// </summary>
    private sealed record BuildResolution(PclProfile? Profile, string Match, string Channel, bool IsKnownOriginal);

    /// <summary>
    /// The build a file resembles most, plus the anchors that differ. Only ever
    /// reported, never used to choose a patch variant: every step is driven by
    /// its own Probe, so "closest" must not become an implicit strategy.
    /// </summary>
    private sealed record ClosestMatch(
        PclProfile Profile,
        int Matched,
        int Total,
        IReadOnlyList<(string Key, string Detail)> Differences);

    private sealed record FingerprintComparison(int Matched, List<(string Key, string Detail)> Differences);

    // Profile keys are "Type::Method/argc"; parsing them once keeps the matching
    // and verification loops from re-splitting the same seven strings per profile.
    private static readonly ConcurrentDictionary<string, (string Type, string Method, int ArgCount)> FingerprintKeys = new();

    private static (string Type, string Method, int ArgCount) ParseFingerprintKey(string key) =>
        FingerprintKeys.GetOrAdd(key, static k =>
        {
            var halves = k.Split("::");
            if (halves.Length != 2) return (string.Empty, string.Empty, -1);
            var method = halves[1].Split('/');
            if (method.Length != 2 || !int.TryParse(method[1], out var count)) return (string.Empty, string.Empty, -1);
            return (halves[0], method[0], count);
        });

    /// <summary>Fingerprint of the anchor named by a "Type::Method/argc" key, or null when it is missing.</summary>
    private static string? FingerprintOf(ModuleDef module, string key)
    {
        var (typeName, methodName, argCount) = ParseFingerprintKey(key);
        if (typeName.Length == 0) return null;
        var type = module.Find(typeName, false);
        var method = type?.Methods.FirstOrDefault(m => m.Name == methodName && m.MethodSig.Params.Count == argCount);
        return method?.Body is null ? null : Fingerprint(method);
    }

    /// <summary>
    /// Compares one profile's anchors against the module. This is the only place
    /// that knows how a profile is verified, shared by the hash check, structural
    /// matching and the "closest build" hint, so those three cannot disagree.
    /// </summary>
    private static FingerprintComparison CompareFingerprints(ModuleDef module, PclProfile profile)
    {
        var matched = 0;
        var differences = new List<(string Key, string Detail)>();
        foreach (var (key, expected) in profile.Fingerprints)
        {
            var actual = FingerprintOf(module, key);
            if (actual == expected)
            {
                matched++;
                continue;
            }

            differences.Add((key, actual is null ? "missing" : $"expected {expected}, got {actual}"));
        }
        return new FingerprintComparison(matched, differences);
    }

    /// <summary>
    /// Reads the channel word the launcher stamps next to its own version
    /// ("PCL 版本：快照版 2.99.0.0", "PCL 已更新至快照版 …"). PCL keeps the numeric
    /// version of the release line in snapshots, so the hash alone cannot tell
    /// the two apart; the banner text can. The #US heap stores UTF-16 strings:
    /// searching the raw bytes for the encoded phrase finds it at either heap
    /// alignment, and unlike decoding the whole image twice it does not allocate
    /// two multi-megabyte strings.
    /// </summary>
    private static string DetectChannel(string path, string version)
    {
        if (string.IsNullOrEmpty(version) || version == "<unknown>") return "unknown";

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return "unknown";
        }

        var span = new ReadOnlySpan<byte>(bytes);
        foreach (var (channel, word) in new[] { ("snapshot", "快照版"), ("beta", "测试版"), ("release", "正式版") })
        {
            if (span.IndexOf(Encoding.Unicode.GetBytes(word + " " + version)) >= 0) return channel;
        }
        return "unknown";
    }

    /// <summary>
    /// Matches an unlisted build by the IL fingerprints of the seven anchor
    /// methods. Snapshot rebuilds keep the numeric version and change bytes, so
    /// this is what makes a new snapshot work without a new profile entry. A
    /// profile from the same channel wins; otherwise the first fingerprint match
    /// is used and the caller warns about the channel mismatch.
    /// <paramref name="closest"/> always describes the best partial match, which
    /// is what the unknown-build path reports when --force is needed.
    /// </summary>
    private static PclProfile? MatchByFingerprints(ModuleDef module, string channel, out ClosestMatch? closest,
        bool quiet = false)
    {
        var candidates = new List<PclProfile>();
        closest = null;
        foreach (var profile in Profiles)
        {
            var total = profile.Fingerprints.Count;
            if (total == 0) continue;

            var comparison = CompareFingerprints(module, profile);
            var match = new ClosestMatch(profile, comparison.Matched, total, comparison.Differences);
            if (comparison.Matched == total) candidates.Add(profile);
            if (IsCloser(match, closest, channel)) closest = match;
        }

        if (candidates.Count == 0) return null;
        var sameChannel = candidates.FirstOrDefault(p => p.Channel == channel);
        if (sameChannel is not null) return sameChannel;

        var fallback = candidates[0];
        if (!quiet)
            Console.WriteLine($"WARN   : 指纹匹配 {fallback.Label}，但渠道标记是 {ChannelLabel(channel)}；按指纹继续");
        return fallback;
    }

    private static bool IsCloser(ClosestMatch candidate, ClosestMatch? current, string channel)
    {
        if (current is null) return true;
        if (candidate.Matched != current.Matched) return candidate.Matched > current.Matched;
        return candidate.Profile.Channel == channel && current.Profile.Channel != channel;
    }

    /// <summary>
    /// Renders the best partial match as two log lines, e.g.
    /// <c>closest: 2.99.0.1 (6/7)</c> plus up to three anchor differences.
    /// </summary>
    private static void PrintClosest(ClosestMatch? closest)
    {
        if (closest is null || closest.Matched == 0) return;

        Console.WriteLine($"closest: {closest.Profile.Label} ({closest.Matched}/{closest.Total})");
        foreach (var (key, detail) in closest.Differences.Take(3))
            Console.WriteLine($"         {key} {detail}");
        var hidden = closest.Differences.Count - 3;
        if (hidden > 0) Console.WriteLine($"         … 另有 {hidden} 个锚点不一致");
    }

    /// <summary>
    /// Machine readable build report used by the GUI: the hash is unknown to the
    /// profile table, but the GUI must still be able to tell an unlisted snapshot
    /// rebuild (structurally identical) from an unrelated binary. The trailing
    /// <c>closest</c> line carries the best partial match for the unknown case.
    /// </summary>
    private static int Identify(string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("source not found", source);

        var version = FileVersionInfo.GetVersionInfo(source).FileVersion ?? "<unknown>";
        var hash = Sha256(source);
        var channel = DetectChannel(source, version);

        using var module = ModuleDefMD.Load(source);
        var profile = Profiles.FirstOrDefault(p => p.Sha256 == hash);
        var matchedProfile = profile;
        var match = profile is not null ? "exact" : "unknown";
        var reportedChannel = profile?.Channel ?? channel;
        var label = profile?.Label ?? (channel == "unknown" ? version + " 未识别渠道" : $"{version} {ChannelLabel(channel)}");

        ClosestMatch? closest = null;
        if (profile is null)
        {
            var structural = MatchByFingerprints(module, channel, out closest, quiet: true);
            if (structural is not null)
            {
                match = "structural";
                matchedProfile = structural;
                label = $"{version} {ChannelLabel(channel == "unknown" ? structural.Channel : channel)}";
            }
            else if (closest is not null && closest.Matched == 0)
            {
                // Nothing in common: reporting it as "closest" would be noise.
                closest = null;
            }
        }

        Console.WriteLine("identify\t1");
        Console.WriteLine($"version\t{version}");
        Console.WriteLine($"channel\t{reportedChannel}");
        Console.WriteLine($"build\t{label}");
        Console.WriteLine($"match\t{match}");
        Console.WriteLine($"profile\t{matchedProfile?.Label ?? string.Empty}");
        Console.WriteLine(
            $"closest\t{closest?.Profile.Label ?? string.Empty}\t" +
            $"{(closest is null ? string.Empty : closest.Matched + "/" + closest.Total)}");
        Console.WriteLine($"sha256\t{hash}");
        return 0;
    }

    /// <summary>
    /// Maintainer helper: prints everything needed to add a new build to
    /// <see cref="Profiles"/> (channel, sha256, seven fingerprints, probe matrix)
    /// plus a ready to paste C# entry. This is what "支持后续版本" means in
    /// practice for builds whose anchors actually moved.
    /// </summary>
    private static int Adopt(string source, bool includeLink, bool save)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("source not found", source);

        var version = FileVersionInfo.GetVersionInfo(source).FileVersion ?? "<unknown>";
        var hash = Sha256(source);
        var channel = DetectChannel(source, version);
        var built = DateTime.Now.ToString("yyyy-MM-dd");

        using var module = ModuleDefMD.Load(source);

        Console.WriteLine($"version : {version}");
        Console.WriteLine($"channel : {channel} ({ChannelLabel(channel)})");
        Console.WriteLine($"sha256  : {hash}");
        Console.WriteLine();
        Console.WriteLine("fingerprints:");
        var entries = ReadKeyFingerprints(module);
        foreach (var (key, value) in entries)
            Console.WriteLine($"  {key,-58} {value}");

        Console.WriteLine();
        Console.WriteLine("probe matrix:");
        foreach (var step in BuildPlan(noUpdate: false, includeLink: includeLink))
        {
            ProbeResult probe;
            try
            {
                probe = step.Probe(module);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {step.Name,-28} ERROR {ex.Message}");
                continue;
            }
            Console.WriteLine($"  {step.Name,-28} {probe.State,-14} {probe.Reason}");
        }

        Console.WriteLine();
        Console.WriteLine("C# profile entry:");
        Console.WriteLine("        new(");
        Console.WriteLine($"            \"{version}\",");
        Console.WriteLine($"            \"{hash}\",");
        Console.WriteLine("            new Dictionary<string, string>");
        Console.WriteLine("            {");
        for (var i = 0; i < entries.Count; i++)
        {
            var comma = i == entries.Count - 1 ? string.Empty : ",";
            Console.WriteLine($"                [\"{entries[i].Key}\"] = \"{entries[i].Value}\"{comma}");
        }
        Console.WriteLine("            },");
        if (channel != "release")
            Console.WriteLine($"            Channel: \"{channel}\",");
        Console.WriteLine($"            Built: \"{built}\"),");

        if (save)
        {
            SaveAdoptedProfile(version, hash, channel, built, entries, auto: false);
            Console.WriteLine();
            Console.WriteLine("         后续运行（含 GUI）会直接识别该构建。");
        }

        return 0;
    }

    /// <summary>The seven anchor fingerprints of a module, in profile-key form.</summary>
    private static List<(string Key, string Value)> ReadKeyFingerprints(ModuleDef module) =>
        KeyMethods
            .Select(m =>
            {
                var key = $"{m.Type}::{m.Method}/{m.Params}";
                return (Key: key, Value: FingerprintOf(module, key) ?? "<missing>");
            })
            .ToList();

    /// <summary>
    /// Appends one build to the external profile file, replacing any earlier line
    /// for the same hash. <paramref name="auto"/> marks a build the tool recorded
    /// itself after a forced in-place patch (see TryRememberOriginal); such a build
    /// is never "verified", only "adopted".
    /// </summary>
    private static void SaveAdoptedProfile(string version, string hash, string channel, string built,
        IReadOnlyList<(string Key, string Value)> entries, bool auto)
    {
        var line = $"build\t{version}\t{hash}\t{channel}\t{built}\t" +
                   string.Join(";", entries.Select(e => e.Key + "=" + e.Value)) +
                   (auto ? "\tauto" : string.Empty);
        var directory = Path.GetDirectoryName(ExternalProfilePath)!;
        Directory.CreateDirectory(directory);

        var lines = File.Exists(ExternalProfilePath)
            ? File.ReadAllLines(ExternalProfilePath).Where(l => !IsSameBuildLine(l, hash)).ToList()
            : new List<string>();
        if (auto)
            lines.Add($"# auto-adopted by --force on {DateTime.Now:yyyy-MM-dd HH:mm} (unverified): {hash}");
        lines.Add(line);
        File.WriteAllLines(ExternalProfilePath, lines);

        Console.WriteLine();
        Console.WriteLine($"saved  : {ExternalProfilePath}");
        if (auto)
            Console.WriteLine("         已按未验证构建收录；下次还原/重打不再需要 --force。");
    }

    private static bool IsSameBuildLine(string line, string hash)
    {
        var fields = line.Split('\t');
        return fields.Length > 2 && fields[0] == "build" &&
               string.Equals(fields[2].Trim(), hash, StringComparison.OrdinalIgnoreCase);
    }
}
