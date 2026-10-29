namespace PclPatcher.Gui.Net48;

internal enum StepState
{
    Pending,
    Active,
    Done,
    Already,
    Skip,
    Error,
    Disabled
}

internal sealed class StepRow
{
    public string Key;
    public string Note;
    public Border Root;
    public Border Circle;
    public TextBlock Glyph;
    public TextBlock NameText;
    public TextBlock NoteText;
    public SolidColorBrush CircleBrush;
    public SolidColorBrush RowBrush;
    public StepState State;
}

/// <summary>
/// Mirrors the patcher's PatchPlan as a PCL-style status list. It is driven by
/// the same stdout the log page shows, so it never claims a step the patcher did
/// not actually run.
/// </summary>
internal sealed class StepPanel : StackPanel
{
    private readonly List<StepRow> _rows = new List<StepRow>();
    private bool _noUpdate;

    public event Action Updated;

    public StepPanel()
    {
        Background = Brushes.Transparent;
        Add("DownloadLeftCtor", "恢复下载页入口", "PageDownloadLeft..ctor");
        Add("ModifyCard", "恢复版本卡片", "CardMinecraft 可见");
        Add("ExtraUiEntries", "扩展 UI 入口", "更新提示 / Mod / 导入");
        Add("ModpackInstall", "重建整合包安装", "七路 switch + 文件选择");
        Add("LoaderInit", "恢复加载器", "DlClientListLoader");
        Add("DisableSelfUpdate", "禁用自更新", "ModSecret.UpdateStart");
    }

    public int Completed
    {
        get
        {
            var n = 0;
            foreach (var row in _rows)
                if (row.State == StepState.Done || row.State == StepState.Already) n++;
            return n;
        }
    }

    public int ActiveCount
    {
        get
        {
            var n = 0;
            foreach (var row in _rows)
                if (row.State != StepState.Disabled) n++;
            return n;
        }
    }

    public void SetNoUpdate(bool enabled)
    {
        _noUpdate = enabled;
        var row = _rows[_rows.Count - 1];
        if (row.State == StepState.Disabled && enabled) Set(row, StepState.Pending, false);
        else if (!enabled && row.State != StepState.Active) Set(row, StepState.Disabled, false);
        Updated?.Invoke();
    }

    public void Reset()
    {
        foreach (var row in _rows)
        {
            var disabled = row.Key == "DisableSelfUpdate" && !_noUpdate;
            Set(row, disabled ? StepState.Disabled : StepState.Pending, false);
        }
        Updated?.Invoke();
    }

    public void ActivateFirst()
    {
        foreach (var row in _rows)
        {
            if (row.State != StepState.Pending) continue;
            Set(row, StepState.Active);
            break;
        }
        Updated?.Invoke();
    }

    public void Complete()
    {
        foreach (var row in _rows)
        {
            if (row.State == StepState.Active || row.State == StepState.Pending)
                Set(row, StepState.Done, false);
        }
        Updated?.Invoke();
    }

    public void Fail()
    {
        var failed = false;
        foreach (var row in _rows)
        {
            if (row.State != StepState.Active) continue;
            Set(row, StepState.Error);
            failed = true;
        }
        if (!failed)
        {
            for (var i = _rows.Count - 1; i >= 0; i--)
            {
                if (_rows[i].State is StepState.Disabled or StepState.Skip) continue;
                Set(_rows[i], StepState.Error);
                break;
            }
        }
        Updated?.Invoke();
    }

    public void Handle(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var text = line.TrimEnd();

        if (text.StartsWith("verify :", StringComparison.Ordinal) ||
            text.StartsWith("written:", StringComparison.Ordinal) ||
            text.StartsWith("dry-run:", StringComparison.Ordinal))
        {
            Complete();
            return;
        }

        if (text.StartsWith("PATCHER_ERROR", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("GUI_ERROR", StringComparison.OrdinalIgnoreCase))
        {
            Fail();
            return;
        }

        var match = Regex.Match(text, @"^(probe|patch)\s*:\s*(\S+)\s*(.*)$");
        if (!match.Success) return;

        var row = _rows.Find(r => r.Key == match.Groups[2].Value);
        if (row is null) return;

        var kind = match.Groups[1].Value;
        var detail = match.Groups[3].Value;
        if (kind == "patch")
        {
            Set(row, StepState.Done);
        }
        else if (detail.StartsWith("already patched", StringComparison.OrdinalIgnoreCase))
        {
            Set(row, StepState.Already);
        }
        else if (detail.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase))
        {
            Set(row, StepState.Skip);
        }
        else
        {
            return;
        }

        ActivateNext();
        Updated?.Invoke();
    }

    private void ActivateNext()
    {
        foreach (var row in _rows)
        {
            if (row.State != StepState.Active) continue;
            return;
        }
        foreach (var row in _rows)
        {
            if (row.State != StepState.Pending) continue;
            Set(row, StepState.Active);
            return;
        }
    }

    private void Add(string key, string name, string note)
    {
        var circleBrush = PclUi.Live(Color.FromRgb(0xEE, 0xF2, 0xF8));
        var rowBrush = PclUi.Live(Colors.Transparent);

        var circle = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = circleBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var glyph = new TextBlock
        {
            Text = (_rows.Count + 1).ToString(),
            FontFamily = PclUi.Font,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = PclUi.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.None
        };
        circle.Child = glyph;

        var nameText = PclUi.Label(name, 13, PclUi.Ink, FontWeights.SemiBold);
        var noteText = PclUi.Label(note, 11, PclUi.Muted);
        noteText.Margin = new Thickness(0, 1, 0, 0);

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(nameText);
        text.Children.Add(noteText);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(circle);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        var root = new Border
        {
            Height = 42,
            CornerRadius = new CornerRadius(4),
            Background = rowBrush,
            Child = content,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = note
        };
        Children.Add(root);

        var row = new StepRow
        {
            Key = key,
            Note = note,
            Root = root,
            Circle = circle,
            Glyph = glyph,
            NameText = nameText,
            NoteText = noteText,
            CircleBrush = circleBrush,
            RowBrush = rowBrush,
            State = StepState.Pending
        };
        _rows.Add(row);
        Set(row, StepState.Pending, false);
    }

    private static void Set(StepRow row, StepState state, bool animate = true)
    {
        if (row.State == state && row.Glyph.Text.Length > 0) return;
        row.State = state;

        Color circle;
        Color ink;
        string glyph;
        var useIcon = true;
        var iconSize = 9.0;

        switch (state)
        {
            case StepState.Active:
                circle = PclUi.Accent;
                ink = Colors.White;
                glyph = (row.Root.Parent as Panel)?.Children.IndexOf(row.Root) is int i ? (i + 1).ToString() : "•";
                useIcon = false;
                break;
            case StepState.Done:
                circle = PclUi.Green;
                ink = Colors.White;
                glyph = "\uE73E";
                break;
            case StepState.Already:
                circle = Color.FromRgb(0xE8, 0xF1, 0xFF);
                ink = PclUi.Accent;
                glyph = "\uE73E";
                break;
            case StepState.Skip:
                circle = Color.FromRgb(0xFF, 0xF4, 0xE5);
                ink = PclUi.Amber;
                glyph = "\uE738";
                break;
            case StepState.Error:
                circle = Color.FromRgb(0xFD, 0xEC, 0xEC);
                ink = PclUi.Red;
                glyph = "\uE783";
                break;
            case StepState.Disabled:
                circle = Color.FromRgb(0xF3, 0xF4, 0xF6);
                ink = Color.FromRgb(0xB0, 0xB5, 0xBD);
                glyph = "\uE738";
                break;
            default:
                circle = Color.FromRgb(0xEE, 0xF2, 0xF8);
                ink = PclUi.Muted;
                glyph = (row.Root.Parent as Panel)?.Children.IndexOf(row.Root) is int n ? (n + 1).ToString() : "•";
                useIcon = false;
                break;
        }

        row.Glyph.Text = glyph;
        row.Glyph.FontFamily = useIcon ? PclUi.Icons : PclUi.Font;
        row.Glyph.FontSize = useIcon ? iconSize : 10.5;
        row.Glyph.Foreground = PclUi.Frozen(ink);

        if (animate)
        {
            PclUi.AnimateColor(row.CircleBrush, circle, 160);
        }
        else
        {
            row.CircleBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            row.CircleBrush.Color = circle;
        }

        var rowColor = state switch
        {
            StepState.Active => Color.FromRgb(0xEA, 0xF2, 0xFE),
            StepState.Error => Color.FromRgb(0xFF, 0xF7, 0xF7),
            _ => Colors.Transparent
        };
        if (animate) PclUi.AnimateColor(row.RowBrush, rowColor, 160);
        else
        {
            row.RowBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            row.RowBrush.Color = rowColor;
        }

        row.NameText.Foreground = PclUi.Frozen(state switch
        {
            StepState.Active => PclUi.Accent,
            StepState.Error => PclUi.Red,
            StepState.Disabled => Color.FromRgb(0xA6, 0xA6, 0xA6),
            _ => PclUi.Ink
        });

        var suffix = state switch
        {
            StepState.Skip => " · 已跳过",
            StepState.Disabled => " · 未启用",
            StepState.Error => " · 失败",
            StepState.Already => " · 已是补丁状态",
            _ => string.Empty
        };
        row.NoteText.Text = row.Note + suffix;
        row.Root.Opacity = state == StepState.Disabled ? 0.62 : 1.0;

        if (state == StepState.Active && animate)
        {
            row.Root.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            row.Root.RenderTransform = scale;
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        }
    }
}
