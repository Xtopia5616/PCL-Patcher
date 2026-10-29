namespace PclPatcher.Gui.Net48;

internal enum LogState
{
    Idle,
    Running,
    Done,
    Error
}

/// <summary>
/// PCL-coloured log console built from TextBlocks instead of a RichTextBox so it
/// can use PCL's slim scrollbar and keep the same line rhythm as the rest of the UI.
/// </summary>
internal sealed class LogPanel : Grid
{
    private readonly List<string> _lines = new List<string>();
    private readonly StackPanel _items;
    private readonly ScrollViewer _scroll;
    private readonly Border _stateDot;
    private readonly TextBlock _stateText;
    private readonly TextBlock _countText;
    private readonly Border _empty;
    private readonly Border _autoToggle;
    private readonly Border _autoDot;
    private readonly TextBlock _autoText;
    private readonly DispatcherTimer _copyTimer;
    private bool _autoScroll = true;
    private LogState _state = LogState.Idle;

    public LogPanel()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 6) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _stateDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = PclUi.Frozen(PclUi.Muted),
            VerticalAlignment = VerticalAlignment.Center
        };
        statePanel.Children.Add(_stateDot);
        _stateText = PclUi.Label("等待运行", 12.5, PclUi.Muted, FontWeights.SemiBold);
        _stateText.Margin = new Thickness(7, 0, 0, 0);
        statePanel.Children.Add(_stateText);
        _countText = PclUi.Label("0 行", 11, Color.FromRgb(0xA6, 0xA6, 0xA6));
        _countText.Margin = new Thickness(12, 0, 0, 0);
        statePanel.Children.Add(_countText);
        toolbar.Children.Add(statePanel);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _autoToggle = new Border
        {
            Height = 26,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "新日志到达时自动滚动到底部"
        };
        var autoContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _autoDot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = PclUi.Frozen(PclUi.Accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        autoContent.Children.Add(_autoDot);
        _autoText = PclUi.Label("自动滚动", 11.5, PclUi.Accent);
        _autoText.Margin = new Thickness(5, 0, 0, 0);
        autoContent.Children.Add(_autoText);
        _autoToggle.Child = autoContent;
        PclUi.AttachTint(_autoToggle, Color.FromRgb(0xEA, 0xF2, 0xFE), Colors.Transparent);
        _autoToggle.MouseLeftButtonUp += (_, _) => SetAutoScroll(!_autoScroll);
        actions.Children.Add(_autoToggle);

        var copy = PclUi.Button("复制", CopyAll, 26);
        copy.Padding = new Thickness(10, 0, 10, 0);
        copy.Margin = new Thickness(0, 0, 4, 0);
        actions.Children.Add(copy);

        var clear = PclUi.Button("清空", Clear, 26);
        clear.Padding = new Thickness(10, 0, 10, 0);
        actions.Children.Add(clear);

        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(actions);
        Children.Add(toolbar);

        var console = new Border
        {
            Background = PclUi.Frozen(Color.FromRgb(0xFC, 0xFD, 0xFF)),
            BorderBrush = PclUi.Frozen(Color.FromRgb(0xDF, 0xEA, 0xFB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true
        };

        var consoleGrid = new Grid();
        _items = new StackPanel { Margin = new Thickness(9, 7, 9, 8) };
        _scroll = new ScrollViewer
        {
            Style = (Style)Application.Current.FindResource("ScrollViewerFullMargin"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0),
            Content = _items,
            Background = Brushes.Transparent
        };
        consoleGrid.Children.Add(_scroll);

        _empty = BuildEmpty();
        consoleGrid.Children.Add(_empty);
        console.Child = consoleGrid;

        Grid.SetRow(console, 1);
        Children.Add(console);

        _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _copyTimer.Tick += (_, _) =>
        {
            _copyTimer.Stop();
            SetState(_state);
        };
    }

    public string Text => string.Join(Environment.NewLine, _lines);

    private static Border BuildEmpty()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = PclUi.Icon(
            "M3.5,2.5 L12.5,2.5 L12.5,13.5 L3.5,13.5 Z M6,5.5 L10,5.5 M6,8 L10,8 M6,10.5 L8.5,10.5",
            Color.FromRgb(0x9C, 0xB8, 0xDC), 28, 1.4);
        panel.Children.Add(icon);
        var title = PclUi.Label("还没有运行记录", 12.5, Color.FromRgb(0x73, 0x80, 0x92), FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(title);
        var hint = PclUi.Label("在补丁页点击“开始补丁”后，这里会显示实时输出。", 11, Color.FromRgb(0x9C, 0xA6, 0xB5));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(hint);

        var layer = new Border
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Child = panel
        };
        return layer;
    }

    public void SetState(LogState state)
    {
        _state = state;
        if (_copyTimer.IsEnabled) return;

        Color color;
        string text;
        switch (state)
        {
            case LogState.Running:
                color = PclUi.Accent;
                text = "正在运行";
                break;
            case LogState.Done:
                color = PclUi.Green;
                text = "已完成";
                break;
            case LogState.Error:
                color = PclUi.Red;
                text = "运行失败";
                break;
            default:
                color = PclUi.Muted;
                text = "等待运行";
                break;
        }

        _stateDot.Background = PclUi.Frozen(color);
        _stateText.Text = text;
        _stateText.Foreground = PclUi.Frozen(color);

        if (state == LogState.Running)
        {
            _stateDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        else
        {
            _stateDot.BeginAnimation(OpacityProperty, null);
            _stateDot.Opacity = 1;
        }
    }

    public void Clear()
    {
        _lines.Clear();
        _items.Children.Clear();
        _empty.Visibility = Visibility.Visible;
        _countText.Text = "0 行";
        SetState(LogState.Idle);
    }

    public void Append(string line)
    {
        if (line is null) return;
        _lines.Add(line);
        _empty.Visibility = Visibility.Collapsed;

        if (_lines.Count > 1600)
        {
            _lines.RemoveAt(0);
            if (_items.Children.Count > 0) _items.Children.RemoveAt(0);
        }

        _items.Children.Add(BuildLine(line));
        _countText.Text = _lines.Count + " 行";

        if (_autoScroll)
            _scroll.ScrollToEnd();
    }

    public void ScrollToEnd()
    {
        _scroll.ScrollToEnd();
    }

    private void SetAutoScroll(bool enabled)
    {
        _autoScroll = enabled;
        _autoDot.Background = PclUi.Frozen(enabled ? PclUi.Accent : Color.FromRgb(0xA6, 0xA6, 0xA6));
        _autoText.Foreground = PclUi.Frozen(enabled ? PclUi.Accent : PclUi.Muted);
        if (enabled) _scroll.ScrollToEnd();
    }

    private void CopyAll()
    {
        if (_lines.Count == 0) return;
        try
        {
            Clipboard.SetText(Text);
            _stateText.Text = "已复制 " + _lines.Count + " 行";
            _stateText.Foreground = PclUi.Frozen(PclUi.Green);
            _copyTimer.Stop();
            _copyTimer.Start();
        }
        catch
        {
            _stateText.Text = "复制失败";
            _stateText.Foreground = PclUi.Frozen(PclUi.Red);
            _copyTimer.Stop();
            _copyTimer.Start();
        }
    }

    private static FrameworkElement BuildLine(string line)
    {
        var block = new TextBlock
        {
            FontFamily = PclUi.Mono,
            FontSize = 11.5,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            Foreground = PclUi.InkBrush,
            Margin = new Thickness(0, 0, 0, 1)
        };

        if (line.StartsWith("$ ", StringComparison.Ordinal))
        {
            block.Inlines.Add(new Run(line) { Foreground = PclUi.Frozen(Color.FromRgb(0x8C, 0x9A, 0xAD)) });
            return block;
        }

        var match = Regex.Match(line, @"^([A-Za-z_]+)\s*:");
        if (!match.Success)
        {
            block.Inlines.Add(new Run(line));
            return block;
        }

        var tag = match.Groups[1].Value;
        var prefix = line.Substring(0, match.Length);
        var rest = line.Substring(match.Length);

        Color prefixColor;
        Color restColor = PclUi.Ink;
        FontWeight restWeight = FontWeights.Normal;
        switch (tag)
        {
            case "patch":
            case "verify":
                prefixColor = PclUi.Green;
                restColor = Color.FromRgb(0x2C, 0x5E, 0x45);
                break;
            case "probe":
                prefixColor = PclUi.Accent;
                restColor = rest.IndexOf("SKIP", StringComparison.OrdinalIgnoreCase) >= 0
                    ? PclUi.Amber
                    : Color.FromRgb(0x5B, 0x6B, 0x80);
                break;
            case "written":
                prefixColor = PclUi.Green;
                restWeight = FontWeights.SemiBold;
                break;
            case "WARN":
                prefixColor = PclUi.Amber;
                restColor = PclUi.Amber;
                break;
            case "PATCHER_ERROR":
            case "GUI_ERROR":
                prefixColor = PclUi.Red;
                restColor = PclUi.Red;
                break;
            case "source":
            case "output":
            case "sha256":
            case "version":
            case "known":
            case "fp":
                prefixColor = Color.FromRgb(0x6B, 0x7C, 0x93);
                restColor = Color.FromRgb(0x4A, 0x55, 0x68);
                break;
            default:
                prefixColor = Color.FromRgb(0x8C, 0x9A, 0xAD);
                break;
        }

        block.Inlines.Add(new Run(prefix) { Foreground = PclUi.Frozen(prefixColor), FontWeight = FontWeights.SemiBold });
        block.Inlines.Add(new Run(rest) { Foreground = PclUi.Frozen(restColor), FontWeight = restWeight });
        return block;
    }
}
