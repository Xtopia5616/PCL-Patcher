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
    /// patched copy whose .bak is a known build) or <c>unknown</c>.
    /// </summary>
    private sealed record BuildResolution(PclProfile? Profile, string Match, string Channel, bool IsKnownOriginal);

    /// <summary>
    /// Reads the channel word the launcher stamps next to its own version
    /// ("PCL 版本：快照版 2.99.0.0", "PCL 已更新至快照版 …"). PCL keeps the numeric
    /// version of the release line in snapshots, so the hash alone cannot tell
    /// the two apart; the banner text can. The #US heap stores UTF-16 strings, so
    /// scanning the raw image avoids depending on a dnlib heap API.
    /// </summary>
    private static string DetectChannel(string path, string version)
    {
        if (string.IsNullOrEmpty(version) || version == "<unknown>") return "unknown";

        string text;
        try
        {
            var bytes = File.ReadAllBytes(path);
            // The heap may start at an odd file offset; decode both alignments.
            text = Encoding.Unicode.GetString(bytes) + "\n" + Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return "unknown";
        }

        foreach (var candidate in new[] { ("snapshot", "快照版"), ("beta", "测试版"), ("release", "正式版") })
        {
            if (text.Contains(candidate.Item2 + " " + version, StringComparison.Ordinal))
                return candidate.Item1;
        }
        return "unknown";
    }

    /// <summary>
    /// Matches an unlisted build by the IL fingerprints of the seven anchor
    /// methods. Snapshot rebuilds keep the numeric version and change bytes, so
    /// this is what makes a new snapshot work without a new profile entry. A
    /// profile from the same channel wins; otherwise the first fingerprint match
    /// is used and the caller warns about the channel mismatch.
    /// </summary>
    private static PclProfile? MatchByFingerprints(ModuleDef module, string channel, out int matched, out int total,
        bool quiet = false)
    {
        matched = 0;
        total = 0;
        var candidates = new List<PclProfile>();
        foreach (var profile in Profiles)
        {
            var profileMatched = 0;
            var profileTotal = 0;
            foreach (var (key, expected) in profile.Fingerprints)
            {
                profileTotal++;
                var parts = key.Split("::");
                var methodPart = parts[1].Split('/');
                var type = module.Find(parts[0], false);
                var method = type?.Methods.FirstOrDefault(m =>
                    m.Name == methodPart[0] && m.MethodSig.Params.Count == int.Parse(methodPart[1]));
                if (method?.Body is not null && Fingerprint(method) == expected) profileMatched++;
            }

            if (profileTotal > 0 && profileMatched == profileTotal) candidates.Add(profile);
            if (profileTotal > matched) matched = profileMatched;
            if (profileTotal > total) total = profileTotal;
        }

        if (candidates.Count == 0) return null;
        var sameChannel = candidates.FirstOrDefault(p => p.Channel == channel);
        if (sameChannel is not null) return sameChannel;

        var fallback = candidates[0];
        if (!quiet)
            Console.WriteLine($"WARN   : 指纹匹配 {fallback.Label}，但渠道标记是 {ChannelLabel(channel)}；按指纹继续");
        return fallback;
    }

    /// <summary>
    /// Machine readable build report used by the GUI: the hash is unknown to the
    /// profile table, but the GUI must still be able to tell an unlisted snapshot
    /// rebuild (structurally identical) from an unrelated binary.
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

        if (profile is null)
        {
            var structural = MatchByFingerprints(module, channel, out var matched, out var total, quiet: true);
            if (structural is not null)
            {
                match = "structural";
                matchedProfile = structural;
                label = $"{version} {ChannelLabel(channel == "unknown" ? structural.Channel : channel)}";
            }
        }

        Console.WriteLine("identify\t1");
        Console.WriteLine($"version\t{version}");
        Console.WriteLine($"channel\t{reportedChannel}");
        Console.WriteLine($"build\t{label}");
        Console.WriteLine($"match\t{match}");
        Console.WriteLine($"profile\t{matchedProfile?.Label ?? string.Empty}");
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
        var entries = new List<(string Key, string Value)>();
        foreach (var (typeName, methodName, paramCount) in KeyMethods)
        {
            var type = module.Find(typeName, false);
            var method = type?.Methods.FirstOrDefault(m => m.Name == methodName && m.MethodSig.Params.Count == paramCount);
            var key = $"{typeName}::{methodName}/{paramCount}";
            var value = method?.Body is null ? "<missing>" : Fingerprint(method);
            entries.Add((key, value));
            Console.WriteLine($"  {key,-58} {value}");
        }

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
            var line = $"build\t{version}\t{hash}\t{channel}\t{built}\t" +
                       string.Join(";", entries.Select(e => e.Key + "=" + e.Value));
            var directory = Path.GetDirectoryName(ExternalProfilePath)!;
            Directory.CreateDirectory(directory);

            var lines = File.Exists(ExternalProfilePath)
                ? File.ReadAllLines(ExternalProfilePath).Where(l => !IsSameBuildLine(l, hash)).ToList()
                : new List<string>();
            lines.Add(line);
            File.WriteAllLines(ExternalProfilePath, lines);
            Console.WriteLine();
            Console.WriteLine($"saved  : {ExternalProfilePath}");
            Console.WriteLine("         后续运行（含 GUI）会直接识别该构建。");
        }

        return 0;
    }

    private static bool IsSameBuildLine(string line, string hash)
    {
        var fields = line.Split('\t');
        return fields.Length > 2 && fields[0] == "build" &&
               string.Equals(fields[2].Trim(), hash, StringComparison.OrdinalIgnoreCase);
    }
}
