namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Patch page
    // ------------------------------------------------------------------

    private FrameworkElement BuildPatchPage()
    {
        // The page fills the window and each column scrolls on its own. A single
        // page-level scrollbar used to stretch to the taller column (the step
        // list), so scrolling dragged the settings cards away and left the
        // bottom-left corner empty.
        var root = new Grid { Margin = new Thickness(22, 20, 22, 16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _rightColumn = new ColumnDefinition { Width = new GridLength(272) };
        root.ColumnDefinitions.Add(_rightColumn);
        _patchRoot = root;

        // Left column: the cards scroll if the window gets short, while the
        // action row stays pinned to the bottom edge of the column.
        var leftColumn = new Grid();
        leftColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(leftColumn);

        _leftStack = new StackPanel();
        _rightStack = new StackPanel();

        var leftScroll = new ScrollViewer
        {
            Style = (Style)Application.Current.FindResource("ScrollViewerFullMargin"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Content = _leftStack
        };
        leftColumn.Children.Add(leftScroll);

        var rightScroll = new ScrollViewer
        {
            Style = (Style)Application.Current.FindResource("ScrollViewerFullMargin"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(14, 0, 0, 0),
            Content = _rightStack
        };
        Grid.SetColumn(rightScroll, 1);
        root.Children.Add(rightScroll);

        // Source card -----------------------------------------------------
        var sourceCard = PclUi.Card("PCL 主程序", out var sourceBody, 10);
        _leftStack.Children.Add(sourceCard);

        var versionRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        versionRow.Children.Add(PclUi.Label("版本", 12.5, PclUi.Ink, FontWeights.SemiBold));
        // A plain read-only line, not a control: the build is detected from the
        // source file's SHA-256 and must always match the file being patched.
        _versionText = PclUi.Label("正在检测版本…", 13.5, PclUi.Ink, FontWeights.SemiBold);
        _versionText.HorizontalAlignment = HorizontalAlignment.Right;
        _versionText.VerticalAlignment = VerticalAlignment.Center;
        _versionText.TextTrimming = TextTrimming.CharacterEllipsis;
        _versionText.Margin = new Thickness(12, 0, 2, 0);
        versionRow.Children.Add(_versionText);
        sourceBody.Children.Add(versionRow);

        var sourceRow = new Grid();
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _sourceBox = new PCL.MyTextBox
        {
            Height = 34,
            MinWidth = 120,
            FontSize = 14,
            HintText = "选择或拖入 Plain Craft Launcher 2.exe"
        };
        _sourceBox.TextChanged += (_, _) => SourceBox_TextChanged();
        sourceRow.Children.Add(_sourceBox);

        _browseButton = PclUi.Button("浏览", BrowseSource, 34);
        _browseButton.Padding = new Thickness(12, 0, 12, 0);
        _browseButton.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_browseButton, 1);
        sourceRow.Children.Add(_browseButton);

        _detectButton = PclUi.Button("自动检测", AutoDetectSource, 34);
        _detectButton.Padding = new Thickness(12, 0, 12, 0);
        _detectButton.Margin = new Thickness(6, 0, 0, 0);
        _detectButton.ToolTip = "在本机查找 PCL：记住的路径、快捷方式、注册表、常见目录，以及正在运行的 PCL 进程";
        Grid.SetColumn(_detectButton, 2);
        sourceRow.Children.Add(_detectButton);
        sourceBody.Children.Add(sourceRow);

        _sourceStrip = new Border
        {
            Height = 38,
            CornerRadius = new CornerRadius(4),
            Background = PclUi.Frozen(Color.FromRgb(0xEA, 0xF2, 0xFE)),
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 9, 0, 0)
        };
        var stripGrid = new Grid();
        stripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stripLeft = new Grid();
        stripLeft.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stripLeft.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _sourceIcon = PclUi.Icon("M8,1.6 L13.2,3.8 L13.2,8.2 C13.2,11.4 8,14.4 8,14.4 C8,14.4 2.8,11.4 2.8,8.2 L2.8,3.8 Z", PclUi.Accent, 14, 1.4);
        stripLeft.Children.Add(_sourceIcon);
        _sourceMeta = PclUi.Label("正在识别文件…", 12, Color.FromRgb(0x4A, 0x55, 0x68));
        _sourceMeta.Margin = new Thickness(7, 0, 10, 0);
        Grid.SetColumn(_sourceMeta, 1);
        stripLeft.Children.Add(_sourceMeta);
        stripGrid.Children.Add(stripLeft);

        _sourceBadge = PclUi.Pill("识别中", PclUi.Accent, Color.FromRgb(0xE8, 0xF1, 0xFF), 11, 7);
        _sourceBadgeLabel = (TextBlock)_sourceBadge.Child;
        Grid.SetColumn(_sourceBadge, 1);
        stripGrid.Children.Add(_sourceBadge);
        _sourceStrip.Child = stripGrid;
        sourceBody.Children.Add(_sourceStrip);

        // Output card -----------------------------------------------------
        var outputCard = PclUi.Card("输出文件", out var outputBody, 10);
        _leftStack.Children.Add(outputCard);

        var outputRow = new Grid();
        outputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _outputBox = new PCL.MyTextBox
        {
            Height = 34,
            MinWidth = 120,
            FontSize = 14,
            HintText = "默认直接替换源文件，也可以另存为其他 exe"
        };
        _outputBox.TextChanged += (_, _) => RefreshLaunchReadiness();
        outputRow.Children.Add(_outputBox);
        var saveAs = PclUi.Button("另存为", BrowseOutput, 34);
        saveAs.Padding = new Thickness(12, 0, 12, 0);
        saveAs.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(saveAs, 1);
        outputRow.Children.Add(saveAs);

        _restoreButton = PclUi.Button("还原备份", RestoreBackup_Click, 34);
        _restoreButton.Padding = new Thickness(12, 0, 12, 0);
        _restoreButton.Margin = new Thickness(6, 0, 0, 0);
        _restoreButton.Opacity = 0.45;
        _restoreButton.IsHitTestVisible = false;
        Grid.SetColumn(_restoreButton, 2);
        outputRow.Children.Add(_restoreButton);
        outputBody.Children.Add(outputRow);

        _backupHint = PclUi.Label("默认直接替换源文件；替换前会自动备份为 .bak，可随时还原。", 11, PclUi.Muted);
        _backupHint.Margin = new Thickness(1, 8, 0, 0);
        outputBody.Children.Add(_backupHint);

        // Options card ----------------------------------------------------
        var optionCard = PclUi.Card("补丁选项", out var optionBody, 10);
        _leftStack.Children.Add(optionCard);

        _dryRunBox = AddOption(optionBody, "只探测", "校验并打印结果，不写出文件", true);
        _noUpdateBox = AddOption(optionBody, "禁用更新", "彻底关闭 PCL 自更新；不勾选时更新改走完整包，补丁会被官方版覆盖", true);
        // ForceFullPackage keeps the updater usable (the MD5-based patch URL
        // would 404 after patching), so auto-update stays on by default; the
        // patch itself is gone once the launcher replaces its own exe.
        _noUpdateBox.Checked = false;
        _linkUnlockBox = AddOption(optionBody, "同时解锁联机", "勾选：下载 + 联机；不勾选：仅解锁下载", true);
        _linkUnlockBox.Checked = true;
        _stopPclBox = AddOption(optionBody, "自动退出 PCL", "补丁/还原前结束正在运行的 PCL", true);
        _stopPclBox.Checked = true;
        _forceBox = AddOption(optionBody, "允许未知版本", "哈希与结构指纹都不匹配时才需要：跳过校验强制写入", false);
        var forceHint = (TextBlock)((Grid)_forceBox.Parent).Children[1];
        _noUpdateBox.Change += (_, isChecked) =>
        {
            _steps?.SetNoUpdate(isChecked);
            _ = RefreshStepProbeAsync(CurrentSource());
        };
        _linkUnlockBox.Change += (_, isChecked) =>
        {
            _steps?.SetLink(isChecked);
            _ = RefreshStepProbeAsync(CurrentSource());
        };
        _forceBox.Change += (_, isChecked) =>
        {
            forceHint.Foreground = PclUi.Frozen(isChecked ? PclUi.Amber : PclUi.Muted);
            RefreshHashBadge();
        };

        // Result banner ---------------------------------------------------
        _resultBanner = new Border
        {
            Visibility = Visibility.Collapsed,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var resultGrid = new Grid();
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _resultIcon = PclUi.Glyph("\uE73E", 12, PclUi.Green);
        _resultIcon.VerticalAlignment = VerticalAlignment.Top;
        _resultIcon.Margin = new Thickness(0, 1, 0, 0);
        resultGrid.Children.Add(_resultIcon);
        _resultText = PclUi.Label(string.Empty, 11.5, PclUi.Ink, null, true);
        _resultText.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(_resultText, 1);
        resultGrid.Children.Add(_resultText);

        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _resultLogLink = MakeInlineLink("查看日志", () => ShowPage(PageKind.Log));
        _resultOpenLink = MakeInlineLink("打开目录", OpenFolder_Click);
        _resultLogBlock = new TextBlock { Inlines = { _resultLogLink }, FontSize = 11.5, FontFamily = PclUi.Font };
        _resultOpenBlock = new TextBlock
        {
            Inlines = { _resultOpenLink },
            FontSize = 11.5,
            FontFamily = PclUi.Font,
            Margin = new Thickness(10, 0, 0, 0)
        };
        links.Children.Add(_resultLogBlock);
        links.Children.Add(_resultOpenBlock);
        Grid.SetColumn(links, 2);
        resultGrid.Children.Add(links);
        _resultBanner.Child = resultGrid;
        _leftStack.Children.Add(_resultBanner);

        // Action row ------------------------------------------------------
        var actions = new Grid { MinHeight = 40 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _startButton = PclUi.Button("开始补丁", Start_Click, 38, true);
        _startButton.MinWidth = 112;
        _startButton.Padding = new Thickness(18, 0, 18, 0);
        actions.Children.Add(_startButton);

        _openButton = PclUi.Button("打开目录", OpenFolder_Click, 38);
        _openButton.MinWidth = 94;
        _openButton.Opacity = 0.45;
        _openButton.IsHitTestVisible = false;
        _openButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_openButton, 1);
        actions.Children.Add(_openButton);

        _launchButton = PclUi.Button("启动补丁版", Launch_Click, 38);
        _launchButton.MinWidth = 110;
        _launchButton.Opacity = 0.45;
        _launchButton.IsHitTestVisible = false;
        _launchButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_launchButton, 2);
        actions.Children.Add(_launchButton);

        _statusPill = BuildStatusPill();
        Grid.SetColumn(_statusPill, 4);
        actions.Children.Add(_statusPill);

        // Pinned below the scrolling cards rather than scrolling with them.
        actions.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(actions, 1);
        leftColumn.Children.Add(actions);

        // Right column: steps + environment -------------------------------
        _stepsCard = PclUi.Card("补丁流程", out var stepsBody, 10);
        var stepsHead = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        stepsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _stepsCount = PclUi.Label("0/9", 11, PclUi.Muted, FontWeights.SemiBold);
        _stepsCount.ToolTip = "已应用 / 启用的步骤数；选择 PCL 后会逐项探测";
        Grid.SetColumn(_stepsCount, 1);
        stepsHead.Children.Add(_stepsCount);
        stepsBody.Children.Add(stepsHead);

        _steps = new StepPanel();
        _steps.Updated += () =>
        {
            if (_stepsCount != null) _stepsCount.Text = _steps.Completed + "/" + _steps.ActiveCount;
        };
        // Mirror the initial checkbox state: DisableSelfUpdate is off by default,
        // and the link group follows "同时解锁联机" (checked by default).
        _steps.SetLink(_linkUnlockBox.Checked == true);
        _steps.SetNoUpdate(_noUpdateBox.Checked == true);
        _steps.Reset();
        _stepsCount.Text = "0/" + _steps.ActiveCount;
        stepsBody.Children.Add(_steps);
        _rightStack.Children.Add(_stepsCard);

        _envCard = PclUi.Card("运行环境", out var envBody, 0);
        envBody.Children.Add(EnvRow("Patcher", "检测中…", PclUi.Muted, out _patcherValue));
        envBody.Children.Add(PclUi.Separator(5, 5));
        envBody.Children.Add(EnvRow("校验方式", "SHA-256 + 指纹", PclUi.Ink));
        envBody.Children.Add(PclUi.Separator(5, 5));
        envBody.Children.Add(EnvRow("网络", "无需联网", PclUi.Ink));
        _rightStack.Children.Add(_envCard);

        return root;
    }

    private PCL.MyCheckBox AddOption(StackPanel body, string title, string description, bool separator)
    {
        var row = new Grid { Height = 37 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var check = new PCL.MyCheckBox
        {
            Text = title,
            Width = 128,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (check.LabText != null)
        {
            check.LabText.FontSize = 13.5;
            check.LabText.FontFamily = PclUi.Font;
        }
        row.Children.Add(check);
        var desc = PclUi.Label(description, 12, PclUi.Muted);
        desc.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(desc, 1);
        row.Children.Add(desc);
        body.Children.Add(row);
        if (separator) body.Children.Add(PclUi.Separator(2, 2));
        return check;
    }

    private Border BuildStatusPill()
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 10, 3),
            Background = PclUi.Frozen(Color.FromRgb(0xF0, 0xF4, 0xFA)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _statusDot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = PclUi.Frozen(PclUi.Muted),
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(_statusDot);
        _statusLabel = PclUi.Label("就绪", 11.5, PclUi.Muted, FontWeights.SemiBold);
        _statusLabel.Margin = new Thickness(6, 0, 0, 0);
        content.Children.Add(_statusLabel);
        pill.Child = content;
        return pill;
    }

    private Grid EnvRow(string name, string value, Color color)
    {
        return EnvRow(name, value, color, out _);
    }

    private Grid EnvRow(string name, string value, Color color, out TextBlock valueBlock)
    {
        var row = new Grid { Height = 26 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(PclUi.Label(name, 12, PclUi.Muted));
        valueBlock = PclUi.Label(value, 12, color, FontWeights.SemiBold);
        valueBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }
}


