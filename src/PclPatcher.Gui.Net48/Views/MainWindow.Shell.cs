namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Shell
    // ------------------------------------------------------------------

    private UIElement BuildShell()
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        shell.Children.Add(BuildTitleBar());

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _pages = new Dictionary<PageKind, FrameworkElement>
        {
            [PageKind.Patch] = BuildPatchPage(),
            [PageKind.Log] = BuildLogPage(),
            [PageKind.About] = BuildAboutPage()
        };

        var host = new Grid
        {
            Background = PclUi.PageBrush,
            ClipToBounds = true
        };
        foreach (var page in _pages.Values)
        {
            page.Visibility = Visibility.Collapsed;
            host.Children.Add(page);
        }
        Grid.SetColumn(host, 1);
        body.Children.Add(host);
        body.Children.Add(BuildNavRail());

        Grid.SetRow(body, 1);
        shell.Children.Add(body);

        _dropOverlay = BuildDropOverlay();
        Grid.SetRowSpan(_dropOverlay, 2);
        Panel.SetZIndex(_dropOverlay, 100);
        shell.Children.Add(_dropOverlay);

        ShowPage(PageKind.Patch, false);
        return shell;
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Grid
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(0x0E, 0x66, 0xC2),
                Color.FromRgb(0x14, 0x78, 0xDC),
                0),
            ClipToBounds = true
        };

        // Drag surface lives behind the header controls so the page tabs and the
        // window buttons keep their clicks; dragging still works on the empty bar.
        var dragSurface = new Border { Background = Brushes.Transparent };
        dragSurface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        bar.Children.Add(dragSurface);

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
            IsHitTestVisible = false
        };
        var titleMark = new Image
        {
            Source = LoadIcon("PCLPatcher-mark-256.png"),
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(titleMark, BitmapScalingMode.HighQuality);
        left.Children.Add(titleMark);
        left.Children.Add(new TextBlock
        {
            Text = "PCL",
            FontFamily = PclUi.Font,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        left.Children.Add(new TextBlock
        {
            Text = "Patcher",
            FontFamily = PclUi.Font,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = PclUi.Brush(0xFF, 0xFF, 0xFF, 0xE6),
            Margin = new Thickness(7, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        left.Children.Add(new Border
        {
            Background = PclUi.Brush(0xFF, 0xFF, 0xFF, 0x2E),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 3),
            Margin = new Thickness(10, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = PclUi.Label("第三方", 10, Colors.White)
        });
        left.Children.Add(new TextBlock
        {
            Text = "IL 补丁工具",
            FontFamily = PclUi.Font,
            FontSize = 10.5,
            Foreground = PclUi.Brush(0xFF, 0xFF, 0xFF, 0xB8),
            Margin = new Thickness(10, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        bar.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        _headerTabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        _headerTabs.Children.Add(BuildHeaderTab(PageKind.Patch, "补丁", "M8,1.4 L14,4.8 L14,11.2 L8,14.6 L2,11.2 L2,4.8 Z M2,4.8 L8,8.2 L14,4.8 M8,8.2 L8,14.6").Root);
        _headerTabs.Children.Add(BuildHeaderTab(PageKind.Log, "日志", "M3.2,2.2 L12.8,2.2 L12.8,13.8 L3.2,13.8 Z M5.6,5.4 L10.4,5.4 M5.6,8 L10.4,8 M5.6,10.6 L8.8,10.6").Root);
        _headerTabs.Children.Add(BuildHeaderTab(PageKind.About, "关于", "M8,2 A6,6 0 1 0 8,14 A6,6 0 0 0 8,2 M8,7 L8,11 M8,5 L8,5.1").Root);
        right.Children.Add(_headerTabs);

        _startPill = BuildStartPill();
        right.Children.Add(_startPill);
        right.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Background = PclUi.Brush(0xFF, 0xFF, 0xFF, 0x24),
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var minimize = ChromeButton("\uE921", "最小化", () => WindowState = WindowState.Minimized);
        var close = ChromeButton("\uE8BB", "关闭", Close, true);
        right.Children.Add(minimize);
        right.Children.Add(close);
        bar.Children.Add(right);

        bar.Children.Add(new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = PclUi.Brush(0x0A, 0x4F, 0xA8, 0x66),
            IsHitTestVisible = false
        });

        _activityHost = new Grid
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Bottom,
            ClipToBounds = true,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };
        _activityBar = new Border
        {
            Width = 170,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF),
                0),
            RenderTransform = new TranslateTransform(-170, 0)
        };
        _activityHost.Children.Add(_activityBar);
        bar.Children.Add(_activityHost);

        return bar;
    }

    private Border BuildStartPill()
    {
        var background = PclUi.Live(Colors.White);
        var pill = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(13, 0, 14, 0),
            Background = background,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "开始补丁（F5）"
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var play = new Path
        {
            Data = Geometry.Parse("M0,0 L9,5.5 L0,11 Z"),
            Fill = PclUi.Frozen(PclUi.Accent),
            Width = 9,
            Height = 11,
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(play);
        content.Children.Add(new TextBlock
        {
            Text = "开始补丁",
            FontFamily = PclUi.Font,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = PclUi.Frozen(PclUi.Accent),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        pill.Child = content;
        pill.MouseEnter += (_, _) => PclUi.AnimateColor(background, Color.FromRgb(0xEA, 0xF2, 0xFE));
        pill.MouseLeave += (_, _) => PclUi.AnimateColor(background, Colors.White);
        pill.MouseLeftButtonUp += (_, _) =>
        {
            if (!_busy) Start_Click();
        };
        pill.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        pill.RenderTransform = scale;
        pill.PreviewMouseLeftButtonDown += (_, _) => AnimateScale(scale, 0.97, 80);
        pill.PreviewMouseLeftButtonUp += (_, _) => AnimateScale(scale, 1, 120);
        pill.MouseEnter += (_, _) => AnimateScale(scale, 1.02, 120);
        pill.MouseLeave += (_, _) => AnimateScale(scale, 1, 160);
        return pill;
    }

    private HeaderTab BuildHeaderTab(PageKind page, string text, string iconData)
    {
        var background = PclUi.Live(Colors.Transparent);
        var root = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 0, 12, 0),
            Background = background,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = PclUi.Icon(iconData, Colors.White, 14, 1.4);
        content.Children.Add(icon);
        var label = PclUi.Label(text, 14, Colors.White);
        label.Margin = new Thickness(6, 0, 0, 0);
        content.Children.Add(label);
        root.Child = content;

        var tab = new HeaderTab { Page = page, Root = root, Icon = icon, Label = label };
        _headerTabItems.Add(tab);

        root.MouseEnter += (_, _) =>
        {
            if (!tab.Active) PclUi.AnimateColor(background, Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), 120);
        };
        root.MouseLeave += (_, _) =>
        {
            if (!tab.Active) PclUi.AnimateColor(background, Colors.Transparent, 160);
        };
        root.MouseLeftButtonUp += (_, _) => ShowPage(page);
        return tab;
    }

    private static void SetHeaderActive(HeaderTab tab, bool active)
    {
        if (tab is null) return;
        tab.Active = active;
        var brush = (SolidColorBrush)tab.Root.Background;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = active ? Colors.White : Colors.Transparent;
        tab.Icon.Stroke = PclUi.Frozen(active ? PclUi.Accent : Colors.White);
        tab.Label.Foreground = PclUi.Frozen(active ? PclUi.Accent : Colors.White);
    }

    private Button ChromeButton(string glyph, string name, Action click, bool close = false)
    {
        var hoverBrush = PclUi.Live(Colors.Transparent);
        var circle = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = hoverBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = PclUi.Icons,
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.None
            }
        };

        var content = new Grid { Width = 44, Height = 56 };
        content.Children.Add(circle);
        var button = new Button
        {
            Width = 44,
            Height = 56,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Content = content,
            Focusable = false,
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetName(button, name);

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        button.Template = template;

        button.MouseEnter += (_, _) =>
        {
            PclUi.AnimateColor(hoverBrush,
                close ? Color.FromRgb(0xE8, 0x11, 0x23) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 110);
            circle.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = close ? Color.FromRgb(0xE8, 0x11, 0x23) : Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = close ? 0.45 : 0.28
            };
        };
        button.MouseLeave += (_, _) =>
        {
            PclUi.AnimateColor(hoverBrush, Colors.Transparent, 160);
            circle.Effect = null;
        };
        button.Click += (_, _) => click();
        return button;
    }

    private UIElement BuildNavRail()
    {
        var rail = new Border
        {
            Background = PclUi.Frozen(Color.FromRgb(0xFD, 0xFD, 0xFE)),
            BorderBrush = PclUi.Frozen(Color.FromRgb(0xE6, 0xEE, 0xFA)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var items = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        _navPatch = CreateNavItem(PageKind.Patch, "补丁", "M8,1.4 L14,4.8 L14,11.2 L8,14.6 L2,11.2 L2,4.8 Z M2,4.8 L8,8.2 L14,4.8 M8,8.2 L8,14.6", "补丁设置（Ctrl+1）");
        _navLog = CreateNavItem(PageKind.Log, "日志", "M3.2,2.2 L12.8,2.2 L12.8,13.8 L3.2,13.8 Z M5.6,5.4 L10.4,5.4 M5.6,8 L10.4,8 M5.6,10.6 L8.8,10.6", "运行日志（Ctrl+2）");
        _navAbout = CreateNavItem(PageKind.About, "关于", "M8,2 A6,6 0 1 0 8,14 A6,6 0 0 0 8,2 M8,7 L8,11 M8,5 L8,5.1", "关于与署名（Ctrl+3）");
        items.Children.Add(_navPatch.Root);
        items.Children.Add(_navLog.Root);
        items.Children.Add(_navAbout.Root);
        grid.Children.Add(items);

        rail.Child = grid;
        return rail;
    }

    private NavItem CreateNavItem(PageKind page, string text, string iconData, string tip)
    {
        var root = new Grid
        {
            Height = 44,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = tip
        };
        AutomationProperties.SetName(root, text);

        var hover = new Border
        {
            Margin = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(4),
            Background = PclUi.Live(Colors.Transparent)
        };
        root.Children.Add(hover);

        var indicator = new Border
        {
            Width = 3,
            Height = 40,
            CornerRadius = new CornerRadius(1.5),
            Background = PclUi.Frozen(PclUi.Accent),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        root.Children.Add(indicator);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = PclUi.Icon(iconData, PclUi.Muted, 20, 1.5);
        content.Children.Add(icon);
        var label = PclUi.Label(text, 15, PclUi.Ink);
        label.Margin = new Thickness(10, 0, 0, 0);
        content.Children.Add(label);
        root.Children.Add(content);

        var item = new NavItem
        {
            Page = page,
            Root = root,
            Hover = hover,
            Indicator = indicator,
            Icon = icon,
            Label = label
        };

        root.MouseEnter += (_, _) =>
        {
            if (!item.Active) PclUi.AnimateColor((SolidColorBrush)hover.Background, Color.FromRgb(0xF2, 0xF7, 0xFF), 120);
        };
        root.MouseLeave += (_, _) =>
        {
            if (!item.Active) PclUi.AnimateColor((SolidColorBrush)hover.Background, Colors.Transparent, 160);
        };
        root.MouseLeftButtonUp += (_, _) => ShowPage(page);
        return item;
    }

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void ShowPage(PageKind page, bool animate = true)
    {
        if (_pages is null) return;
        if (_page == page && _pages[page].Visibility == Visibility.Visible) return;

        foreach (var pair in _pages)
            pair.Value.Visibility = pair.Key == page ? Visibility.Visible : Visibility.Collapsed;

        _page = page;
        SetNavActive(_navPatch, page == PageKind.Patch);
        SetNavActive(_navLog, page == PageKind.Log);
        SetNavActive(_navAbout, page == PageKind.About);
        foreach (var tab in _headerTabItems) SetHeaderActive(tab, tab.Page == page);

        if (animate) PclUi.FadeIn(_pages[page], 170, 8);
        if (page == PageKind.Log) _log?.ScrollToEnd();
    }

    private static void SetNavActive(NavItem item, bool active)
    {
        if (item is null) return;
        item.Active = active;
        item.Indicator.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(active ? 1 : 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        item.Icon.Stroke = PclUi.Frozen(active ? PclUi.Accent : PclUi.Muted);
        item.Label.Foreground = PclUi.Frozen(active ? PclUi.Accent : PclUi.Ink);
        PclUi.AnimateColor((SolidColorBrush)item.Hover.Background, Colors.Transparent, 160);
    }
}
