namespace PclPatcher.Gui.Net48;

/// <summary>One PCL executable found on this machine.</summary>
internal sealed class PclCandidate
{
    public string Path;
    public string Version;
    public long Size;
    public DateTime Modified;
    public int Score;
    public string Source;
    public bool HasBackup;
    public bool HasConfig;
    public bool IsRunning;
}

/// <summary>Progress ping from the locator: either a phase message or a new hit.</summary>
internal sealed class PclScanUpdate
{
    public string Status;
    public PclCandidate Candidate;
    public int Count;
}

internal sealed class PclScanOptions
{
    public IReadOnlyList<string> Seeds = Array.Empty<string>();
    public string MinimumVersion;
    public bool IncludeShallow = true;
    public TimeSpan ShallowBudget = TimeSpan.FromSeconds(2.5);
}

/// <summary>
/// Finds the user's PCL without a full disk walk. Sources, cheapest first:
/// remembered paths, the paths already in the window, running processes (exact),
/// registry hints, shortcuts, recent-app history, and a bounded shallow walk of
/// the usual install locations. Every hit is validated against PCL's own
/// product/assembly metadata so a random launcher.exe is never accepted.
/// </summary>
internal static class PclLocator
{
    private const int ProcessQueryLimitedInformation = 0x1000;

    private static readonly string[] SkippedDirectoryNames =
    {
        "Windows", "WinSxS", "WindowsApps", "$Recycle.Bin", "System Volume Information",
        "Recovery", "PerfLogs", "ProgramData", "AppData", "node_modules", ".git", ".svn", ".hg",
        "bin", "obj", "Cache", "Caches", "Temp", "Tmp", "Packages", "NuGet", "pip",
        "site-packages", "__pycache__", "Config.Msi", "OneDriveTemp"
    };

    public static Task<List<PclCandidate>> QuickScanAsync(
        PclScanOptions options,
        IProgress<PclScanUpdate> progress,
        CancellationToken token)
        => Task.Run(() => QuickScan(options, progress, token), token);

    public static List<PclCandidate> QuickScan(
        PclScanOptions options,
        IProgress<PclScanUpdate> progress = null,
        CancellationToken token = default)
    {
        options ??= new PclScanOptions();
        var found = new Dictionary<string, PclCandidate>(StringComparer.OrdinalIgnoreCase);

        token.ThrowIfCancellationRequested();
        Report(progress, "正在检查启动参数与最近使用…", found.Count);
        foreach (var seed in options.Seeds ?? Array.Empty<string>())
        {
            token.ThrowIfCancellationRequested();
            Consider(found, seed, "启动路径", options.MinimumVersion, progress);
        }

        token.ThrowIfCancellationRequested();
        Report(progress, "正在从运行中的进程反查…", found.Count);
        foreach (var running in RunningProcesses(options.MinimumVersion))
        {
            token.ThrowIfCancellationRequested();
            Merge(found, running);
            Report(progress, "已从运行中的进程找到 PCL", found.Count, running);
        }

        token.ThrowIfCancellationRequested();
        Report(progress, "正在检查注册表与快捷方式…", found.Count);
        foreach (var path in Safe(RegistryEntries()))
        {
            token.ThrowIfCancellationRequested();
            Consider(found, path, "注册表", options.MinimumVersion, progress);
        }
        foreach (var path in Safe(RecentUsageEntries()))
        {
            token.ThrowIfCancellationRequested();
            Consider(found, path, "最近使用", options.MinimumVersion, progress);
        }
        foreach (var path in Safe(ShortcutEntries()))
        {
            token.ThrowIfCancellationRequested();
            Consider(found, path, "快捷方式", options.MinimumVersion, progress);
        }

        if (options.IncludeShallow)
        {
            var clock = Stopwatch.StartNew();
            foreach (var root in Safe(ShallowRoots()))
            {
                if (clock.Elapsed > options.ShallowBudget) break;
                token.ThrowIfCancellationRequested();
                Report(progress, "正在扫描 " + root.Key + "…", found.Count);
                foreach (var file in Walk(root.Key, root.Value, clock, options.ShallowBudget))
                {
                    token.ThrowIfCancellationRequested();
                    Consider(found, file, "磁盘扫描", options.MinimumVersion, progress);
                }
            }
        }

        var list = new List<PclCandidate>(found.Values);
        list.Sort(CompareCandidates);
        return list;
    }

    /// <summary>True when the file really is PCL, judged by its version info or assembly identity.</summary>
    public static bool LooksLikePcl(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var haystack = (info.ProductName ?? string.Empty) + "|" +
                           (info.FileDescription ?? string.Empty) + "|" +
                           (info.OriginalFilename ?? string.Empty);
            if (haystack.IndexOf("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        catch
        {
            // Unreadable metadata; fall through to the assembly check.
        }

        try
        {
            var name = AssemblyName.GetAssemblyName(path).Name;
            return string.Equals(name, "Plain Craft Launcher 2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Plain Craft Launcher", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Snapshots every running process and returns those whose image is PCL.</summary>
    public static List<PclCandidate> RunningProcesses(string minimumVersion = null)
    {
        var result = new List<PclCandidate>();
        var self = SelfPath();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return result;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                var path = ImagePath(process);
                if (string.IsNullOrEmpty(path)) continue;
                if (self is not null && string.Equals(path, self, StringComparison.OrdinalIgnoreCase)) continue;
                if (IOPath.GetFileName(path).IndexOf("PclPatcher", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!File.Exists(path) || !LooksLikePcl(path)) continue;

                var candidate = Inspect(path, "运行中的 PCL", minimumVersion);
                if (candidate is null) continue;
                candidate.IsRunning = true;
                candidate.Score += 10;
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>Reads the metadata and score for a known PCL path.</summary>
    public static PclCandidate Inspect(string path, string source, string minimumVersion)
    {
        try
        {
            var full = IOPath.GetFullPath(path);
            var info = new FileInfo(full);
            if (!info.Exists) return null;

            var version = string.Empty;
            try
            {
                version = (FileVersionInfo.GetVersionInfo(full).FileVersion ?? string.Empty).Trim();
            }
            catch
            {
                // Version resources are optional for the ranking.
            }

            var directory = info.DirectoryName ?? string.Empty;
            var candidate = new PclCandidate
            {
                Path = full,
                Version = version.Length == 0 ? "未知版本" : version,
                Size = info.Length,
                Modified = info.LastWriteTime,
                Source = source,
                HasBackup = File.Exists(full + ".bak"),
                HasConfig = Directory.Exists(IOPath.Combine(directory, "PCL")) ||
                            File.Exists(IOPath.Combine(directory, "PCL", "Setup.ini"))
            };
            candidate.Score = Score(candidate, minimumVersion);
            return candidate;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Scoring
    // ------------------------------------------------------------------

    private static int Score(PclCandidate candidate, string minimumVersion)
    {
        var score = candidate.Source switch
        {
            "运行中的 PCL" => 55,
            "记住的路径" => 30,
            "快捷方式" => 28,
            "注册表" => 22,
            "最近使用" => 16,
            "启动路径" => 12,
            "手动选择" => 0,
            _ => 10
        };

        if (candidate.HasConfig) score += 40;

        var directory = IOPath.GetDirectoryName(candidate.Path) ?? string.Empty;
        if (Directory.Exists(IOPath.Combine(directory, "versions")) ||
            Directory.Exists(IOPath.Combine(directory, ".minecraft"))) score += 15;

        if (VersionAtLeast(candidate.Version, minimumVersion)) score += 20;
        if ((DateTime.Now - candidate.Modified).TotalDays <= 120) score += 10;
        if (candidate.HasBackup) score += 8;
        if (IsCloudOrNetwork(candidate.Path)) score -= 12;

        return score;
    }

    private static bool VersionAtLeast(string version, string minimum)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(minimum)) return false;
        if (!Version.TryParse(version, out var actual)) return false;
        if (!Version.TryParse(minimum, out var required)) return false;
        return actual >= required;
    }

    private static bool IsCloudOrNetwork(string path)
    {
        if (path.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        try
        {
            var root = IOPath.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }

    private static int CompareCandidates(PclCandidate left, PclCandidate right)
    {
        var byScore = right.Score.CompareTo(left.Score);
        return byScore != 0 ? byScore : right.Modified.CompareTo(left.Modified);
    }

    // ------------------------------------------------------------------
    // Candidate plumbing
    // ------------------------------------------------------------------

    private static void Consider(
        Dictionary<string, PclCandidate> found,
        string path,
        string source,
        string minimumVersion,
        IProgress<PclScanUpdate> progress)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string full;
        try
        {
            full = IOPath.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return;
        }

        if (!File.Exists(full) || !LooksLikePcl(full)) return;

        var candidate = Inspect(full, source, minimumVersion);
        if (candidate is null) return;

        if (found.ContainsKey(full))
        {
            Merge(found, candidate);
            return;
        }

        found[full] = candidate;
        Report(progress, null, found.Count, candidate);
    }

    private static void Merge(Dictionary<string, PclCandidate> found, PclCandidate candidate)
    {
        if (!found.TryGetValue(candidate.Path, out var existing))
        {
            found[candidate.Path] = candidate;
            return;
        }

        existing.IsRunning |= candidate.IsRunning;
        if (candidate.Score <= existing.Score) return;
        existing.Score = candidate.Score;
        existing.Source = candidate.Source;
    }

    private static void Report(IProgress<PclScanUpdate> progress, string status, int count, PclCandidate candidate = null)
    {
        if (progress is null) return;
        if (status is null && candidate is null) return;
        progress.Report(new PclScanUpdate { Status = status, Count = count, Candidate = candidate });
    }

    // ------------------------------------------------------------------
    // Running processes
    // ------------------------------------------------------------------

    private static string _selfPath;

    private static string SelfPath()
    {
        if (_selfPath is not null) return _selfPath.Length == 0 ? null : _selfPath;
        try
        {
            _selfPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            _selfPath = string.Empty;
        }
        return _selfPath.Length == 0 ? null : _selfPath;
    }

    private static string ImagePath(Process process)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle != IntPtr.Zero)
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                if (QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0)
                    return buffer.ToString(0, size);
            }
        }
        catch
        {
            // The process may have exited between enumeration and the query.
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            // Access denied (elevated process) or process already gone.
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    // ------------------------------------------------------------------
    // Registry
    // ------------------------------------------------------------------

    private static IEnumerable<string> RegistryEntries()
    {
        foreach (var path in AppPathEntries()) yield return path;
        foreach (var path in UninstallEntries()) yield return path;
    }

    private static IEnumerable<string> AppPathEntries()
    {
        var roots = new List<KeyValuePair<RegistryKey, string>>
        {
            new(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            new(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            new(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths")
        };

        foreach (var root in roots)
        {
            using var key = root.Key.OpenSubKey(root.Value);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                if (name.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("pcl", StringComparison.OrdinalIgnoreCase) < 0) continue;
                using var app = key.OpenSubKey(name);
                var value = app?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(value)) yield return value.Trim().Trim('"');
            }
        }
    }

    private static IEnumerable<string> UninstallEntries()
    {
        var roots = new List<KeyValuePair<RegistryKey, string>>
        {
            new(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            new(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            new(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var root in roots)
        {
            using var key = root.Key.OpenSubKey(root.Value);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var item = key.OpenSubKey(name);
                if (item is null) continue;

                var display = item.GetValue("DisplayName") as string;
                if (string.IsNullOrEmpty(display)) continue;
                if (display.IndexOf("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !string.Equals(display.Trim(), "PCL", StringComparison.OrdinalIgnoreCase)) continue;

                var location = item.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(location))
                    yield return IOPath.Combine(location.Trim().Trim('"'), "Plain Craft Launcher 2.exe");

                var icon = CleanIconPath(item.GetValue("DisplayIcon") as string);
                if (icon is not null) yield return icon;
            }
        }
    }

    private static string CleanIconPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 2) path = path.Substring(0, comma);
        path = path.Trim().Trim('"');
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static IEnumerable<string> RecentUsageEntries()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(
                   @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched"))
        {
            if (key is not null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsInterestingStem(IOPath.GetFileNameWithoutExtension(name))) continue;
                    yield return name;
                }
            }
        }

        using (var key = Registry.CurrentUser.OpenSubKey(
                   @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache"))
        {
            if (key is not null)
            {
                foreach (var name in key.GetValueNames())
                {
                    var path = ExePathFromMuiName(name);
                    if (path is not null) yield return path;
                }
            }
        }
    }

    private static string ExePathFromMuiName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var path = name;
        var marker = path.IndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) path = path.Substring(0, marker + 4);
        else if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;

        if (!IsInterestingStem(IOPath.GetFileNameWithoutExtension(path))) return null;
        return File.Exists(path) ? path : null;
    }

    // ------------------------------------------------------------------
    // Shortcuts
    // ------------------------------------------------------------------

    private static IEnumerable<string> ShortcutEntries()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var folder in folders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!IsInterestingStem(IOPath.GetFileNameWithoutExtension(file))) continue;
                var target = ShellLink.ResolveTarget(file);
                if (target is not null) yield return target;
            }
        }
    }

    private static bool IsInterestingStem(string stem)
    {
        if (string.IsNullOrEmpty(stem)) return false;
        if (stem.IndexOf("patcher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return stem.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0
            || stem.IndexOf("pcl", StringComparison.OrdinalIgnoreCase) >= 0
            || stem.IndexOf("craft", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ------------------------------------------------------------------
    // Shallow disk walk
    // ------------------------------------------------------------------

    private static IEnumerable<KeyValuePair<string, int>> ShallowRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives())
        {
            bool eligible;
            try
            {
                eligible = drive.IsReady &&
                           drive.DriveType != DriveType.Network &&
                           drive.DriveType != DriveType.CDRom;
            }
            catch
            {
                eligible = false;
            }
            if (!eligible) continue;
            yield return Root(drive.RootDirectory.FullName, 2, seen);
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var name in new[] { "Desktop", "Downloads", "Documents" })
            yield return Root(IOPath.Combine(profile, name), 2, seen);

        yield return Root(IOPath.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Programs"), 2, seen);

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
            yield return Root(Environment.GetFolderPath(folder), 2, seen);
    }

    private static KeyValuePair<string, int> Root(string path, int depth, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(path)) return new KeyValuePair<string, int>(string.Empty, 0);
        if (!seen.Add(path)) return new KeyValuePair<string, int>(string.Empty, 0);
        return new KeyValuePair<string, int>(path, depth);
    }

    private static IEnumerable<string> Walk(string root, int maxDepth, Stopwatch clock, TimeSpan budget)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;

        var stack = new Stack<KeyValuePair<string, int>>();
        stack.Push(new KeyValuePair<string, int>(root, 0));

        while (stack.Count > 0)
        {
            if (clock.Elapsed > budget) yield break;

            var entry = stack.Pop();
            var directory = entry.Key;
            var depth = entry.Value;

            foreach (var file in SafeFiles(directory))
            {
                if (IsInterestingFile(file)) yield return file;
            }

            if (depth >= maxDepth) continue;
            foreach (var child in SafeDirectories(directory))
            {
                if (ShouldSkip(child)) continue;
                if (depth >= 1 && !IsInterestingStem(IOPath.GetFileName(child))) continue;
                stack.Push(new KeyValuePair<string, int>(child, depth + 1));
            }
        }
    }

    private static bool IsInterestingFile(string path)
    {
        var name = IOPath.GetFileName(path);
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return IsInterestingStem(IOPath.GetFileNameWithoutExtension(name));
    }

    private static bool ShouldSkip(string directory)
    {
        string name;
        try
        {
            name = IOPath.GetFileName(directory);
        }
        catch
        {
            return true;
        }

        if (string.IsNullOrEmpty(name)) return true;
        if (name.StartsWith("$", StringComparison.Ordinal)) return true;
        if (SkippedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;

        try
        {
            return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static string[] SafeFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] SafeDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Registry and shell enumeration can fail midway (protected keys, broken
    /// shortcuts). Swallow failures per step so one bad entry cannot abort the
    /// whole search, and never let it reach the startup path.
    /// </summary>
    private static IEnumerable<T> Safe<T>(IEnumerable<T> source)
    {
        IEnumerator<T> enumerator;
        try
        {
            enumerator = source.GetEnumerator();
        }
        catch
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext()) yield break;
                }
                catch
                {
                    yield break;
                }
                yield return enumerator.Current;
            }
        }
    }
}
