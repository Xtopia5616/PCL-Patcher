namespace PclPatcher.Gui.Net48;

/// <summary>
/// First-run risk notice. The patcher modifies a third-party executable in place,
/// so the main window stays unusable until the user explicitly acknowledges the
/// notice. Declining (button, close box or Esc) ends the patcher instead of
/// leaving a half-usable window behind.
///
/// The same window is reused in read-only mode from the 关于 page, where it shows
/// when the current revision was accepted.
/// </summary>
internal sealed class DisclaimerWindow : Window
{
    /// <summary>
    /// Revision of the copy below. Acceptance is stored per revision, so bump
    /// this string whenever the notice changes in a way users should re-read.
    /// </summary>
    public const string Version = "2026-10-29-2";

    private const string FullTextUrl =
        "https://github.com/Xtopia5616/PCL-Patcher/blob/main/DISCLAIMER.md";

    private sealed class Point
    {
        public string Title;
        public string Body;
    }

    private static readonly Point[] Points =
    {
        new Point
        {
            Title = "第三方非官方工具",
            Body = "与 PCL 作者龙腾猫跃、成都瓜皮龙科技有限公司、网易、Microsoft、Mojang 均无隶属、合作、赞助或授权关系。"
        },
        new Point
        {
            Title = "只解锁下载与联机",
            Body = "本工具只恢复下载与联机入口，不会解锁、破解或绕过任何赞助（捐赠 / 爱发电）相关内容与权益；请通过官方渠道支持 PCL 作者。"
        },
        new Point
        {
            Title = "只在你合法持有的本机副本上使用",
            Body = "补丁后的 PCL 属于 PCL 修改版，禁止分发，也不得用于代下载、账号交易或任何形式的下载服务。"
        },
        new Point
        {
            Title = "默认原地修改，存在损坏风险",
            Body = "工具会直接替换目标 exe，替换前只保留一份 .bak。断电、文件占用或安全软件拦截都可能损坏文件，请自行额外备份。"
        },
        new Point
        {
            Title = "版本依赖与选项副作用",
            Body = "补丁依赖特定 PCL 版本的 IL 结构，未知版本可能失败。--no-update 会关闭 PCL 更新；自动结束 PCL 可能丢失未保存的配置或下载。"
        },
        new Point
        {
            Title = "合规、账号与安全风险",
            Body = "反向工程与修改行为须符合你所在地区的法律及 PCL、Minecraft 许可；修改客户端可能触发安全软件、平台风控或账号处罚。"
        },
        new Point
        {
            Title = "按“现状”提供，不承担担保",
            Body = "不承诺补丁成功、稳定或长期可用；数据丢失、账号处罚及法律与合规责任由你自行承担。"
        }
    };

    private readonly bool _readOnly;
    private readonly string _acceptedAt;

    private PCL.MyCheckBox _agree;
    private PCL.MyButton _accept;
    private TextBlock _hint;

    public DisclaimerWindow(bool readOnly = false, string acceptedAt = null)
    {
        _readOnly = readOnly;
        _acceptedAt = acceptedAt;

        Title = readOnly ? "免责声明" : "使用前须知";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 60);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = PclUi.Font;
        // CenterScreen, not CenterOwner: the main window can itself sit partly
        // outside a high-DPI work area, which would push the notice off-screen.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        PclUi.EnableRoundedCorners(this);

        Content = BuildShell();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Decline();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_readOnly) Close();
                else Accept();
            }
        };

        Opacity = 0;
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            if (_readOnly) _accept?.Focus();
            else _agree?.Focus();
        };

        // SizeToContent has settled by now, so the final bounds are known.
        ContentRendered += (_, _) => ClampToWorkArea();
    }

    /// <summary>
    /// Pulls the window into the monitor's work area using physical pixels.
    /// WPF's SystemParameters.WorkArea and the window's own DIP bounds disagree
    /// on high-DPI screens, which can leave the consent buttons below the
    /// taskbar; Win32 has no such ambiguity.
    /// </summary>
    private void ClampToWorkArea()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
            if (!GetWindowRect(handle, out var rect)) return;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;

            var left = Math.Max(info.Work.Left, Math.Min(rect.Left, info.Work.Right - width));
            var top = Math.Max(info.Work.Top, Math.Min(rect.Top, info.Work.Bottom - height));
            if (left == rect.Left && top == rect.Top) return;

            SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
        catch
        {
            // A failed clamp only affects placement; the notice still works.
        }
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    private UIElement BuildShell()
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        shell.Children.Add(BuildTitleBar());

        var body = new StackPanel();

        // Hero ------------------------------------------------------------
        var hero = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = PclUi.Frozen(Color.FromRgb(0xEA, 0xF2, 0xFE)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = PclUi.Icon("M8,2 L13.5,4.5 L13.5,9 C13.5,12 8,14.5 8,14.5 C8,14.5 2.5,12 2.5,9 L2.5,4.5 Z",
                PclUi.Accent, 24, 1.5)
        };
        hero.Children.Add(icon);

        var heading = new StackPanel { Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(PclUi.Label("免责声明与使用风险提示", 17, PclUi.Ink, FontWeights.SemiBold));
        heading.Children.Add(PclUi.Label(
            _readOnly
                ? (_acceptedAt is null
                    ? "当前版本尚未确认。"
                    : "本版本已于 " + _acceptedAt + " 确认。")
                : "第三方非官方工具 · 首次使用前请阅读并确认",
            11.5, PclUi.Muted, null, true));
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        badges.Children.Add(PclUi.Pill("条款版本 " + Version, PclUi.Accent, Color.FromRgb(0xE8, 0xF1, 0xFF), 10, 7));
        var thirdParty = PclUi.Pill("第三方 · 非官方", PclUi.Ink, Color.FromRgb(0xF0, 0xF4, 0xFA), 10, 7);
        thirdParty.Margin = new Thickness(6, 0, 0, 0);
        badges.Children.Add(thirdParty);
        heading.Children.Add(badges);
        Grid.SetColumn(heading, 1);
        hero.Children.Add(heading);
        body.Children.Add(hero);

        // Risk points -----------------------------------------------------
        var list = new StackPanel();
        for (var i = 0; i < Points.Length; i++) list.Children.Add(BuildPoint(i + 1, Points[i]));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            MaxHeight = 322,
            Padding = new Thickness(0, 0, 8, 0),
            Content = list
        };
        body.Children.Add(scroll);

        // Full-text note --------------------------------------------------
        var note = new TextBlock
        {
            FontFamily = PclUi.Font,
            FontSize = 11,
            Foreground = PclUi.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17,
            Margin = new Thickness(0, 10, 0, 0)
        };
        note.Inlines.Add(new Run("以上为摘要，完整免责声明与责任限制见 "));
        note.Inlines.Add(PclUi.Link("DISCLAIMER.md", FullTextUrl, 11));
        note.Inlines.Add(new Run("。本提示不构成法律意见，也不改变项目的 MIT 许可。"));
        body.Children.Add(note);

        // Consent ---------------------------------------------------------
        if (!_readOnly) body.Children.Add(BuildConsent());

        body.Children.Add(BuildButtons());

        var content = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(24, 18, 24, 18),
            Child = body
        };
        Grid.SetRow(content, 1);
        shell.Children.Add(content);
        return shell;
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(0x0E, 0x66, 0xC2),
                Color.FromRgb(0x14, 0x78, 0xDC),
                0)
        };

        var grid = new Grid();
        var dragSurface = new Border { Background = Brushes.Transparent };
        dragSurface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        grid.Children.Add(dragSurface);

        var caption = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            IsHitTestVisible = false
        };
        caption.Children.Add(PclUi.Glyph("\uE72E", 12, Colors.White));
        var title = PclUi.Label(Title, 13.5, Colors.White, FontWeights.SemiBold);
        title.Margin = new Thickness(8, 0, 0, 0);
        caption.Children.Add(title);
        grid.Children.Add(caption);

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
        close.MouseLeftButtonUp += (_, _) => Decline();
        AutomationProperties.SetName(close, "关闭");
        grid.Children.Add(close);

        bar.Child = grid;
        return bar;
    }

    private static UIElement BuildPoint(int number, Point point)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The badge needs its own centered layout: a Border stretches a TextBlock
        // child horizontally, which left-aligns the digit and makes the circle
        // look off-centre. PCL English also has eccentric digit bearings, so the
        // tiny number uses a system face with predictable metrics.
        var numberLabel = new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = PclUi.Frozen(PclUi.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(0),
            // Digits sit a hair below the line-box centre; nudge the glyph up.
            Margin = new Thickness(0, -1, 0, 0)
        };
        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = PclUi.Frozen(Color.FromRgb(0xEA, 0xF2, 0xFE)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
            Child = numberLabel
        };
        row.Children.Add(badge);

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(PclUi.Label(point.Title, 12.5, PclUi.Ink, FontWeights.SemiBold, true));
        var body = PclUi.Label(point.Body, 11.5, PclUi.Muted, null, true);
        body.LineHeight = 19;
        body.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(body);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private UIElement BuildConsent()
    {
        var panel = new StackPanel();
        _agree = new PCL.MyCheckBox
        {
            Text = "我已阅读并理解上述风险",
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_agree.LabText != null)
        {
            _agree.LabText.FontSize = 12.5;
            _agree.LabText.FontFamily = PclUi.Font;
        }
        _agree.Change += (_, __) => UpdateConsent();
        AutomationProperties.SetName(_agree, "我已阅读并理解上述风险");
        panel.Children.Add(_agree);

        _hint = PclUi.Label("勾选后即可点击「我已知情」继续。", 10.5, PclUi.Muted, null, true);
        _hint.Margin = new Thickness(0, 3, 0, 0);
        panel.Children.Add(_hint);

        return new Border
        {
            Background = PclUi.Frozen(Color.FromRgb(0xF7, 0xFA, 0xFF)),
            BorderBrush = PclUi.Frozen(Color.FromRgb(0xE3, 0xEC, 0xFA)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9, 12, 10),
            Margin = new Thickness(0, 12, 0, 0),
            Child = panel
        };
    }

    private UIElement BuildButtons()
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        if (_readOnly)
        {
            _accept = PclUi.Button("关闭", Close, 34, true);
            _accept.MinWidth = 88;
            buttons.Children.Add(_accept);
            return buttons;
        }

        var decline = PclUi.Button("不同意，退出", Decline, 34);
        decline.MinWidth = 104;
        decline.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(decline);

        _accept = PclUi.Button("我已知情", Accept, 34, true);
        _accept.MinWidth = 108;
        _accept.IsEnabled = false;
        _accept.Opacity = 0.5;
        AutomationProperties.SetName(_accept, "我已知情，开始使用");
        buttons.Children.Add(_accept);
        return buttons;
    }

    private void UpdateConsent()
    {
        var checkedNow = _agree?.Checked == true;
        if (_accept != null)
        {
            _accept.IsEnabled = checkedNow;
            _accept.Opacity = checkedNow ? 1 : 0.5;
        }
        if (_hint != null)
        {
            _hint.Text = checkedNow ? "已确认，可以继续。" : "勾选后即可点击「我已知情」继续。";
            _hint.Foreground = PclUi.Frozen(checkedNow ? PclUi.Green : PclUi.Muted);
        }
    }

    private void Accept()
    {
        if (_readOnly)
        {
            Close();
            return;
        }
        if (_agree?.Checked != true)
        {
            if (_hint != null)
            {
                _hint.Text = "请先勾选「我已阅读并理解上述风险」。";
                _hint.Foreground = PclUi.Frozen(PclUi.Red);
            }
            _agree?.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Decline()
    {
        if (_readOnly) Close();
        else DialogResult = false;
    }
}
