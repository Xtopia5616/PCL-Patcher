namespace PclPatcher.Gui.Net48;

public partial class App : Application
{
    private static Assembly _pcl;
    private static string _pclSourcePath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            File.WriteAllText(IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                args.ExceptionObject?.ToString() ?? "unknown");
        DispatcherUnhandledException += (_, args) =>
        {
            File.WriteAllText(IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                args.Exception.ToString());
            args.Handled = true;
        };

        var pclPath = FindPcl(e.Args);
        if (pclPath is null)
        {
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        // The GUI only needs PCL's controls/theme. Loading the user's selected exe
        // directly would keep a file lock on it, which makes the default in-place
        // patch impossible; load a private copy and leave the source free.
        _pclSourcePath = pclPath;
        var loadPath = PreparePclLoadCopy(pclPath);
        // UnsafeLoadFrom keeps portable copies working when the launcher lives on a
        // OneDrive/network-mapped folder; the user explicitly picked this file.
        _pcl = Assembly.UnsafeLoadFrom(loadPath);

        // PCL 的界面字体 PCL English 内嵌在 PCL 本体的资源里。这里从用户本机的 PCL 中
        // 提取到本地缓存再加载，不随发布包分发字体文件。
        Resources["PclFontFamily"] = LoadPclFont(_pcl);

        // PCL 控件样式是 PCL 内部的 Application.xaml；单文件发布时以 EmbeddedResource
        // 打进本 exe，开发目录里仍允许放一份 loose XAML 方便调试。
        using (var stream = OpenTheme())
        {
            if (stream is null)
            {
                MessageBox.Show("缺少 PclTheme.xaml（内嵌资源）。请重新构建发布包。",
                    "无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }

        var window = new MainWindow(pclPath);
        MainWindow = window;
        window.Show();
    }

    private static Stream OpenTheme()
    {
        var embedded = typeof(App).Assembly.GetManifestResourceStream("PclPatcher.Gui.Net48.PclTheme.xaml");
        if (embedded is not null) return embedded;

        var themePath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "PclTheme.xaml");
        return File.Exists(themePath) ? File.OpenRead(themePath) : null;
    }

    private static string PreparePclLoadCopy(string source)
    {
        try
        {
            var hash = Sha256File(source);
            var size = new FileInfo(source).Length;
            var roots = new[]
            {
                IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PclPatcher", "ui"),
                IOPath.Combine(IOPath.GetTempPath(), "PclPatcher", "ui"),
                IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, ".pclpatcher", "ui")
            };

            foreach (var root in roots)
            {
                var directory = IOPath.Combine(root, "ui-" + hash.Substring(0, 16));
                var target = IOPath.Combine(directory, IOPath.GetFileName(source));
                string temp = null;
                try
                {
                    if (File.Exists(target) && new FileInfo(target).Length == size &&
                        string.Equals(Sha256File(target), hash, StringComparison.OrdinalIgnoreCase))
                        return target;

                    Directory.CreateDirectory(directory);
                    temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
                    File.Copy(source, temp, true);
                    if (!string.Equals(Sha256File(temp), hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PCL UI copy hash mismatch");

                    if (File.Exists(target)) File.Delete(target);
                    File.Move(temp, target);
                    temp = null;
                    CleanupOldUi(root, directory);
                    return target;
                }
                catch (Exception ex)
                {
                    LogExtract("ui copy to " + root + " failed: " + ex.Message);
                }
                finally
                {
                    if (temp is not null)
                    {
                        try { File.Delete(temp); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogExtract("ui copy failed: " + ex.Message);
        }

        // Last resort: load the original. The GUI then locks it, so in-place
        // patching needs the user to patch a copy or close the GUI.
        return source;
    }

    private static void CleanupOldUi(string root, string keep)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(root, "ui-*"))
            {
                if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch
        {
            // A locked older copy is harmless.
        }
    }

    private static string FindPcl(string[] args)
    {
        var settings = AppSettings.Load();
        var seeds = new List<string>
        {
            args.FirstOrDefault(),
            IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe"),
            IOPath.Combine(Environment.CurrentDirectory, "Plain Craft Launcher 2.exe"),
            @"D:\Minecraft\Plain Craft Launcher 2.exe"
        };
        seeds.AddRange(settings.RecentPclPaths);

        // Startup stays cheap: only the registered locations and remembered
        // paths are checked. The deeper shallow walk belongs to the 自动检测
        // button, which can afford to run for a couple of seconds.
        var found = PclLocator.QuickScan(new PclScanOptions { Seeds = seeds, IncludeShallow = false });
        if (found.Count > 0) return Remember(settings, found[0].Path);

        // PCL may already be open; then its image path is exact.
        var running = PclLocator.RunningProcesses();
        if (running.Count > 0) return Remember(settings, running[0].Path);

        while (true)
        {
            var answer = MessageBox.Show(
                "没有在本机找到 Plain Craft Launcher。\n\n" +
                "请先启动一次 PCL（它会自动被识别），然后点「是」重新检测。\n" +
                "也可以点「否」手动选择 PCL 文件。",
                "PCL Patcher", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) break;

            var detected = PclLocator.QuickScan(new PclScanOptions { Seeds = seeds });
            if (detected.Count > 0) return Remember(settings, detected[0].Path);

            var nowRunning = PclLocator.RunningProcesses();
            if (nowRunning.Count > 0) return Remember(settings, nowRunning[0].Path);
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择 PCL 主程序",
            Filter = "PCL 主程序|Plain Craft Launcher 2.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return null;
        return Remember(settings, dialog.FileName);
    }

    private static string Remember(AppSettings settings, string path)
    {
        settings.Remember(path);
        return path;
    }

    private static FontFamily LoadPclFont(Assembly pcl)
    {
        var fallback = new FontFamily("Microsoft YaHei UI");
        try
        {
            var folder = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PclPatcher", "Fonts");
            Directory.CreateDirectory(folder);
            var file = IOPath.Combine(folder, "PCL English.ttf");

            if (!File.Exists(file) || new FileInfo(file).Length == 0)
            {
                using var resources = pcl.GetManifestResourceStream("Plain Craft Launcher 2.g.resources");
                if (resources is null) return fallback;
                using var reader = new ResourceReader(resources);
                foreach (DictionaryEntry entry in reader)
                {
                    if ((string)entry.Key != "resources/font.ttf" || entry.Value is not Stream font) continue;
                    using var output = File.Create(file);
                    font.CopyTo(output);
                    break;
                }
            }

            if (!File.Exists(file)) return fallback;

            var uri = new Uri(folder + IOPath.DirectorySeparatorChar, UriKind.Absolute);
            var families = Fonts.GetFontFamilies(uri);
            foreach (var family in families)
            {
                if (family.FamilyNames.Values.Any(name =>
                        string.Equals(name, "PCL English", StringComparison.OrdinalIgnoreCase)))
                    return family;
            }

            return new FontFamily(new Uri(folder + IOPath.DirectorySeparatorChar, UriKind.Absolute), "./#PCL English");
        }
        catch
        {
            return fallback;
        }
    }

    private static string _embeddedPatcherPath;
    private static string _managedPatcherDir;

    /// <summary>
    /// The single-file build embeds the published PclPatcher.exe as a manifest
    /// resource. It is extracted to LocalAppData once (TEMP and the exe folder are
    /// fallbacks), under a content-hash name, so upgrades never collide with a
    /// running copy.
    /// </summary>
    public static string EmbeddedPatcherPath =>
        _embeddedPatcherPath ??= ExtractEmbeddedPatcher();

    /// <summary>
    /// Directory containing the framework-dependent PclPatcher.dll payload.
    /// Running that DLL through the signed dotnet.exe host works on machines whose
    /// security software blocks unsigned exes from writing outside their profile.
    /// </summary>
    public static string EmbeddedManagedPatcherDirectory =>
        _managedPatcherDir ??= ExtractManagedPatcher();

    private static readonly string[] ManagedResources =
    {
        "PclPatcher.Payload.PclPatcher.dll",
        "PclPatcher.Payload.PclPatcher.deps.json",
        "PclPatcher.Payload.PclPatcher.runtimeconfig.json",
        "PclPatcher.Payload.dnlib.dll"
    };

    private static readonly string[] ManagedFiles =
    {
        "PclPatcher.dll",
        "PclPatcher.deps.json",
        "PclPatcher.runtimeconfig.json",
        "dnlib.dll"
    };

    private static string ExtractManagedPatcher()
    {
        var assembly = typeof(App).Assembly;
        using var dll = assembly.GetManifestResourceStream(ManagedResources[0]);
        if (dll is null) return null;
        var hash = Sha256(dll);

        var roots = new[]
        {
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PclPatcher", "managed"),
            IOPath.Combine(IOPath.GetTempPath(), "PclPatcher", "managed"),
            IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, ".pclpatcher", "managed")
        };

        foreach (var root in roots)
        {
            var target = IOPath.Combine(root, "managed-" + hash.Substring(0, 16));
            var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var existing = IOPath.Combine(target, "PclPatcher.dll");
                if (Directory.Exists(target) && File.Exists(existing) &&
                    string.Equals(Sha256File(existing), hash, StringComparison.OrdinalIgnoreCase))
                    return target;

                Directory.CreateDirectory(temp);
                for (var i = 0; i < ManagedResources.Length; i++)
                {
                    using var input = assembly.GetManifestResourceStream(ManagedResources[i]);
                    if (input is null) throw new InvalidDataException("missing resource " + ManagedResources[i]);
                    using var output = File.Create(IOPath.Combine(temp, ManagedFiles[i]));
                    input.CopyTo(output);
                }

                if (!string.Equals(Sha256File(IOPath.Combine(temp, "PclPatcher.dll")), hash,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("managed patcher hash mismatch");

                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(temp, target);
                CleanupOldManaged(root, target);
                return target;
            }
            catch (Exception ex)
            {
                LogExtract("managed write to " + root + " failed: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temp)) Directory.Delete(temp, true);
                }
                catch
                {
                    // Best effort.
                }
            }
        }

        return null;
    }

    private static string ExtractEmbeddedPatcher()
    {
        try
        {
            using var payload = typeof(App).Assembly
                .GetManifestResourceStream("PclPatcher.Payload.PclPatcher.exe");
            if (payload is null)
            {
                LogExtract("payload resource missing");
                return null;
            }

            var hash = Sha256(payload);
            var local = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PclPatcher", "bin");
            var extracted = WritePayload(payload, hash, local);
            if (extracted is not null) return extracted;

            LogExtract("LocalAppData extraction failed, falling back to TEMP");
            extracted = WritePayload(payload, hash, IOPath.Combine(IOPath.GetTempPath(), "PclPatcher", "bin"));
            if (extracted is not null) return extracted;

            // Some locked-down machines deny both profile and temp writes; a
            // portable copy next to the exe is the last workable location.
            LogExtract("TEMP extraction failed, falling back to the exe directory");
            return WritePayload(payload, hash,
                IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, ".pclpatcher", "bin"));
        }
        catch (Exception ex)
        {
            LogExtract("extraction threw: " + ex);
            return null;
        }
    }

    private static void CleanupOldManaged(string root, string keep)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(root, "managed-*"))
            {
                if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch
        {
            // A locked older payload is harmless.
        }
    }

    private static void LogExtract(string message)
    {
        var line = DateTime.Now.ToString("s") + " " + message + Environment.NewLine;
        foreach (var path in new[]
                 {
                     IOPath.Combine(IOPath.GetTempPath(), "PclPatcher-extract.log"),
                     IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "PclPatcher-extract.log")
                 })
        {
            try
            {
                File.AppendAllText(path, line);
                return;
            }
            catch
            {
                // Try the next writable location.
            }
        }
    }

    private static string WritePayload(Stream payload, string hash, string directory)
    {
        string temp = null;
        try
        {
            Directory.CreateDirectory(directory);
            var target = IOPath.Combine(directory, "PclPatcher-" + hash.Substring(0, 16) + ".exe");
            if (File.Exists(target) &&
                new FileInfo(target).Length == payload.Length &&
                string.Equals(Sha256File(target), hash, StringComparison.OrdinalIgnoreCase))
                return target;

            temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            payload.Position = 0;
            using (var output = File.Create(temp)) payload.CopyTo(output);

            if (!string.Equals(Sha256File(temp), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("embedded patcher hash mismatch");

            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
            temp = null;
            CleanupOldPayloads(directory, target);
            return target;
        }
        catch (Exception ex)
        {
            LogExtract("write to " + directory + " failed: " + ex.Message);
            return null;
        }
        finally
        {
            if (temp is not null)
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static void CleanupOldPayloads(string directory, string keep)
    {
        try
        {
            foreach (var file in Directory.GetFiles(directory, "PclPatcher-*.exe"))
            {
                if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // Cleanup is best effort; a locked old payload is harmless.
        }
    }

    private static string Sha256(Stream stream)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(stream));
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256(stream);
    }

    private static string ToHex(byte[] bytes) =>
        BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name;
        if (name == "Plain Craft Launcher 2") return _pcl;

        var sourceDirectory = IOPath.GetDirectoryName(_pclSourcePath);
        if (string.IsNullOrEmpty(sourceDirectory)) sourceDirectory = IOPath.GetDirectoryName(_pcl.Location);
        var file = IOPath.Combine(sourceDirectory ?? string.Empty, name + ".dll");
        if (File.Exists(file)) return Assembly.UnsafeLoadFrom(file);

        using var stream = _pcl.GetManifestResourceStream("PCL.Resources.resources");
        if (stream is null) return null;
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            if ((string)entry.Key == name && entry.Value is byte[] bytes)
                return Assembly.Load(bytes);
        }
        return null;
    }
}


