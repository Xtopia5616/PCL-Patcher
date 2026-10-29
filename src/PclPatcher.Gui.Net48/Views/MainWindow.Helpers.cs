namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private bool RestartAsAdministrator()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return false;

            var source = _sourceBox.Text?.Trim().Trim('"') ?? string.Empty;
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = IOPath.GetDirectoryName(exe),
                Arguments = string.IsNullOrEmpty(source) ? string.Empty : Quote(source)
            };
            Process.Start(psi);
            Close();
            return true;
        }
        catch (Exception ex)
        {
            PclDialog.Show(this, "无法以管理员身份重启",
                "已取消或启动失败：\n" + ex.Message + "\n\n请手动右键 exe → 以管理员身份运行。",
                "知道了", tone: DialogTone.Error);
            return false;
        }
    }

    private void UpdatePatcherInfo()
    {
        if (_patcherValue is null) return;
        _patcherPath = FindPatcher();
        if (_patcherPath is null)
        {
            _patcherValue.Text = "未找到";
            _patcherValue.Foreground = PclUi.Frozen(PclUi.Red);
            _patcherValue.ToolTip = "把 PclPatcher.exe 放在 GUI 同一目录。";
            AutomationProperties.SetHelpText(_patcherValue, "未找到");
            ApplyVersionCatalog(PclVersionCatalog.Empty);
            return;
        }
        var isEmbedded = _patcherMode != "disk";
        var modeText = _patcherMode == "dotnet"
            ? "dotnet 托管模式"
            : _patcherMode == "exe"
                ? "自包含 exe"
                : "外部文件";
        _patcherValue.Text = isEmbedded ? "已内置" : "已找到";
        _patcherValue.Foreground = PclUi.Frozen(PclUi.Green);
        _patcherValue.ToolTip = _patcherPath + Environment.NewLine + modeText;
        AutomationProperties.SetHelpText(_patcherValue, modeText);

        _ = RefreshVersionCatalogAsync();
    }

    /// <summary>
    /// Reads the supported PCL builds from the patcher, so the version row, the
    /// about badge and the mismatch dialog all render the same "X+" range and no
    /// PCL build is hardcoded in the GUI.
    /// </summary>
    private async Task RefreshVersionCatalogAsync()
    {
        if (_catalogLoading) return;
        var host = _patcherPath;
        if (string.IsNullOrEmpty(host))
        {
            ApplyVersionCatalog(PclVersionCatalog.Empty);
            return;
        }

        _catalogLoading = true;
        try
        {
            var args = new List<string>(_patcherPrefix) { "--profiles" };
            var output = await Task.Run(() => RunProfilesCommand(host, args)).ConfigureAwait(true);
            ApplyVersionCatalog(PclVersionCatalog.Parse(output));
        }
        catch
        {
            ApplyVersionCatalog(PclVersionCatalog.Empty);
        }
        finally
        {
            _catalogLoading = false;
        }
    }

    private static string RunProfilesCommand(string host, List<string> args)
    {
        var psi = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            Arguments = string.Join(" ", args.Select(Quote))
        };

        using var process = Process.Start(psi);
        if (process is null) return string.Empty;
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(8000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // The process may have exited between the check and the kill.
            }
        }
        return output;
    }

    private void ApplyVersionCatalog(PclVersionCatalog catalog)
    {
        _catalog = catalog;
        if (_versionBox is not null)
        {
            _versionBox.Items.Clear();
            _versionBox.Items.Add(catalog.SupportLabel);
            _versionBox.SelectedIndex = 0;
            _versionBox.ToolTip = catalog.DetailText;
            AutomationProperties.SetHelpText(_versionBox, catalog.DetailText);
        }
        if (_versionPillLabel is not null)
            _versionPillLabel.Text = catalog.SupportLabel;

        if (_lastHash is not null)
        {
            _lastKnown = catalog.Matches(_lastHash);
            _lastBackupKnown = catalog.Matches(_lastBackupHash);
            RefreshHashBadge();
            SetBackupHint(_restoreAvailable);
            RefreshLaunchReadiness();
        }
    }

    private string FindPatcher()
    {
        _patcherPrefix.Clear();
        _patcherMode = null;

        // Prefer the framework-dependent payload when the .NET 10 runtime is
        // available: dotnet.exe is a signed host and keeps working when security
        // software blocks unsigned executables from writing the PCL folder.
        var managed = App.EmbeddedManagedPatcherDirectory;
        var dotnet = FindDotnetHost();
        if (managed is not null && dotnet is not null && HasNet10Runtime(dotnet))
        {
            _patcherMode = "dotnet";
            _patcherPrefix.Add(IOPath.Combine(managed, "PclPatcher.dll"));
            return dotnet;
        }

        var embedded = App.EmbeddedPatcherPath;
        if (!string.IsNullOrEmpty(embedded) && File.Exists(embedded))
        {
            _patcherMode = "exe";
            return embedded;
        }

        var candidates = new List<string>
        {
            IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "PclPatcher.exe"),
            IOPath.Combine(Environment.CurrentDirectory, "dist", "PclPatcher.exe")
        };
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(IOPath.Combine(dir.FullName, "dist", "PclPatcher.exe"));
            candidates.Add(IOPath.Combine(dir.FullName, "src", "PclPatcher", "bin", "Release", "net10.0", "PclPatcher.exe"));
        }
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null) _patcherMode = "disk";
        return found;
    }

    private static string _dotnetHost;
    private static bool? _hasNet10Runtime;

    private static string FindDotnetHost()
    {
        if (_dotnetHost is not null) return _dotnetHost.Length == 0 ? null : _dotnetHost;

        var candidates = new List<string>();
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath)) candidates.Add(hostPath);

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            candidates.Add(IOPath.Combine(dotnetRoot, "dotnet.exe"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
            candidates.Add(IOPath.Combine(programFiles, "dotnet", "dotnet.exe"));
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
            candidates.Add(IOPath.Combine(programFilesX86, "dotnet", "dotnet.exe"));

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(IOPath.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            candidates.Add(IOPath.Combine(folder.Trim(), "dotnet.exe"));
        }

        _dotnetHost = candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        return _dotnetHost.Length == 0 ? null : _dotnetHost;
    }

    private static bool HasNet10Runtime(string dotnet)
    {
        if (_hasNet10Runtime.HasValue) return _hasNet10Runtime.Value;
        try
        {
            var shared = IOPath.Combine(IOPath.GetDirectoryName(dotnet) ?? string.Empty,
                "shared", "Microsoft.NETCore.App");
            _hasNet10Runtime = Directory.Exists(shared) &&
                               Directory.GetDirectories(shared, "10.*").Length > 0;
        }
        catch
        {
            _hasNet10Runtime = false;
        }
        return _hasNet10Runtime.Value;
    }

    private void UpdateResponsiveLayout()
    {
        if (_headerTabs != null)
            _headerTabs.Visibility = ActualWidth >= 900 ? Visibility.Visible : Visibility.Collapsed;
        if (_patchRoot is null || _stepsCard is null || _envCard is null) return;
        var compact = ActualWidth < 940;
        if (compact == _compact && _rightColumn.Width.Value > 0 == !compact) return;
        _compact = compact;

        if (compact)
        {
            _rightColumn.Width = new GridLength(0);
            _rightStack.Visibility = Visibility.Collapsed;
            Detach(_stepsCard);
            Detach(_envCard);
            _leftStack.Children.Add(_stepsCard);
            _leftStack.Children.Add(_envCard);
            _stepsCard.Margin = new Thickness(0, 4, 0, 10);
            _envCard.Margin = new Thickness(0, 0, 0, 0);
        }
        else
        {
            Detach(_stepsCard);
            Detach(_envCard);
            _rightStack.Children.Add(_stepsCard);
            _rightStack.Children.Add(_envCard);
            _stepsCard.Margin = new Thickness(0, 0, 0, 10);
            _envCard.Margin = new Thickness(0);
            _rightColumn.Width = new GridLength(272);
            _rightStack.Visibility = Visibility.Visible;
        }
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is ContentControl content) content.Content = null;
        else if (element.Parent is Decorator decorator) decorator.Child = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            if (!_busy) Start_Click();
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        switch (e.Key)
        {
            case Key.O:
                e.Handled = true;
                BrowseSource();
                break;
            case Key.D1:
            case Key.NumPad1:
                e.Handled = true;
                ShowPage(PageKind.Patch);
                break;
            case Key.D2:
            case Key.NumPad2:
                e.Handled = true;
                ShowPage(PageKind.Log);
                break;
            case Key.D3:
            case Key.NumPad3:
                e.Handled = true;
                ShowPage(PageKind.About);
                break;
            case Key.L:
                e.Handled = true;
                ShowPage(PageKind.Log);
                break;
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _detectCts?.Cancel();
        if (!_busy) return;
        var quit = PclDialog.Show(this,
            "补丁正在运行",
            "关闭窗口会中止正在运行的 patcher。\n确定要退出吗？",
            "退出", "继续等待", DialogTone.Warning);
        if (!quit)
        {
            e.Cancel = true;
            return;
        }
        try
        {
            _runningProcess?.Kill();
        }
        catch
        {
            // The process may already have exited.
        }
    }

    private static void AnimateScale(ScaleTransform scale, double value, int ms)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    private static string Quote(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
