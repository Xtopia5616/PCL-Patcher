namespace PclPatcher;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        try
        {
            // The GUI (and any other host that redirects stdout) has no console, so
            // without this .NET would encode Chinese profile labels with the OEM
            // code page (936) while the reader decodes UTF-8: mojibake in the UI and
            // in the log panel.
            if (Console.IsOutputRedirected)
            {
                try { Console.OutputEncoding = new UTF8Encoding(false); }
                catch (IOException) { }
            }

            // Machine readable entries for the GUIs; they must stay free of the
            // banner and of any PCL build so they can run without a source file
            // or while another tool parses stdout.
            if (args.Contains("--profiles"))
            {
                PrintProfiles();
                return 0;
            }

            var source = ResolveSource(args);
            if (args.Contains("--identify")) return Identify(source);
            if (args.Contains("--adopt"))
                return Adopt(source, includeLink: !args.Contains("--download-only"), save: args.Contains("--save"));

            return Run(args);
        }
        catch (Exception ex)
        {
            var hint = ex is IOException
                ? " (close PCL if it is currently running, then retry)"
                : string.Empty;
            Console.Error.WriteLine("PATCHER_ERROR: " + ex.Message + hint);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        Console.WriteLine("PclPatcher 1.1 · 第三方 IL 补丁工具");
        Console.WriteLine("Some birds aren't meant to be caged. Their feathers are just too bright.");
        Console.WriteLine("And when they fly away, the part of you that knows it was a sin to lock them up does rejoice.");
        Console.WriteLine();

        var source = ResolveSource(args);
        var outputArg = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).Skip(1).FirstOrDefault();
        var output = Path.GetFullPath(outputArg ?? source);
        var inPlace = string.Equals(source, output, StringComparison.OrdinalIgnoreCase);
        if (inPlace) output = source;
        var backup = GetBackupPath(source);
        var dryRun = args.Contains("--dry-run");
        var force = args.Contains("--force");
        var noUpdate = args.Contains("--no-update");
        var downloadOnly = args.Contains("--download-only");
        var allowPartial = args.Contains("--allow-partial");
        var printFingerprints = args.Contains("--fingerprints");
        var restore = args.Contains("--restore");
        var stopPcl = !args.Contains("--no-stop");

        if (printFingerprints)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("source not found", source);
            using var inspect = ModuleDefMD.Load(source);
            PrintFingerprints(inspect);
            return 0;
        }

        if (restore) return RestoreSource(source, backup, dryRun, stopPcl);

        if (!File.Exists(source)) throw new FileNotFoundException("source not found", source);
        var sourceHash = Sha256(source);
        var plan = BuildPlan(noUpdate, includeLink: !downloadOnly);

        using var module = ModuleDefMD.Load(source);
        var resolution = ResolveBuild(module, source, sourceHash, backup, force, dryRun);
        var profile = resolution.Profile;
        var sourceIsKnownOriginal = resolution.IsKnownOriginal;

        Console.WriteLine($"source : {source}");
        Console.WriteLine($"mode   : {(inPlace ? "in-place, source will be replaced" : "separate output file")}");
        Console.WriteLine($"scope  : {(downloadOnly ? "download only" : "download + link")}");
        Console.WriteLine($"output : {output}");
        if (inPlace) Console.WriteLine($"backup : {backup}");
        Console.WriteLine($"sha256 : {sourceHash}");
        Console.WriteLine();

        if (profile is not null && sourceIsKnownOriginal)
            CheckFingerprints(module, profile, force || dryRun);
        else if (profile is not null)
            Console.WriteLine("fp     : skipped (source is already patched; backup matches a known build)");

        var pending = new List<PatchStep>();
        foreach (var step in plan)
        {
            var probe = step.Probe(module);
            if (probe.State == PatchState.AlreadyPatched)
            {
                Console.WriteLine($"probe  : {step.Name,-28} already patched ({probe.Reason})");
                continue;
            }

            if (probe.State == PatchState.Missing)
            {
                if (step.Required && !allowPartial)
                    throw new InvalidOperationException(
                        $"{step.Name}: {probe.Reason}. Pass --allow-partial to skip this step.");
                Console.WriteLine($"probe  : {step.Name,-28} SKIP ({probe.Reason})");
                continue;
            }

            pending.Add(step);
        }

        // Stop the launcher and verify the destination before mutating the
        // module, so a locked/protected folder fails before any work is shown.
        if (!dryRun)
        {
            if (inPlace && pending.Count > 0)
            {
                EnsureDirectoryWritable(Path.GetDirectoryName(output)!);
                StopRunningPcl(source, stopPcl);
            }
            else if (!inPlace)
            {
                EnsureDirectoryWritable(Path.GetDirectoryName(output)!);
            }
        }

        var applied = new List<PatchStep>();
        foreach (var step in pending)
        {
            var changes = step.Apply(module);
            step.Verify(module);
            applied.Add(step);
            Console.WriteLine($"patch  : {step.Name,-28} {changes}");
        }

        try
        {
            ValidateIl(module);
        }
        catch (InvalidOperationException ex) when (applied.Count == 0)
        {
            // Nothing was applied: the source is an older patch product.
            // Point at restore instead of leaking a raw IL message.
            throw new InvalidOperationException(
                ex.Message + Environment.NewLine +
                "  源文件是旧版补丁产物，先还原再重打：--restore（GUI：还原备份 → 开始补丁）", ex);
        }
        Console.WriteLine("verify : IL structure OK");

        if (dryRun)
        {
            Console.WriteLine("dry-run: no file written");
            return 0;
        }

        if (inPlace && applied.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("already: source is already patched; nothing written");
            return 0;
        }

        var temp = output + ".tmp";
        try
        {
            TryDelete(temp);
            module.Write(temp, new ModuleWriterOptions(module));

            using (var check = ModuleDefMD.Load(temp))
            {
                foreach (var step in applied) step.Verify(check);
                ValidateIl(check);
            }

            if (inPlace)
            {
                PrepareBackup(source, backup, sourceHash, sourceIsKnownOriginal);
                ReplaceFile(temp, output);
            }
            else
            {
                File.Move(temp, output, true);
            }
        }
        finally
        {
            TryDelete(temp);
        }

        Console.WriteLine();
        Console.WriteLine($"written: {output}");
        Console.WriteLine($"sha256 : {Sha256(output)}");
        if (inPlace) Console.WriteLine($"backup : {backup}");
        Console.WriteLine("verify : output reloaded and check passed");
        return 0;
    }

    /// <summary>
    /// Tab separated profile list consumed by the GUIs. The number after the
    /// "profiles" tag is the format version (3 = added channel/label/verified/built),
    /// not the build count. Header/tag lines keep the format self-describing, and
    /// extra fields/lines can be added without breaking the parser in PclVersionCatalog.
    /// </summary>
    private static void PrintProfiles()
    {
        Console.WriteLine("profiles\t3");
        foreach (var profile in Profiles)
            Console.WriteLine(
                $"profile\t{profile.Version}\t{profile.Sha256}\t{profile.Fingerprints.Count}\t{profile.Channel}\t{profile.Label}\t" +
                $"{(profile.Verified ? 1 : 0)}\t{profile.Built}");
        Console.WriteLine($"support\t{SupportedRange}");
    }

    private static string ResolveSource(string[] args)
    {
        var source = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (source is null)
            throw new InvalidOperationException(
                "usage: PclPatcher <PCL.exe> [output.exe] [--restore] [--dry-run] [--force] [--allow-partial] " +
                "[--download-only] [--no-update] [--no-stop] [--fingerprints] [--identify] [--adopt] [--profiles]");
        return Path.GetFullPath(source);
    }

    private static string GetBackupPath(string source) => source + BackupSuffix;

    // Link steps are opt-in at the call site: --download-only leaves them out so
    // a user who only wants the download entry never touches PageLinkMain.
    // DisableSelfUpdate stays orthogonal and is appended last, as before.
    private static List<PatchStep> BuildPlan(bool noUpdate, bool includeLink)
    {
        var plan = new List<PatchStep>
        {
            new("DownloadLeftCtor", true, ProbeDownloadLeftCtor, PatchDownloadLeftCtor, VerifyDownloadLeftCtor),
            new("ModifyCard", true, ProbeModifyCard, PatchModifyCard, VerifyModifyCard),
            new("ExtraUiEntries", false, ProbeExtraUiEntries, PatchExtraUiEntries, VerifyExtraUiEntries),
            new("ModpackInstall", false, ProbeModpackInstall, PatchModpackInstall, VerifyModpackInstall),
            new("LoaderInit", true, ProbeLoaderInit, PatchLoaderInit, VerifyLoaderInit),
            new("CurseForgeMirror", false, ProbeCurseForgeMirror, PatchCurseForgeMirror, VerifyCurseForgeMirror)
        };

        if (includeLink)
        {
            plan.Add(new("RestoreLinkEntry", true, ProbeRestoreLinkEntry, PatchRestoreLinkEntry, VerifyRestoreLinkEntry));
            plan.Add(new("LinkIgnoreDisable", true, ProbeLinkIgnoreDisable, PatchLinkIgnoreDisable, VerifyLinkIgnoreDisable));
            plan.Add(new("LinkRequireCustomPeer", true, ProbeLinkRequireCustomPeer, PatchLinkRequireCustomPeer, VerifyLinkRequireCustomPeer));
            plan.Add(new("LinkPrecheckCustomPeer", true, ProbeLinkPrecheckCustomPeer, PatchLinkPrecheckCustomPeer, VerifyLinkPrecheckCustomPeer));
            plan.Add(new("LinkNoticeText", true, ProbeLinkNoticeText, PatchLinkNoticeText, VerifyLinkNoticeText));
            plan.Add(new("LinkPeerHint", true, ProbeLinkPeerHint, PatchLinkPeerHint, VerifyLinkPeerHint));
            plan.Add(new("LinkPeerButtons", true, ProbeLinkPeerButtons, PatchLinkPeerButtons, VerifyLinkPeerButtons));
        }

        if (noUpdate)
            plan.Add(new("DisableSelfUpdate", true, ProbeDisableSelfUpdate, PatchDisableSelfUpdate, VerifyDisableSelfUpdate));

        return plan;
    }
}
