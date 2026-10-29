namespace PclPatcher.Gui.Net48;

internal enum DialogTone
{
    Info,
    Warning,
    Error,
    Question
}

/// <summary>
/// Modal in PCL's visual language. System MessageBox would pull a white Win32
/// dialog into the middle of the PCL-styled window, so warnings and confirmations
/// use a small blue-caption window with PCL MyButtons instead.
/// </summary>
internal static class PclDialog
{
    public static bool Show(
        Window owner,
        string title,
        string message,
        string primary = "确定",
        string secondary = null,
        DialogTone tone = DialogTone.Info)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 452,
            SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 40),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Brushes.White,
            FontFamily = PclUi.Font
        };
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        PclUi.EnableRoundedCorners(window);

        var result = false;
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
            if (e.ButtonState == MouseButtonState.Pressed) window.DragMove();
        };
        barGrid.Children.Add(dragSurface);
        barGrid.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontFamily = PclUi.Font,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        });

        var closeBrush = PclUi.Live(Colors.Transparent);
        var closeCircle = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = closeBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = PclUi.Glyph("\uE8BB", 9, Colors.White)
        };
        var close = new Grid
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        close.Children.Add(closeCircle);
        close.MouseEnter += (_, _) =>
        {
            PclUi.AnimateColor(closeBrush, Color.FromRgb(0xE8, 0x11, 0x23), 110);
            closeCircle.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0xE8, 0x11, 0x23),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.4
            };
        };
        close.MouseLeave += (_, _) =>
        {
            PclUi.AnimateColor(closeBrush, Colors.Transparent, 160);
            closeCircle.Effect = null;
        };
        close.MouseLeftButtonUp += (_, _) => window.Close();
        barGrid.Children.Add(close);
        bar.Child = barGrid;
        shell.Children.Add(bar);

        var content = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(22, 20, 22, 16)
        };
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (brush, glyph, background) = tone switch
        {
            DialogTone.Error => (PclUi.Red, "\uE783", Color.FromRgb(0xFD, 0xEC, 0xEC)),
            DialogTone.Warning => (PclUi.Amber, "\uE7BA", Color.FromRgb(0xFF, 0xF4, 0xE5)),
            DialogTone.Question => (PclUi.Accent, "?", Color.FromRgb(0xEA, 0xF2, 0xFE)),
            _ => (PclUi.Accent, "i", Color.FromRgb(0xEA, 0xF2, 0xFE))
        };

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = PclUi.Frozen(background),
            VerticalAlignment = VerticalAlignment.Top,
            Child = PclUi.Glyph(glyph, glyph == "i" || glyph == "?" ? 15 : 13, brush)
        };
        if (glyph == "i" || glyph == "?")
        {
            ((TextBlock)icon.Child).FontFamily = PclUi.Font;
            ((TextBlock)icon.Child).FontWeight = FontWeights.Bold;
        }
        contentGrid.Children.Add(icon);

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = PclUi.Font,
            FontSize = 13,
            Foreground = PclUi.InkBrush,
            LineHeight = 21,
            Margin = new Thickness(14, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(messageText, 1);
        contentGrid.Children.Add(messageText);

        var body = new StackPanel();
        body.Children.Add(contentGrid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            var cancel = PclUi.Button(secondary, () =>
            {
                result = false;
                window.Close();
            }, 34);
            cancel.MinWidth = 82;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(cancel);
        }

        var ok = PclUi.Button(primary, () =>
        {
            result = true;
            window.Close();
        }, 34, true);
        ok.MinWidth = 88;
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        content.Child = body;
        Grid.SetRow(content, 1);
        shell.Children.Add(content);
        window.Content = shell;

        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                result = false;
                window.Close();
            }
            else if (e.Key == Key.Enter)
            {
                result = true;
                window.Close();
            }
        };

        window.Opacity = 0;
        window.Loaded += (_, _) =>
        {
            window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            ok.Focus();
        };

        window.ShowDialog();
        return result;
    }
}
