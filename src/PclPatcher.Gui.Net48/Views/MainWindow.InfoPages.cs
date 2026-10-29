namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Log page
    // ------------------------------------------------------------------

    private LogPanel _log;

    private FrameworkElement BuildLogPage()
    {
        var root = new Grid { Margin = new Thickness(22, 20, 22, 16) };
        var card = new PCL.MyCard
        {
            Title = "运行日志",
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var body = new Grid { Margin = new Thickness(15, 40, 15, 15) };
        _log = new LogPanel();
        body.Children.Add(_log);
        card.Children.Add(body);
        root.Children.Add(card);
        return root;
    }

    // ------------------------------------------------------------------
    // About page
    // ------------------------------------------------------------------

    private FrameworkElement BuildAboutPage()
    {
        var scroll = new ScrollViewer
        {
            Style = (Style)Application.Current.FindResource("ScrollViewerFullMargin"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0),
            Background = Brushes.Transparent
        };
        var root = new Grid { Margin = new Thickness(22, 20, 22, 16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(right, 1);
        root.Children.Add(left);
        root.Children.Add(right);

        var hero = PclUi.Card("关于", out var heroBody, 10);
        left.Children.Add(hero);
        var heroHeader = new Grid();
        heroHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heroHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = new Image
        {
            Width = 54,
            Height = 54,
            Source = LoadIcon("PCLPatcher-256.png"),
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        heroHeader.Children.Add(logo);
        var heroText = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heroText.Children.Add(PclUi.Label("Plain Craft Launcher (PCL) Patcher", 14.5, PclUi.Ink, FontWeights.SemiBold, true));
        heroText.Children.Add(PclUi.Label("第三方 · 非官方补丁工具", 11.5, PclUi.Muted));
        heroText.Children.Add(PclUi.Label("作者：Xtopia（开发协助：DeepSeek）", 11.5, PclUi.Accent));
        Grid.SetColumn(heroText, 1);
        heroHeader.Children.Add(heroText);
        heroBody.Children.Add(heroHeader);

        var badges = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var versionPill = PclUi.Pill("正在检测版本…", PclUi.Accent, Color.FromRgb(0xE8, 0xF1, 0xFF));
        _versionPillLabel = (TextBlock)versionPill.Child;
        badges.Children.Add(versionPill);
        var pclPill = PclUi.Pill("PCL 控件", PclUi.Ink, Color.FromRgb(0xF0, 0xF4, 0xFA));
        pclPill.Margin = new Thickness(6, 0, 0, 0);
        badges.Children.Add(pclPill);
        var netPill = PclUi.Pill("net48", PclUi.Ink, Color.FromRgb(0xF0, 0xF4, 0xFA));
        netPill.Margin = new Thickness(6, 0, 0, 0);
        badges.Children.Add(netPill);
        heroBody.Children.Add(badges);

        var intro = PclUi.Label(
            "在本机 PCL 副本上恢复被移除的下载入口。默认直接替换源文件，替换前自动备份为 .bak，可随时还原。",
            12.5, PclUi.Ink, null, true);
        intro.LineHeight = 21;
        intro.Margin = new Thickness(0, 14, 0, 0);
        heroBody.Children.Add(intro);

        var how = PclUi.Card("工作方式", out var howBody, 10);
        left.Children.Add(how);
        howBody.Children.Add(FeatureRow("M8,1.4 L14,4.8 L14,11.2 L8,14.6 L2,11.2 L2,4.8 Z M2,4.8 L8,8.2 L14,4.8 M8,8.2 L8,14.6",
            "在原位置重建调用", "不是改常量，而是重建被编译器折叠的方法体。"));
        howBody.Children.Add(PclUi.Separator(7, 7));
        howBody.Children.Add(FeatureRow("M8,2 A6,6 0 1 0 8,14 A6,6 0 0 0 8,2 M8,7 L8,11 M8,5 L8,5.1",
            "原地补丁，自动备份", "替换前把原文件保存为 .bak；「还原备份」随时恢复原版。"));
        howBody.Children.Add(PclUi.Separator(7, 7));
        howBody.Children.Add(FeatureRow("M2.5,8 L6,11.5 L13.5,4",
            "每一步都可复核", "Probe / Apply / Verify，全模块 IL 校验后再写盘。"));

        var credits = PclUi.Card("署名与许可", out var creditsBody, 10);
        right.Children.Add(credits);
        var author = PclUi.Label("本工具作者：Xtopia（开发协助：DeepSeek）", 12.5, PclUi.Ink, FontWeights.SemiBold);
        author.Margin = new Thickness(0, 0, 0, 8);
        creditsBody.Children.Add(author);

        var credit = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = PclUi.Font,
            FontSize = 12.5,
            Foreground = PclUi.InkBrush,
            LineHeight = 21
        };
        credit.Inlines.Add(new Run("UI 控件与设计来自 PCL，作者："));
        credit.Inlines.Add(new Run("龙腾猫跃") { FontWeight = FontWeights.Bold });
        credit.Inlines.Add(new Run("。"));
        creditsBody.Children.Add(credit);

        var sponsor = new TextBlock { FontFamily = PclUi.Font, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
        sponsor.Inlines.Add(new Run("赞助 PCL：") { Foreground = PclUi.InkBrush });
        sponsor.Inlines.Add(PclUi.Link("爱发电", "https://meloong.com/afd/a/LTCat"));
        creditsBody.Children.Add(sponsor);
        creditsBody.Children.Add(PclUi.LinkLine("PCL 项目 / 源代码", "https://github.com/Meloong-Git/PCL"));
        creditsBody.Children.Add(PclUi.LinkLine("PCL 分发有限许可 / 合理使用指南", "https://github.com/Meloong-Git/PCL/blob/main/LICENCE"));

        var licenseInfo = PclUi.Label(
            "本工具原创代码以 MIT 许可发布：可自由使用、修改与再分发，保留版权与许可声明即可。PclTheme.xaml、PCL 控件与字体不在 MIT 范围内，权利归龙腾猫跃及 PCL 项目；补丁后的 PCL 仍受 PCL 许可约束，请勿分发。完整条款见项目仓库的 LICENSE、NOTICE 与 DISCLAIMER.md。",
            11, PclUi.Muted, null, true);
        licenseInfo.LineHeight = 18;
        licenseInfo.Margin = new Thickness(0, 10, 0, 0);
        creditsBody.Children.Add(licenseInfo);

        var notice = PclUi.Label(
            "本工具名称以 Plain Craft Launcher (PCL) 开头，并标注“第三方”。发布包不包含 PCL 本体；界面控件与字体在运行时从你选择的 PCL.exe 加载。",
            11, PclUi.Muted, null, true);
        notice.LineHeight = 18;
        notice.Margin = new Thickness(0, 10, 0, 0);
        creditsBody.Children.Add(notice);

        var legal = PclUi.Card("Minecraft", out var legalBody, 0);
        right.Children.Add(legal);
        legalBody.Children.Add(PclUi.Label("本工具不提供游戏本体，也不会绕过正版验证。", 12.5, PclUi.Ink, null, true));
        legalBody.Children.Add(PclUi.LinkLine("购买 Minecraft 正版：minecraft.net", "https://www.minecraft.net/"));
        var cnNotice = PclUi.Label(
            "中国大陆：网络游戏的出版、进口与运营受专门监管，Minecraft 由网易独家运营；请自行确认下载与游玩行为合规。本工具不提供下载服务，也不授权任何规避行为。",
            11, PclUi.Muted, null, true);
        cnNotice.LineHeight = 18;
        cnNotice.Margin = new Thickness(0, 8, 0, 0);
        legalBody.Children.Add(cnNotice);
        var disclaimer = PclUi.Label(
            "本工具是第三方非官方工具，与 PCL 作者、成都瓜皮龙科技有限公司、网易、Microsoft、Mojang 无关。补丁后的 PCL 属于 PCL 的修改版，请勿分发；使用风险与法律责任由使用者自行承担。完整免责声明见项目仓库的 DISCLAIMER.md。",
            11, PclUi.Muted, null, true);
        disclaimer.LineHeight = 18;
        disclaimer.Margin = new Thickness(0, 8, 0, 0);
        legalBody.Children.Add(disclaimer);

        scroll.Content = root;
        return scroll;
    }

    private Grid FeatureRow(string iconData, string title, string description)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(PclUi.Icon(iconData, PclUi.Accent, 16, 1.5));
        var text = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
        text.Children.Add(PclUi.Label(title, 12, PclUi.Ink, FontWeights.SemiBold));
        text.Children.Add(PclUi.Label(description, 10.5, PclUi.Muted, null, true));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private Hyperlink MakeInlineLink(string text, Action action)
    {
        var link = new Hyperlink(new Run(text))
        {
            Foreground = PclUi.Frozen(PclUi.Accent),
            TextDecorations = null,
            FontFamily = PclUi.Font,
            FontSize = 11.5
        };
        link.Click += (_, _) => action();
        return link;
    }
}
