namespace PclPatcher;

internal static partial class Program
{
    private const string BackupSuffix = ".bak";

    /// <summary>
    /// Decides which profile a source file follows. Resolution order matters:
    /// an exact sha256 is the strongest signal; a matching <c>.bak</c> means the
    /// source is already patched; and a build whose seven anchor fingerprints all
    /// match a profile is accepted as that profile even when its bytes are new
    /// (snapshot rebuilds). Only a build that matches nothing needs --force.
    /// </summary>
    private static BuildResolution ResolveBuild(ModuleDef module, string source, string hash, string backup, bool force, bool dryRun)
    {
        var version = FileVersionInfo.GetVersionInfo(source).FileVersion ?? "<unknown>";
        var channel = DetectChannel(source, version);
        Console.WriteLine($"version: {version}");
        if (channel != "unknown") Console.WriteLine($"build  : {version} {ChannelLabel(channel)}");
        Console.WriteLine($"support: {SupportedRange} / profiles: {string.Join(", ", Profiles.Select(p => p.Label))}");

        var profile = Profiles.FirstOrDefault(p => p.Sha256 == hash);
        if (profile is not null)
        {
            Console.WriteLine($"match  : exact sha256 → {profile.Label}");
            return new BuildResolution(profile, "exact", channel, true);
        }

        if (File.Exists(backup))
        {
            var backupHash = Sha256(backup);
            var backupProfile = Profiles.FirstOrDefault(p => p.Sha256 == backupHash);
            if (backupProfile is not null)
            {
                Console.WriteLine($"note   : source sha256 {hash} is not the known original,");
                Console.WriteLine($"         but backup matches {backupProfile.Label}; treating source as already patched");
                return new BuildResolution(backupProfile, "patched", channel, false);
            }
            Console.WriteLine($"WARN   : backup exists but its sha256 is unknown ({backupHash})");
        }

        var structural = MatchByFingerprints(module, channel, out var matched, out var total);
        if (structural is not null)
        {
            Console.WriteLine($"match  : structural {matched}/{total} fingerprints → {structural.Label} (unlisted build)");
            return new BuildResolution(structural, "structural", channel, true);
        }

        if (force)
        {
            Console.WriteLine("WARN   : unknown hash, continuing because --force was passed");
            return new BuildResolution(null, "forced", channel, false);
        }

        if (dryRun)
        {
            Console.WriteLine("WARN   : unknown hash; dry-run is allowed, write requires --force");
            return new BuildResolution(null, "dry-run", channel, false);
        }

        throw new InvalidOperationException(
            $"unverified PCL binary (version={version}, sha256={hash}); " +
            $"supported builds: {string.Join(", ", Profiles.Select(p => p.Label))} ({SupportedRange}). " +
            "Snapshots that keep the anchor methods are matched by fingerprint automatically; " +
            "for a build whose anchors moved run --adopt and add the printed profile, " +
            "or pass --force to write anyway.");
    }

    /// <summary>
    /// Saves the pristine source next to itself before an in-place replace. An
    /// existing backup is reused; it is only refreshed when the current source is
    /// a verified original build (i.e. the old backup is stale), never when the
    /// source already looks patched.
    /// </summary>
    private static void PrepareBackup(string source, string backup, string sourceHash, bool sourceIsKnownOriginal)
    {
        if (File.Exists(backup))
        {
            var backupHash = Sha256(backup);
            if (string.Equals(backupHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"backup : reusing {backup}");
                return;
            }

            if (!sourceIsKnownOriginal)
            {
                Console.WriteLine($"WARN   : existing backup does not match the source; keeping {backup}");
                return;
            }

            Console.WriteLine($"backup : refreshing stale backup {backup}");
            var staleAttributes = File.GetAttributes(backup);
            if ((staleAttributes & System.IO.FileAttributes.ReadOnly) != 0)
                File.SetAttributes(backup, staleAttributes & ~System.IO.FileAttributes.ReadOnly);
        }

        var temp = backup + ".tmp";
        try
        {
            TryDelete(temp);
            File.Copy(source, temp, true);
            var copiedHash = Sha256(temp);
            if (!string.Equals(copiedHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("backup verification failed (hash mismatch); source was not modified");
            File.Move(temp, backup, true);
            Console.WriteLine($"backup : saved original to {backup}");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static int RestoreSource(string source, string backup, bool dryRun, bool stopPcl)
    {
        if (!File.Exists(backup))
            throw new InvalidOperationException(
                $"no backup found at {backup}; run the patcher in place first, or restore the file manually");

        Console.WriteLine($"source : {source}");
        Console.WriteLine($"backup : {backup}");
        Console.WriteLine($"mode   : restore from backup");
        var backupHash = Sha256(backup);
        Console.WriteLine($"version: {FileVersionInfo.GetVersionInfo(backup).FileVersion ?? "<unknown>"}");
        Console.WriteLine($"sha256 : {backupHash}");

        try
        {
            using var module = ModuleDefMD.Load(backup);
            if (module.Find("PCL.ModMain", false) is null)
                throw new InvalidOperationException($"backup does not look like a PCL executable: {backup}");
        }
        catch (BadImageFormatException)
        {
            throw new InvalidOperationException($"backup is not a valid .NET assembly: {backup}");
        }

        if (File.Exists(source) &&
            string.Equals(Sha256(source), backupHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("already: source already matches the backup; nothing to restore");
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine("dry-run: no file written");
            return 0;
        }

        StopRunningPcl(source, stopPcl);
        EnsureDirectoryWritable(Path.GetDirectoryName(source)!);
        var temp = source + ".restore.tmp";
        try
        {
            TryDelete(temp);
            File.Copy(backup, temp, true);
            var tempAttributes = File.GetAttributes(temp);
            if ((tempAttributes & System.IO.FileAttributes.ReadOnly) != 0)
                File.SetAttributes(temp, tempAttributes & ~System.IO.FileAttributes.ReadOnly);
            if (!string.Equals(Sha256(temp), backupHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("restore verification failed (hash mismatch); source was not modified");

            if (File.Exists(source)) ReplaceFile(temp, source);
            else File.Move(temp, source, true);
        }
        finally
        {
            TryDelete(temp);
        }

        Console.WriteLine();
        Console.WriteLine($"restored: {source}");
        Console.WriteLine($"sha256  : {Sha256(source)}");
        Console.WriteLine($"backup  : {backup} (kept)");
        return 0;
    }

    /// <summary>
    /// Checks that the destination folder accepts a new file before any patching
    /// work is written. Some security suites (Lenovo PCManager/Huorong and
    /// similar) allow signed hosts to write a folder but block unsigned exes;
    /// without this preflight the failure only surfaces after the IL work is done.
    /// </summary>
    private static void EnsureDirectoryWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(WriteFailure(directory, ex), ex);
        }

        var probe = Path.Combine(directory, ".pclpatcher-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.WriteByte(0);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            throw new InvalidOperationException(WriteFailure(directory, ex), ex);
        }
        finally
        {
            TryDelete(probe);
        }
    }

    private static string WriteFailure(string directory, Exception ex) =>
        $"cannot write to {directory}: {ex.Message.TrimEnd('.')}" + Environment.NewLine +
        "  This is usually a permissions or security-software rule (for example Lenovo PCManager/Huorong)" + Environment.NewLine +
        "  blocking unsigned executables from writing that folder. Run as administrator, add PclPatcher.exe" + Environment.NewLine +
        "  to the trust list, or pass an explicit output path in a writable folder.";

    /// <summary>
    /// Closes running PCL instances before an in-place change. Windows maps the
    /// launcher's exe and refuses to replace it while the launcher is open, so
    /// this keeps the default in-place mode usable. Pass --no-stop to opt out.
    /// </summary>
    private static void StopRunningPcl(string source, bool enabled)
    {
        if (!enabled) return;

        var baseName = Path.GetFileNameWithoutExtension(source);
        var found = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;

                string name;
                try { name = process.ProcessName; }
                catch { continue; }

                if (name.StartsWith("PclPatcher", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) continue;

                var matches = string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    try { matches = string.Equals(process.MainModule?.FileName, source, StringComparison.OrdinalIgnoreCase); }
                    catch { }
                }
                if (!matches) continue;

                found++;
                Console.WriteLine($"stop   : {name} (pid {process.Id})");
                try
                {
                    var closed = false;
                    try { closed = process.CloseMainWindow(); } catch { }
                    if (!closed || !process.WaitForExit(3000))
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    Console.WriteLine($"stopped: {name} (pid {process.Id})");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"cannot stop {name} (pid {process.Id}): {ex.Message}. Close PCL manually and retry.", ex);
                }
            }
        }

        // A constrained process job can hide the launcher from GetProcesses();
        // taskkill goes through the system service and still sees it.
        if (found == 0) KillByImageName(baseName);
    }

    private static void KillByImageName(string baseName)
    {
        var image = baseName + ".exe";
        try
        {
            var psi = new ProcessStartInfo("taskkill", $"/IM \"{image}\" /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process is null) return;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode == 0)
            {
                Console.WriteLine($"stop   : taskkill {image}");
                Console.WriteLine($"stopped: {image}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("WARN   : taskkill failed: " + ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var attributes = File.GetAttributes(path);
            if ((attributes & System.IO.FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~System.IO.FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch
        {
            // Best effort: a leftover temp file must not mask the real result.
        }
    }

    /// <summary>
    /// Replaces a file with a same-directory temp file. The destination keeps its
    /// ACL because the temp file is created next to it, and File.Replace avoids a
    /// window where the destination is missing.
    /// </summary>
    private static void ReplaceFile(string temp, string destination)
    {
        var attributes = File.GetAttributes(destination);
        var readOnly = (attributes & System.IO.FileAttributes.ReadOnly) != 0;
        if (readOnly) File.SetAttributes(destination, attributes & ~System.IO.FileAttributes.ReadOnly);

        try
        {
            try
            {
                File.Replace(temp, destination, null, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(temp, destination, true);
            }
        }
        finally
        {
            if (readOnly && File.Exists(destination))
                File.SetAttributes(destination, File.GetAttributes(destination) | System.IO.FileAttributes.ReadOnly);
        }
    }
}
