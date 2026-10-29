namespace PclPatcher.Gui.Net48;

/// <summary>
/// Modal "find PCL" surface. It shows what the quick scan already found and
/// keeps polling running processes, so the user can simply start PCL while the
/// dialog is open and have the patcher reverse the path from the process.
/// </summary>
internal sealed class PclFinderWindow : Window
{
    private readonly string _minimumVersion;
    private readonly StackPanel _rowPanel = new StackPanel();
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private readonly PCL.MyButton _useButton;
    private readonly Dictionary<string, Border> _rowByPath = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _dotByPath = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly DateTime _openedAt = DateTime.UtcNow;

    private Border _selectedRow;
    private string _selectedPath;
    private bool _closed;

    public PclFinderWindow(IReadOnlyList<PclCandidate> candidates, string minimumVersion)
    {
        _minimumVersion = minimumVersion ?? string.Empty;

        Title = "查找 PCL";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(440, SystemParameters.WorkArea.Height - 60);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = PclUi.Font;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        PclUi.EnableRoundedCorners(this);

        _status = PclUi.Label(string.Empty, 12.5, PclUi.Ink, null, true);
        _hint = PclUi.Label(
            "没看到你的 PCL？先启动一次 PCL，这个窗口开着就会自动识别。\n" +
            "PCL 以管理员身份运行时进程路径读不到，请用「浏览…」直接选文件。",
            11.5, PclUi.Muted, null, true);
        _hint.Margin = new Thickness(0, 10, 0, 0);
        _useButton = PclUi.Button("使用这个", Confirm, 34, true);
        _useButton.IsEnabled = false;
        _useButton.Opacity = 0.5;

        Content = BuildShell();

        foreach (var candidate in candidates ?? Array.Empty<PclCandidate>())
            AddCandidate(candidate, null);

        if (_rowByPath.Count > 0)
            _status.Text = "快速扫描找到 " + _rowByPath.Count + " 个候选。也可以启动 PCL，让它出现在运行中的进程里。";
        else
            _status.Text = "正在快速查找… 也可以先启动 PCL，我会从运行中的进程反查它的位置。";

        Loaded += (_, _) =>
        {
            // Re-apply the selection once the PCL button template is live; a
            // candidate chosen in the constructor would otherwise keep the
            // primary button in its initial disabled look.
            if (_selectedPath is not null) SelectRow(_selectedPath);
            _ = PollAsync();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            try { _cts.Cancel(); } catch { }
            _cts.Dispose();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter && _selectedPath is not null)
            {
                e.Handled = true;
                Confirm();
            }
        };
    }

    public string SelectedPath => _selectedPath;

    private UIElement BuildShell()
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(0x0E, 0x66, 0xC2),
                Color.FromRgb(0x14, 0x78, 0xDC),
                0)
        };

        var barGrid = new Grid();
        var dragSurface = new Border { Background = Brushes.Transparent };
        dragSurface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        barGrid.Children.Add(dragSurface);
        barGrid.Children.Add(new TextBlock
        {
            Text = Title,
            Foreground = Brushes.White,
            FontFamily = PclUi.Font,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });

        var close = new Grid
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        close.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = PclUi.Glyph("\uE8BB", 9, Colors.White)
        });
        close.MouseLeftButtonUp += (_, _) => Close();
        barGrid.Children.Add(close);

        bar.Child = barGrid;
        shell.Children.Add(bar);

        var body = new StackPanel();
        body.Children.Add(_status);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 300,
            MinHeight = 64,
            Margin = new Thickness(0, 10, 0, 0),
            Content = _rowPanel
        };
        body.Children.Add(scroll);
        body.Children.Add(_hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var browse = PclUi.Button("浏览…", Browse_Click, 34);
        browse.MinWidth = 84;
        browse.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(browse);

        var cancel = PclUi.Button("取消", Close, 34);
        cancel.MinWidth = 76;
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);

        _useButton.MinWidth = 96;
        buttons.Children.Add(_useButton);
        body.Children.Add(buttons);

        var content = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(20, 16, 20, 16),
            Child = body
        };
        Grid.SetRow(content, 1);
        shell.Children.Add(content);
        return shell;
    }

    private void AddCandidate(PclCandidate candidate, string note)
    {
        if (candidate is null || string.IsNullOrEmpty(candidate.Path)) return;
        if (!_seen.Add(candidate.Path)) return;

        var row = BuildRow(candidate);
        _rowByPath[candidate.Path] = row;
        _rowPanel.Children.Add(row);

        if (_selectedPath is null) SelectRow(candidate.Path);
        if (note is not null) _status.Text = note;
        else _status.Text = "已找到 " + _rowByPath.Count + " 个候选。";
    }

    private Border BuildRow(PclCandidate candidate)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = candidate.Path
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1.5),
            BorderBrush = PclUi.Frozen(PclUi.Muted),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _dotByPath[candidate.Path] = dot;
        grid.Children.Add(dot);

        var text = new StackPanel();
        text.Children.Add(PclUi.Label(IOPath.GetFileName(candidate.Path), 13.5, PclUi.Ink, FontWeights.SemiBold));
        var directory = PclUi.Label(IOPath.GetDirectoryName(candidate.Path) ?? string.Empty, 11, PclUi.Muted);
        directory.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(directory);

        var meta = new StringBuilder();
        meta.Append(string.IsNullOrEmpty(candidate.Version) ? "未知版本" : candidate.Version);
        meta.Append(" · ").Append((candidate.Size / 1024d / 1024d).ToString("F1", CultureInfo.InvariantCulture)).Append(" MB");
        meta.Append(" · ").Append(candidate.Source);
        if (candidate.HasBackup) meta.Append(" · 已打过补丁");
        var metaBlock = PclUi.Label(meta.ToString(), 11, candidate.HasBackup ? PclUi.Accent : PclUi.Muted);
        metaBlock.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(metaBlock);

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        border.Child = grid;

        border.MouseLeftButtonUp += (_, e) =>
        {
            SelectRow(candidate.Path);
            if (e.ClickCount >= 2) Confirm();
        };
        return border;
    }

    private void SelectRow(string path)
    {
        if (path is null || !_rowByPath.TryGetValue(path, out var row)) return;

        if (_selectedRow is not null)
        {
            _selectedRow.Background = Brushes.Transparent;
            _selectedRow.BorderBrush = Brushes.Transparent;
        }

        _selectedRow = row;
        _selectedPath = path;
        row.Background = PclUi.Frozen(Color.FromRgb(0xEA, 0xF2, 0xFE));
        row.BorderBrush = PclUi.Frozen(PclUi.Accent);

        foreach (var pair in _dotByPath)
        {
            var active = string.Equals(pair.Key, path, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = active ? PclUi.AccentBrush : Brushes.Transparent;
            pair.Value.BorderBrush = active ? PclUi.AccentBrush : PclUi.Frozen(PclUi.Muted);
        }

        _useButton.IsEnabled = true;
        _useButton.Opacity = 1;
    }

    private void Confirm()
    {
        if (string.IsNullOrEmpty(_selectedPath)) return;
        Close();
    }

    private void Browse_Click()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PCL 主程序",
            Filter = "PCL 主程序|Plain Craft Launcher 2.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        if (!PclLocator.LooksLikePcl(dialog.FileName))
        {
            _status.Text = "所选文件看起来不是 PCL，请确认后再选。";
            return;
        }

        var candidate = PclLocator.Inspect(dialog.FileName, "手动选择", _minimumVersion);
        if (candidate is null)
        {
            _status.Text = "无法读取这个文件，请换一个。";
            return;
        }

        AddCandidate(candidate, "已选中手动选择的文件。");
        SelectRow(candidate.Path);
    }

    private async Task PollAsync()
    {
        var deadline = _openedAt.AddMinutes(10);
        while (!_closed && !_cts.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(900, _cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (_closed) return;

            List<PclCandidate> running;
            try
            {
                running = await Task.Run(() => PclLocator.RunningProcesses(_minimumVersion)).ConfigureAwait(true);
            }
            catch
            {
                continue;
            }
            if (_closed) return;

            foreach (var candidate in running)
                AddCandidate(candidate, "已从运行中的进程找到 PCL。");

            if (_rowByPath.Count == 0 && (DateTime.UtcNow - _openedAt).TotalSeconds > 6)
                _status.Text = "还没找到。请先启动一次 PCL，这个窗口开着就会自动识别。";
        }
    }
}
