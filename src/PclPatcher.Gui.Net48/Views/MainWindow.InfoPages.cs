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

        // The repository address is spelled out, not just labelled: the About
        // page doubles as the place to copy the project link from.
        var repoLine = new TextBlock
        {
            FontFamily = PclUi.Font,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        repoLine.Inlines.Add(new Run("GitHub 仓库：") { Foreground = PclUi.MutedBrush });
        repoLine.Inlines.Add(PclUi.Link("github.com/Xtopia5616/PCL-Patcher", UpdateChecker.RepositoryUrl, 11.5));
        repoLine.Inlines.Add(new Run("（Issues / 更新记录见下方「版本与更新」）") { Foreground = PclUi.MutedBrush });
        heroBody.Children.Add(repoLine);

        var intro = PclUi.Label(
            "在本机 PCL 副本上恢复被移除的下载入口；可选择同时恢复联机入口。默认直接替换源文件，替换前自动备份为 .bak，可随时还原。",
            12.5, PclUi.Ink, null, true);
        intro.LineHeight = 21;
        intro.Margin = new Thickness(0, 14, 0, 0);
        heroBody.Children.Add(intro);

        // Version and updates: the GUI reads its own version from the assembly
        // metadata and compares it with the newest GitHub release on demand.
        // Without readable metadata the card says “版本未知” and the button just
        // opens the release page, which is the only useful action then.
        var updates = PclUi.Card("版本与更新", out var updateBody, 10);
        left.Children.Add(updates);

        var current = UpdateChecker.CurrentVersion;
        var unknownVersion = string.IsNullOrEmpty(current);

        var updateHead = new Grid();
        updateHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        updateHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var versionStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        versionStack.Children.Add(PclUi.Label("PCL Patcher", 11, PclUi.Muted));
        _updateVersionLabel = PclUi.Label(
            unknownVersion ? "版本未知" : "v" + current,
            17, unknownVersion ? PclUi.Amber : PclUi.Accent, FontWeights.SemiBold);
        _updateVersionLabel.Margin = new Thickness(0, 1, 0, 0);
        versionStack.Children.Add(_updateVersionLabel);
        updateHead.Children.Add(versionStack);

        _updateButton = PclUi.Button("检查更新", CheckUpdates_Click, 30);
        _updateButton.MinWidth = 88;
        _updateButton.Padding = new Thickness(12, 0, 12, 0);
        _updateButton.Margin = new Thickness(10, 0, 0, 0);
        _updateButton.VerticalAlignment = VerticalAlignment.Center;
        _updateButton.ToolTip = "读取 GitHub 上的最新发布，与本机版本比较";
        Grid.SetColumn(_updateButton, 1);
        updateHead.Children.Add(_updateButton);
        updateBody.Children.Add(updateHead);

        _updateStatus = PclUi.Label(
            unknownVersion
                ? "版本未知：没有读取到本工具的版本号，无法自动比较新旧；点「检查更新」会到 GitHub 查看最新发布。"
                : "尚未检查更新。点「检查更新」会读取 GitHub 上的最新发布并比较版本。",
            11, unknownVersion ? PclUi.Amber : PclUi.Muted, null, true);
        _updateStatus.LineHeight = 17;
        _updateStatus.Margin = new Thickness(0, 10, 0, 0);
        AutomationProperties.SetHelpText(_updateStatus, "更新检查状态");
        updateBody.Children.Add(_updateStatus);

        var updateLinks = new TextBlock
        {
            FontFamily = PclUi.Font,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0)
        };
        updateLinks.Inlines.Add(PclUi.Link("GitHub 仓库", UpdateChecker.RepositoryUrl, 11.5));
        updateLinks.Inlines.Add(new Run("  ·  ") { Foreground = PclUi.MutedBrush });
        updateLinks.Inlines.Add(PclUi.Link("发布页与更新记录", UpdateChecker.ReleasesUrl, 11.5));
        updateBody.Children.Add(updateLinks);

        var scope = PclUi.Label(
            "范围声明：本工具只解锁下载与联机入口，不会解锁、破解或绕过任何赞助（捐赠 / 爱发电）相关内容与权益。",
            11.5, PclUi.Accent, null, true);
        scope.LineHeight = 19;
        scope.Margin = new Thickness(0, 7, 0, 0);
        heroBody.Children.Add(scope);

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

        // Verified builds: the hashes this project already patched and launched.
        // Others can compare their file's hash here instead of trying blind.
        var verified = PclUi.Card("已验证构建", out var verifiedBody, 10);
        left.Children.Add(verified);
        _verifiedList = new StackPanel();
        verifiedBody.Children.Add(_verifiedList);
        var verifiedNote = PclUi.Label(
            "✓ 表示本机实机验证过（补丁 + 启动检查），哈希一致即可直接补丁；待验证条目由 --adopt 收录，补丁时仍会逐项 Probe。",
            10.5, PclUi.Muted, null, true);
        verifiedNote.LineHeight = 16;
        verifiedNote.Margin = new Thickness(0, 6, 0, 0);
        verifiedBody.Children.Add(verifiedNote);

        var credits = PclUi.Card("署名与许可", out var creditsBody, 10);
        right.Children.Add(credits);

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
        var sponsorNote = PclUi.Label(
            "请通过官方渠道支持作者；本工具不提供、也不解锁任何赞助者身份或赞助专属内容。",
            10.5, PclUi.Muted, null, true);
        sponsorNote.LineHeight = 17;
        sponsorNote.Margin = new Thickness(0, 3, 0, 0);
        creditsBody.Children.Add(sponsorNote);
        creditsBody.Children.Add(PclUi.LinkLine("PCL 项目 / 源代码", "https://github.com/Meloong-Git/PCL"));
        creditsBody.Children.Add(PclUi.LinkLine("PCL 分发有限许可 / 合理使用指南", "https://github.com/Meloong-Git/PCL/blob/main/LICENCE"));

        creditsBody.Children.Add(PclUi.Separator(7, 6));
        creditsBody.Children.Add(PclUi.LinkLine("本工具 GitHub 仓库（源码 / Issues）", UpdateChecker.RepositoryUrl));
        creditsBody.Children.Add(PclUi.LinkLine("GitHub 发布页（下载 / 更新记录）", UpdateChecker.ReleasesUrl));

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

        legalBody.Children.Add(PclUi.Separator(9, 9));
        var reopen = new TextBlock
        {
            FontFamily = PclUi.Font,
            FontSize = 11.5,
            Foreground = PclUi.MutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
        reopen.Inlines.Add(MakeInlineLink("查看免责声明与使用风险提示", ShowDisclaimerReadOnly));
        reopen.Inlines.Add(new Run("（首次使用时需确认，更新条款后需重新确认）"));
        legalBody.Children.Add(reopen);

        scroll.Content = root;
        RefreshVerifiedBuilds();
        return scroll;
    }

    /// <summary>
    /// Renders the supported build list with its verification state. This is the
    /// public "these hashes are known good" table: a matching SHA-256 means the
    /// build was already patched and launched here, so nobody has to try blind.
    /// </summary>
    private void RefreshVerifiedBuilds()
    {
        if (_verifiedList is null) return;
        _verifiedList.Children.Clear();

        if (!_catalog.IsLoaded)
        {
            _verifiedList.Children.Add(PclUi.Label("正在读取版本列表…", 11, PclUi.Muted));
            return;
        }

        foreach (var profile in _catalog.Profiles)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new TextBlock
            {
                Text = profile.Verified ? "\uE73E" : "\uE9CE",
                FontFamily = PclUi.Icons,
                FontSize = 10.5,
                Foreground = PclUi.Frozen(profile.Verified ? PclUi.Green : PclUi.Amber),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(mark);

            var name = PclUi.Label(profile.Label + (profile.Verified ? string.Empty : "（待验证）"), 11.5,
                profile.Verified ? PclUi.Ink : PclUi.Muted, FontWeights.SemiBold);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var hash = PclUi.Label(profile.ShortHash + "…", 10, PclUi.Muted);
            hash.FontFamily = PclUi.Mono;
            hash.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hash, 2);
            row.Children.Add(hash);

            _verifiedList.Children.Add(row);
        }
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
