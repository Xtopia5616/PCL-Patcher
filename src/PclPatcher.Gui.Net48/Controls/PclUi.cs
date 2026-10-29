namespace PclPatcher.Gui.Net48;

/// <summary>
/// Small set of PCL-flavoured primitives shared by the code-built pages.
/// Colours and metrics follow PCL's Application.xaml / FormMain so that the
/// patcher reads as a PCL page rather than a generic WPF window.
/// </summary>
internal static class PclUi
{
    public static readonly Color Accent = Color.FromRgb(0x13, 0x70, 0xF3);
    public static readonly Color AccentDark = Color.FromRgb(0x0B, 0x5B, 0xCB);
    public static readonly Color AccentLight = Color.FromRgb(0x48, 0x90, 0xF5);
    public static readonly Color Ink = Color.FromRgb(0x34, 0x3D, 0x4A);
    public static readonly Color Muted = Color.FromRgb(0x8C, 0x8C, 0x8C);
    public static readonly Color Page = Color.FromRgb(0xD5, 0xE6, 0xFD);
    public static readonly Color Line = Color.FromRgb(0xE3, 0xEC, 0xFA);
    public static readonly Color Green = Color.FromRgb(0x1E, 0x9E, 0x5A);
    public static readonly Color Amber = Color.FromRgb(0xB4, 0x53, 0x09);
    public static readonly Color Red = Color.FromRgb(0xCE, 0x21, 0x11);
    public static readonly Color White = Colors.White;

    public static readonly FontFamily Font = ResolveFont();
    public static readonly FontFamily Mono = new FontFamily("Consolas");
    public static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

    public static readonly SolidColorBrush InkBrush = Frozen(Ink);
    public static readonly SolidColorBrush MutedBrush = Frozen(Muted);
    public static readonly SolidColorBrush AccentBrush = Frozen(Accent);
    public static readonly SolidColorBrush LineBrush = Frozen(Line);
    public static readonly SolidColorBrush PageBrush = Frozen(Page);
    public static readonly SolidColorBrush WhiteBrush = Frozen(Colors.White);

    public static FontFamily ResolveFont()
    {
        var fromApp = Application.Current?.Resources["PclFontFamily"] as FontFamily;
        return fromApp ?? new FontFamily("Microsoft YaHei UI");
    }

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Live(Color color) => new SolidColorBrush(color);

    public static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255)
        => Frozen(Color.FromArgb(a, r, g, b));

    public static TextBlock Label(
        string text,
        double size = 13,
        Color? color = null,
        FontWeight? weight = null,
        bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = Font,
            Foreground = Frozen(color ?? Ink),
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (weight.HasValue) block.FontWeight = weight.Value;
        return block;
    }

    public static TextBlock Glyph(string glyph, double size, Color color)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = Icons,
            FontSize = size,
            Foreground = Frozen(color),
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static Path Icon(string data, Color color, double size = 18, double thickness = 1.6)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = Frozen(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return path;
    }

    public static Border Pill(string text, Color foreground, Color background, double size = 11, double padX = 8)
    {
        return new Border
        {
            Background = Frozen(background),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(padX, 2, padX, 3),
            Child = Label(text, size, foreground, FontWeights.SemiBold),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static PCL.MyButton Button(string text, Action click, double height = 34, bool highlight = false)
    {
        var button = new PCL.MyButton
        {
            Text = text,
            Height = height,
            Padding = new Thickness(14, 0, 14, 0)
        };
        if (highlight) button.ColorType = PCL.MyButton.ColorState.Highlight;
        button.Click += (_, _) => click();
        return button;
    }

    public static PCL.MyCard Card(string title, out StackPanel body, double bottom = 10)
    {
        var card = new PCL.MyCard
        {
            Title = title,
            Margin = new Thickness(0, 0, 0, bottom)
        };
        if (card.MainTextBlock != null) card.MainTextBlock.FontSize = 16;
        body = new StackPanel { Margin = new Thickness(15, 40, 15, 15) };
        card.Children.Add(body);
        return card;
    }

    public static void AnimateColor(SolidColorBrush brush, Color to, int ms = 140)
    {
        var animation = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    public static void FadeIn(UIElement element, int ms = 180, double offsetX = 0)
    {
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });

        if (Math.Abs(offsetX) < 0.01) return;
        var transform = new TranslateTransform(offsetX, 0);
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offsetX, 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    public static void AttachTint(Border border, Color hover, Color normal, int ms = 120)
    {
        var brush = Live(normal);
        border.Background = brush;
        border.MouseEnter += (_, _) => AnimateColor(brush, hover, ms);
        border.MouseLeave += (_, _) => AnimateColor(brush, normal, ms);
    }

    public static void AttachScale(FrameworkElement element, double hover = 1.03)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;

        void Animate(double value, int ms)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        }

        element.MouseEnter += (_, _) => Animate(hover, 120);
        element.MouseLeave += (_, _) => Animate(1, 160);
        element.PreviewMouseLeftButtonDown += (_, _) => Animate(0.97, 80);
        element.PreviewMouseLeftButtonUp += (_, _) => Animate(hover, 120);
    }

    public static Border Separator(double top = 0, double bottom = 0)
    {
        return new Border
        {
            Height = 1,
            Background = Frozen(Color.FromRgb(0xEE, 0xF3, 0xFA)),
            Margin = new Thickness(0, top, 0, bottom),
            SnapsToDevicePixels = true
        };
    }

    public static Hyperlink Link(string text, string url, double size = 12.5)
    {
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text))
        {
            Foreground = Frozen(Accent),
            TextDecorations = null,
            FontSize = size
        };
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // A missing browser association should not crash the patcher.
            }
        };
        return link;
    }

    public static TextBlock LinkLine(string text, string url, double size = 12.5)
    {
        var block = new TextBlock { Margin = new Thickness(0, 2, 0, 2), FontFamily = Font };
        block.Inlines.Add(Link(text, url, size));
        return block;
    }

    /// <summary>Windows 11 rounded corners; harmless on older systems.</summary>
    public static void EnableRoundedCorners(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                var preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
            }
            catch
            {
                // DWM is not available (Windows 10 or older), keep square corners.
            }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
