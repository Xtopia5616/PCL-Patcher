namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    private enum PageKind
    {
        Patch,
        Log,
        About
    }

    private enum StatusTone
    {
        Idle,
        Busy,
        Ok,
        Warn,
        Error
    }

    private enum ResultTone
    {
        Success,
        Info,
        Error
    }

    private sealed class NavItem
    {
        public PageKind Page;
        public Grid Root;
        public Border Hover;
        public Border Indicator;
        public Path Icon;
        public TextBlock Label;
        public bool Active;
    }

    private sealed class HeaderTab
    {
        public PageKind Page;
        public Border Root;
        public Path Icon;
        public TextBlock Label;
        public bool Active;
    }

    private readonly string _pclPath;
    private PclVersionCatalog _catalog = PclVersionCatalog.Empty;
    private bool _catalogLoading;

    private PCL.MyTextBox _sourceBox;
    private PCL.MyTextBox _outputBox;
    private PCL.MyComboBox _versionBox;
    private PCL.MyCheckBox _dryRunBox;
    private PCL.MyCheckBox _noUpdateBox;
    private PCL.MyCheckBox _stopPclBox;
    private PCL.MyCheckBox _forceBox;
    private PCL.MyButton _startButton;
    private PCL.MyButton _openButton;
    private PCL.MyButton _launchButton;
    private PCL.MyButton _restoreButton;
    private PCL.MyButton _browseButton;
    private PCL.MyButton _detectButton;
    private TextBlock _backupHint;
    private TextBlock _versionPillLabel;
    private bool _restoreAvailable;
    private bool _lastBackupKnown;
    private string _lastBackupPath;

    private Border _startPill;

    private Border _sourceStrip;
    private Path _sourceIcon;
    private TextBlock _sourceMeta;
    private Border _sourceBadge;
    private TextBlock _sourceBadgeLabel;

    private StepPanel _steps;
    private TextBlock _stepsCount;
    private TextBlock _patcherValue;
    private PCL.MyCard _stepsCard;
    private PCL.MyCard _envCard;
    private StackPanel _leftStack;
    private StackPanel _rightStack;
    private ColumnDefinition _rightColumn;
    private Grid _patchRoot;

    private Border _statusPill;
    private Border _statusDot;
    private TextBlock _statusLabel;

    private Border _resultBanner;
    private TextBlock _resultIcon;
    private TextBlock _resultText;
    private Hyperlink _resultLogLink;
    private Hyperlink _resultOpenLink;
    private TextBlock _resultLogBlock;
    private TextBlock _resultOpenBlock;

    private Grid _activityHost;
    private Border _activityBar;
    private Grid _dropOverlay;
    private TextBlock _dropSub;

    private Dictionary<PageKind, FrameworkElement> _pages;
    private PageKind _page;
    private NavItem _navPatch;
    private NavItem _navLog;
    private NavItem _navAbout;
    private StackPanel _headerTabs;
    private readonly List<HeaderTab> _headerTabItems = new List<HeaderTab>();

    private readonly AppSettings _settings = AppSettings.Load();
    private bool _busy;
    private bool _detecting;
    private CancellationTokenSource _detectCts;
    private string _detectNote;
    private bool _compact;
    private bool _suppressText;
    private bool _noOp;
    private bool _writeDenied;
    private bool _openReady;
    private bool _launchReady;
    private Process _runningProcess;
    private string _patcherPath;
    private readonly List<string> _patcherPrefix = new List<string>();
    private string _patcherMode;
    private string _lastAutoOutput;
    private string _lastHash;
    private string _lastHashPath;
    private string _lastBackupHash;
    private string _lastVersion;
    private double _lastSize;
    private bool _lastKnown;
    private int _infoGeneration;

    public MainWindow(string pclPath)
    {
        _pclPath = pclPath;
        Title = "Plain Craft Launcher (PCL) Patcher（第三方）";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(980, work.Width - 16);
        Height = Math.Min(700, work.Height - 16);
        MinWidth = Math.Min(860, Width);
        MinHeight = Math.Min(520, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        Background = PclUi.PageBrush;
        FontFamily = PclUi.Font;
        Icon = LoadIcon("PCLPatcher-256.png");
        Content = BuildShell();
        Opacity = 0;
        PclUi.EnableRoundedCorners(this);

        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;

        AllowDrop = true;
        PreviewDragEnter += OnPreviewDragOver;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDragLeave += (_, _) => HideDropOverlay();
        PreviewDrop += OnPreviewDrop;
        TaskbarItemInfo = new TaskbarItemInfo();
    }

    private static ImageSource LoadIcon(string fileName)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri("pack://application:,,,/Assets/" + fileName, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
