namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Drag and drop
    // ------------------------------------------------------------------

    private Grid BuildDropOverlay()
    {
        var overlay = new Grid
        {
            Background = PclUi.Brush(0xF0, 0xF7, 0xFF, 0xF2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var frame = new Border
        {
            Margin = new Thickness(18),
            BorderBrush = PclUi.Frozen(PclUi.Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Background = PclUi.Brush(0xFF, 0xFF, 0xFF, 0xCC)
        };
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(PclUi.Icon("M8,2 L13.5,4.5 L13.5,9 C13.5,12 8,14.5 8,14.5 C8,14.5 2.5,12 2.5,9 L2.5,4.5 Z", PclUi.Accent, 48, 1.6));
        var title = PclUi.Label("松开以使用这个文件", 16, PclUi.Ink, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(title);
        _dropSub = PclUi.Label("只接受 .exe 文件", 11.5, PclUi.Muted);
        _dropSub.HorizontalAlignment = HorizontalAlignment.Center;
        _dropSub.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(_dropSub);
        frame.Child = panel;
        overlay.Children.Add(frame);
        return overlay;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        var files = GetDropFiles(e);
        if (files.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            HideDropOverlay();
            return;
        }
        e.Effects = DragDropEffects.Copy;
        _dropSub.Text = files.Length == 1 ? IOPath.GetFileName(files[0]) : files.Length + " 个文件";
        _dropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        var files = GetDropFiles(e);
        HideDropOverlay();
        if (files.Length == 0) return;
        e.Handled = true;
        SetSource(files[0]);
    }

    private static string[] GetDropFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
        if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return Array.Empty<string>();
        return files
            .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
            .ToArray();
    }

    private void HideDropOverlay()
    {
        if (_dropOverlay != null) _dropOverlay.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------------
    // Source / output
    // ------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        UpdatePatcherInfo();
        SetSource(_pclPath);
        UpdateResponsiveLayout();
    }

    private void SetSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim().Trim('"');
        try
        {
            if (!IOPath.IsPathRooted(path)) path = IOPath.GetFullPath(path);
        }
        catch
        {
            // Keep the user's text; the validation below will mark it invalid.
        }

        _suppressText = true;
        _sourceBox.Text = path;
        _suppressText = false;
        _sourceBox.ToolTip = path;
        ShowPathTail(_sourceBox);

        var output = path;
        if (string.IsNullOrWhiteSpace(_outputBox.Text) || _outputBox.Text == _lastAutoOutput)
        {
            _outputBox.Text = output;
            _outputBox.ToolTip = output;
            _lastAutoOutput = output;
            ShowPathTail(_outputBox);
        }

        _ = UpdateSourceInfoAsync(path);
    }

    private void AutoDetectSource() => SetSource(_pclPath);

    private void SourceBox_TextChanged()
    {
        if (_suppressText || _busy) return;
        var path = _sourceBox.Text?.Trim().Trim('"') ?? string.Empty;
        _sourceBox.ToolTip = path.Length == 0 ? null : path;
        ShowPathTail(_sourceBox);
        if (path.Length == 0)
        {
            _lastHash = null;
            _lastBackupHash = null;
            _lastBackupKnown = false;
            _lastBackupPath = null;
            _restoreAvailable = false;
            SetBackupHint(false);
            RefreshBackupState();
            SetSourceStrip("未选择 PCL 主程序", "待选择", PclUi.Muted,
                Color.FromRgb(0xF3, 0xF6, 0xFA), Color.FromRgb(0xEC, 0xEF, 0xF4), PclUi.Muted);
            return;
        }

        if (!File.Exists(path))
        {
            _lastHash = null;
            _lastBackupKnown = false;
            _lastBackupPath = null;
            _restoreAvailable = false;
            SetBackupHint(false);
            RefreshBackupState();
            SetSourceStrip("文件不存在，请重新选择", "文件不存在", PclUi.Red,
                Color.FromRgb(0xFD, 0xEC, 0xEC), Color.FromRgb(0xFA, 0xD7, 0xD7), PclUi.Red);
            return;
        }

        var output = path;
        if (string.IsNullOrWhiteSpace(_outputBox.Text) || _outputBox.Text == _lastAutoOutput)
        {
            _outputBox.Text = output;
            _outputBox.ToolTip = output;
            _lastAutoOutput = output;
            ShowPathTail(_outputBox);
        }
        _ = UpdateSourceInfoAsync(path);
    }

    private static string BackupPath(string source) => source + ".bak";

    private async Task UpdateSourceInfoAsync(string path)
    {
        var generation = ++_infoGeneration;
        SetSourceStrip("正在校验文件…", "校验中", PclUi.Accent,
            Color.FromRgb(0xEA, 0xF2, 0xFE), Color.FromRgb(0xE8, 0xF1, 0xFF), PclUi.Accent);

        try
        {
            if (!File.Exists(path))
            {
                ResetBackupState();
                SetSourceStrip("文件不存在，请重新选择", "文件不存在", PclUi.Red,
                    Color.FromRgb(0xFD, 0xEC, 0xEC), Color.FromRgb(0xFA, 0xD7, 0xD7), PclUi.Red);
                return;
            }

            var version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "未知版本";
            var size = new FileInfo(path).Length / 1024d / 1024d;

            await Task.Delay(120).ConfigureAwait(true);
            if (generation != _infoGeneration) return;

            var hash = await Task.Run(() => Sha256(path)).ConfigureAwait(true);
            if (generation != _infoGeneration) return;

            var backup = BackupPath(path);
            string backupHash = null;
            if (File.Exists(backup))
            {
                try
                {
                    backupHash = await Task.Run(() => Sha256(backup)).ConfigureAwait(true);
                }
                catch
                {
                    backupHash = null;
                }
                if (generation != _infoGeneration) return;
            }

            _lastHash = hash;
            _lastHashPath = path;
            _lastVersion = version;
            _lastSize = size;
            _lastKnown = _catalog.Matches(hash);
            _lastBackupPath = backup;
            _lastBackupHash = backupHash;
            _restoreAvailable = File.Exists(backup);
            _lastBackupKnown = _catalog.Matches(backupHash);

            var shortHash = hash.Length >= 16 ? hash.Substring(0, 16) : hash;
            _sourceMeta.Text = $"{version} · {size:F1} MB · SHA-256 {shortHash}…";
            _sourceMeta.ToolTip = path + Environment.NewLine + "SHA-256: " + hash;
            if (_restoreAvailable)
                _sourceMeta.ToolTip += Environment.NewLine + "备份: " + backup;
            RefreshHashBadge();
            SetBackupHint(_restoreAvailable);
            RefreshBackupState();
        }
        catch (Exception ex)
        {
            if (generation != _infoGeneration) return;
            _lastHash = null;
            _lastKnown = false;
            ResetBackupState();
            SetSourceStrip("读取失败：" + ex.Message, "读取失败", PclUi.Red,
                Color.FromRgb(0xFD, 0xEC, 0xEC), Color.FromRgb(0xFA, 0xD7, 0xD7), PclUi.Red);
        }
    }

    private void RefreshHashBadge()
    {
        if (_lastHash is null) return;
        var meta = $"{_lastVersion} · {_lastSize:F1} MB · SHA-256 {_lastHash.Substring(0, Math.Min(16, _lastHash.Length))}…";
        if (!_catalog.IsLoaded)
        {
            // The patcher is the authority on supported builds; until its profile
            // list arrives we cannot claim a match or a mismatch.
            SetSourceStrip(
                meta + " · 待校验",
                "待校验", PclUi.Muted,
                Color.FromRgb(0xF3, 0xF6, 0xFA), Color.FromRgb(0xEC, 0xEF, 0xF4), PclUi.Muted);
            return;
        }

        if (_lastKnown)
        {
            SetSourceStrip(
                meta,
                "已匹配", PclUi.Green,
                Color.FromRgb(0xE9, 0xF7, 0xEF), Color.FromRgb(0xDD, 0xF3, 0xE6), PclUi.Green);
            return;
        }

        if (_lastBackupKnown)
        {
            SetSourceStrip(
                meta + " · 已打补丁",
                "已打补丁", PclUi.Accent,
                Color.FromRgb(0xEA, 0xF2, 0xFE), Color.FromRgb(0xE8, 0xF1, 0xFF), PclUi.Accent);
            return;
        }

        var allowed = _forceBox?.Checked == true;
        SetSourceStrip(
            meta,
            allowed ? "已允许" : "哈希不符", PclUi.Amber,
            Color.FromRgb(0xFF, 0xF6, 0xE6),
            allowed ? Color.FromRgb(0xFF, 0xE9, 0xC7) : Color.FromRgb(0xFB, 0xE3, 0xC3),
            PclUi.Amber);
    }

    private void SetBackupHint(bool backupExists)
    {
        if (_backupHint is null) return;
        if (!backupExists)
        {
            _backupHint.Text = "默认直接替换源文件；替换前会自动备份为 .bak，可随时还原。";
            _backupHint.Foreground = PclUi.Frozen(PclUi.Muted);
            return;
        }

        var name = string.IsNullOrEmpty(_lastBackupPath) ? "源文件.bak" : IOPath.GetFileName(_lastBackupPath);
        if (_lastBackupKnown && !_lastKnown)
        {
            _backupHint.Text = "源文件已是补丁状态；原版已备份为 " + name + "，可随时还原。";
            _backupHint.Foreground = PclUi.Frozen(PclUi.Accent);
        }
        else if (_lastKnown)
        {
            _backupHint.Text = "已存在原版备份 " + name + "；补丁时会复用它，不会覆盖。";
            _backupHint.Foreground = PclUi.Frozen(PclUi.Green);
        }
        else
        {
            _backupHint.Text = "已存在备份 " + name + "，可尝试还原。";
            _backupHint.Foreground = PclUi.Frozen(PclUi.Amber);
        }
    }

    private void ResetBackupState()
    {
        _lastBackupHash = null;
        _lastBackupKnown = false;
        _lastBackupPath = null;
        _restoreAvailable = false;
        SetBackupHint(false);
        RefreshBackupState();
    }

    private void RefreshBackupState()
    {
        if (_restoreButton is null) return;
        var enabled = _restoreAvailable && !_busy;
        _restoreButton.Opacity = enabled ? 1.0 : 0.45;
        _restoreButton.IsHitTestVisible = enabled;
    }

    private void SetSourceStrip(string meta, string badge, Color foreground, Color background, Color badgeBackground, Color badgeForeground)
    {
        if (_sourceStrip is null) return;
        _sourceMeta.Text = meta;
        _sourceBadgeLabel.Text = badge;
        _sourceStrip.Background = PclUi.Frozen(background);
        _sourceBadge.Background = PclUi.Frozen(badgeBackground);
        _sourceBadgeLabel.Foreground = PclUi.Frozen(badgeForeground);
        _sourceIcon.Stroke = PclUi.Frozen(foreground);
    }

    private void BrowseSource()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PCL 主程序",
            Filter = "PCL 主程序|Plain Craft Launcher 2.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) SetSource(dialog.FileName);
    }

    private void BrowseOutput()
    {
        var sourceDir = IOPath.GetDirectoryName(_sourceBox.Text ?? string.Empty);
        var dialog = new SaveFileDialog
        {
            Title = "选择输出文件",
            Filter = "可执行文件|*.exe",
            FileName = "PCL-patched.exe",
            InitialDirectory = !string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir)
                ? sourceDir
                : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) != true) return;
        _outputBox.Text = dialog.FileName;
        _outputBox.ToolTip = dialog.FileName;
        ShowPathTail(_outputBox);
        _lastAutoOutput = null;
    }

    private static void ShowPathTail(PCL.MyTextBox box)
    {
        if (box is null) return;
        box.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (box.IsKeyboardFocusWithin) return;
            box.CaretIndex = box.Text?.Length ?? 0;
            box.ScrollToEnd();
        }), DispatcherPriority.Background);
    }
}
