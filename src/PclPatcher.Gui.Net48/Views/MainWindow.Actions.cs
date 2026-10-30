namespace PclPatcher.Gui.Net48;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Run
    // ------------------------------------------------------------------

    private async void Start_Click()
    {
        if (_busy || _detecting) return;

        var source = _sourceBox.Text?.Trim().Trim('"') ?? string.Empty;
        var output = _outputBox.Text?.Trim().Trim('"') ?? string.Empty;

        if (!File.Exists(source))
        {
            PclDialog.Show(this, "无法开始", "请选择有效的 PCL 主程序。", "知道了", tone: DialogTone.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(output)) output = source;

        string fullSource;
        string fullOutput;
        try
        {
            fullSource = IOPath.GetFullPath(source);
            fullOutput = IOPath.GetFullPath(output);
        }
        catch (Exception ex)
        {
            PclDialog.Show(this, "路径无效", ex.Message, "知道了", tone: DialogTone.Error);
            return;
        }

        var inPlace = string.Equals(fullSource, fullOutput, StringComparison.OrdinalIgnoreCase);
        if (inPlace) fullOutput = fullSource;
        if (string.IsNullOrEmpty(IOPath.GetDirectoryName(fullOutput)))
        {
            PclDialog.Show(this, "无法开始", "输出文件需要包含完整的目录路径。", "知道了", tone: DialogTone.Warning);
            return;
        }

        var hash = _lastHash;
        if (hash is null || !string.Equals(_lastHashPath, fullSource, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                hash = await Task.Run(() => Sha256(fullSource));
            }
            catch (Exception ex)
            {
                PclDialog.Show(this, "读取失败", "无法读取源文件：" + ex.Message, "知道了", tone: DialogTone.Error);
                return;
            }
        }

        // A backup that matches a supported build means the source itself is our
        // patched copy; that is expected when patching in place a second time.
        // If the profile list could not be read, the GUI defers to the patcher,
        // which repeats the SHA-256 / fingerprint checks before writing.
        var backup = BackupPath(fullSource);
        var hasOriginalBackup = false;
        var catalogReady = _catalog.IsLoaded;
        var sourceKnown = catalogReady && _catalog.Matches(hash);
        if (catalogReady && !sourceKnown && File.Exists(backup))
        {
            try
            {
                hasOriginalBackup = _catalog.Matches(await Task.Run(() => Sha256(backup)));
            }
            catch
            {
                hasOriginalBackup = false;
            }
        }

        if (catalogReady && !sourceKnown && !hasOriginalBackup && _forceBox.Checked != true)
        {
            // The patcher may still accept this build by structural fingerprints
            // (unlisted snapshot rebuild); only ask for the escape hatch when the
            // identify pass also came back empty.
            var identified = await IdentifyAsync(fullSource);
            if (string.IsNullOrEmpty(identified.Structural))
            {
                var closest = string.IsNullOrEmpty(identified.Closest)
                    ? string.Empty
                    : "\n\n最接近的已收录构建：" + identified.Closest + "（锚点指纹不一致）。";
                PclDialog.Show(this,
                    "版本不匹配",
                    "当前文件不在已支持的版本范围（" + _catalog.SupportDescription + "）内，也没有可用的原版备份。" +
                    closest + "\n请选择受支持的 PCL 版本，或先在“补丁选项”中勾选“允许未知版本”。",
                    "知道了",
                    tone: DialogTone.Warning);
                return;
            }

            _structuralLabel = identified.Structural;
            SyncVersionSelection();
        }

        if (inPlace && _dryRunBox.Checked != true && !IsAdministrator())
        {
            var elevate = PclDialog.Show(this,
                "需要管理员权限",
                "原地补丁需要写入 PCL 所在目录（" + IOPath.GetDirectoryName(fullSource) + "）。\n" +
                "建议以管理员身份重启本工具，会弹出 UAC 提示。\n\n" +
                "源文件尚未修改。",
                "以管理员身份重启", "仍然继续", DialogTone.Question);
            if (elevate && RestartAsAdministrator()) return;
        }

        if (inPlace && _dryRunBox.Checked != true)
        {
            var alreadyPatched = hasOriginalBackup;
            var scope = _linkUnlockBox.Checked == true ? "下载 + 联机" : "仅解锁下载";
            var backupText = File.Exists(backup)
                ? "已有备份：\n" + backup + "\n（继续使用该备份，不会覆盖）"
                : "将自动备份为：\n" + backup;
            var message = alreadyPatched
                ? "源文件已经是补丁状态。\n\n" +
                  "重新补丁会先还原原版，再按当前选项重新应用，旧补丁不会叠加。\n" +
                  "改过“同时解锁联机 / 禁用更新”后，直接再点开始补丁即可。\n\n" + backupText
                : "补丁会直接替换源文件：\n" + fullSource + "\n\n补丁范围：" + scope + "\n\n" + backupText +
                  "\n\n补丁后可用“还原备份”恢复原版。";
            if (_forceBox.Checked == true)
                message += "\n\n⚠ 已勾选“允许未知版本”：本次不校验 SHA-256 与方法指纹，" +
                           "写出的 PCL 可能无法启动。";
            var confirm = PclDialog.Show(this,
                alreadyPatched ? "重新补丁" : "替换源文件",
                message, alreadyPatched ? "还原并重新补丁" : "继续", "取消", DialogTone.Question);
            if (!confirm) return;
        }
        else if (!inPlace && File.Exists(fullOutput) && _dryRunBox.Checked != true)
        {
            var overwrite = PclDialog.Show(this,
                "覆盖输出文件",
                "输出文件已经存在：\n" + fullOutput + "\n\n继续会覆盖这个文件。",
                "覆盖", "取消", DialogTone.Question);
            if (!overwrite) return;
        }

        _patcherPath = FindPatcher();
        if (_patcherPath is null)
        {
            PclDialog.Show(this,
                "缺少 Patcher",
                "没有找到 PclPatcher.exe。请把它放在 GUI 同一目录，或先运行 build.ps1。",
                "知道了", tone: DialogTone.Error);
            return;
        }

        // Omit the output argument for in-place mode so the patcher creates and
        // reuses its .bak backup instead of treating the source as a save-as target.
        var args = new List<string> { fullSource };
        if (!inPlace) args.Add(fullOutput);
        if (_noUpdateBox.Checked == true) args.Add("--no-update");
        if (_linkUnlockBox.Checked != true) args.Add("--download-only");
        if (_stopPclBox.Checked != true) args.Add("--no-stop");
        if (_forceBox.Checked == true) args.Add("--force");
        if (_dryRunBox.Checked == true) args.Add("--dry-run");

        await RunPatcherAsync(args, inPlace ? fullSource : fullOutput);
    }

    private async void RestoreBackup_Click()
    {
        if (_busy) return;

        var source = _sourceBox.Text?.Trim().Trim('"') ?? string.Empty;
        if (!File.Exists(source))
        {
            PclDialog.Show(this, "无法还原", "请先选择有效的 PCL 主程序。", "知道了", tone: DialogTone.Warning);
            return;
        }

        string fullSource;
        try
        {
            fullSource = IOPath.GetFullPath(source);
        }
        catch (Exception ex)
        {
            PclDialog.Show(this, "路径无效", ex.Message, "知道了", tone: DialogTone.Error);
            return;
        }

        var backup = BackupPath(fullSource);
        if (!File.Exists(backup))
        {
            PclDialog.Show(this, "没有备份",
                "没有找到备份文件：\n" + backup + "\n\n只有原地补丁过的文件才能还原。",
                "知道了", tone: DialogTone.Warning);
            return;
        }

        if (!IsAdministrator())
        {
            var elevate = PclDialog.Show(this,
                "需要管理员权限",
                "还原备份需要写入 PCL 所在目录（" + IOPath.GetDirectoryName(fullSource) + "）。\n" +
                "建议以管理员身份重启本工具，会弹出 UAC 提示。",
                "以管理员身份重启", "仍然继续", DialogTone.Question);
            if (elevate && RestartAsAdministrator()) return;
        }

        var confirm = PclDialog.Show(this,
            "还原备份",
            "将用备份覆盖当前 PCL 主程序：\n" + fullSource + "\n\n备份：" + backup +
            "\n\n当前补丁会被移除，源文件恢复为原版。",
            "还原", "取消", DialogTone.Question);
        if (!confirm) return;

        _patcherPath = FindPatcher();
        if (_patcherPath is null)
        {
            PclDialog.Show(this,
                "缺少 Patcher",
                "没有找到 PclPatcher.exe。请把它放在 GUI 同一目录，或先运行 build.ps1。",
                "知道了", tone: DialogTone.Error);
            return;
        }

        // Point the output/open/launch actions at the restored source file.
        _outputBox.Text = fullSource;
        _outputBox.ToolTip = fullSource;
        _lastAutoOutput = fullSource;
        ShowPathTail(_outputBox);

        var restoreArgs = new List<string> { fullSource, "--restore" };
        if (_stopPclBox.Checked != true) restoreArgs.Add("--no-stop");
        await RunPatcherAsync(restoreArgs, fullSource, restore: true);
    }

    // target is passed explicitly: args also carry flags (--restore/--dry-run/
    // --no-stop/...), so args[1] is not necessarily an output path. Treating
    // "--restore" as the output made the post-restore refresh check a file
    // named "--restore" and report "文件不存在" right after a successful restore.
    private async Task RunPatcherAsync(List<string> args, string target, bool restore = false)
    {
        // A probe that is still running must not overwrite the live step states.
        _probeGeneration++;
        _noOp = false;
        _writeDenied = false;
        _downgradeBlocked = false;
        SetBusy(true);
        _resultBanner.Visibility = Visibility.Collapsed;
        _log.Clear();
        _log.SetState(LogState.Running);
        if (!restore)
        {
            _steps.SetLink(_linkUnlockBox.Checked == true);
            _steps.SetNoUpdate(_noUpdateBox.Checked == true);
            _steps.Reset();
            _steps.ActivateFirst();
        }
        else
        {
            // A restore removes the patch, so the step list must not keep claiming
            // that the five patch steps are complete.
            _steps.Reset();
        }
        SetStatus(restore ? "正在还原备份…" : "正在运行 patcher…", StatusTone.Busy);
        var commandArgs = new List<string>(_patcherPrefix);
        commandArgs.AddRange(args);
        var commandName = _patcherMode == "dotnet" ? "dotnet" : Quote(_patcherPath);
        _log.Append("$ " + commandName + " " + string.Join(" ", commandArgs.Select(Quote)));
        _log.Append(string.Empty);

        var psi = new ProcessStartInfo(_patcherPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            Arguments = string.Join(" ", commandArgs.Select(Quote))
        };

        // Re-read the per-step states after a successful run: a patch turns every
        // row into "已应用", a restore turns them back into "待应用". A failed run
        // keeps its red rows, so the failure stays visible in the panel.
        var refreshProbe = false;
        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _runningProcess = process;
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Run(() => process.WaitForExit());

            if (process.ExitCode == 0)
            {
                refreshProbe = true;
                if (!restore) _steps.Complete();
                _log.SetState(LogState.Done);
                if (restore)
                {
                    SetStatus("还原完成", StatusTone.Ok);
                    _openReady = true;
                    _launchReady = false;
                    _patchedOutputPath = null;
                    UpdateActionButtons();
                    ShowResult(ResultTone.Success,
                        "已还原原始文件 · " + IOPath.GetFileName(target), false, true);
                    await UpdateSourceInfoAsync(target);
                }
                else if (_dryRunBox.Checked == true)
                {
                    SetStatus("探测完成", StatusTone.Ok);
                    ShowResult(ResultTone.Info, "探测通过，未写出文件。", false, false);
                    await UpdateSourceInfoAsync(args[0]);
                }
                else if (_noOp && File.Exists(target))
                {
                    SetStatus("已是补丁状态", StatusTone.Ok);
                    _openReady = true;
                    _launchReady = true;
                    _patchedOutputPath = target;
                    UpdateActionButtons();
                    ShowResult(ResultTone.Info, "源文件已经是补丁状态，未重复写入。", false, true);
                    await UpdateSourceInfoAsync(args[0]);
                }
                else if (File.Exists(target))
                {
                    var size = new FileInfo(target).Length / 1024d / 1024d;
                    SetStatus("补丁完成", StatusTone.Ok);
                    _openReady = true;
                    _launchReady = true;
                    _patchedOutputPath = target;
                    UpdateActionButtons();
                    var backupNote = File.Exists(BackupPath(target)) ? " · 已备份 .bak" : string.Empty;
                    ShowResult(ResultTone.Success,
                        $"补丁完成 · {IOPath.GetFileName(target)}（{size:F1} MB）{backupNote}", false, true);
                    await UpdateSourceInfoAsync(args[0]);
                }
                else
                {
                    refreshProbe = false;
                    SetStatus("输出文件未生成", StatusTone.Error);
                    ShowResult(ResultTone.Error, "patcher 返回成功，但没有找到输出文件。", true, false);
                }
            }
            else
            {
                if (!restore) _steps.Fail();
                _log.SetState(LogState.Error);
                if (_writeDenied)
                {
                    SetStatus("无法写入 PCL 目录", StatusTone.Error);
                    var retry = PclDialog.Show(this,
                        "无法写入 PCL 目录",
                        "补丁没有写入源文件。\n\n" +
                        "当前进程没有该目录的写权限，常见于安全软件拦截或受限运行环境（沙箱）。\n\n" +
                        "可以：\n" +
                        "1. 以管理员身份重启本工具；\n" +
                        "2. 把 PCL 复制到可写目录后再补丁；\n" +
                        "3. 把 PclPatcher 加入安全软件信任。\n\n" +
                        "源文件未被修改。",
                        "以管理员身份重启", "取消", DialogTone.Error);
                    if (retry && RestartAsAdministrator()) return;
                    ShowResult(ResultTone.Error, "补丁没有写入：当前环境没有该目录的写权限。", true, false);
                }
                else if (_downgradeBlocked && !restore)
                {
                    // The patcher refuses to rebuild from an older .bak unless the
                    // downgrade is explicit; --force never implies it. Ask here
                    // instead of making the user discover --allow-downgrade.
                    SetStatus("会降级到备份版本", StatusTone.Warn);
                    var allow = PclDialog.Show(this,
                        "会降级到备份版本",
                        "源文件已经是补丁状态，但同目录的 .bak 是另一个（更旧的）PCL 构建。\n" +
                        "从该备份重新补丁会把启动器降级。\n\n" +
                        "“允许未知版本”只跳过版本校验，不会允许降级；要降级需要在这里明确同意。\n\n" +
                        "源文件尚未修改。",
                        "允许降级", "取消", DialogTone.Warning);
                    if (allow)
                    {
                        var retryArgs = new List<string>(args) { "--allow-downgrade" };
                        await RunPatcherAsync(retryArgs, target, restore);
                        return;
                    }
                    ShowResult(ResultTone.Error, "已取消：不会从更旧的备份降级重打。", true, false);
                }
                else if (process.ExitCode == 2)
                {
                    // Nothing was applied: every anchor was missing or skipped.
                    // The file on disk is unchanged, which is not a crash.
                    SetStatus("没有可应用的步骤", StatusTone.Warn);
                    ShowResult(ResultTone.Info,
                        "0 个步骤被应用（锚点缺失或全部被跳过），文件未修改。请查看日志中的 probe 行。",
                        true, false);
                }
                else
                {
                    SetStatus((restore ? "还原失败，退出码 " : "补丁失败，退出码 ") + process.ExitCode, StatusTone.Error);
                    ShowResult(ResultTone.Error,
                        restore ? "还原没有完成，请查看日志中的 PATCHER_ERROR。" : "补丁没有完成，请查看日志中的 PATCHER_ERROR。",
                        true, false);
                }
            }
        }
        catch (Exception ex)
        {
            AppendLine("GUI_ERROR: " + ex.Message);
            if (!restore) _steps.Fail();
            _log.SetState(LogState.Error);
            SetStatus("启动 patcher 失败", StatusTone.Error);
            ShowResult(ResultTone.Error, "无法启动 patcher，请查看日志。", true, false);
        }
        finally
        {
            _runningProcess = null;
            SetBusy(false);
            // Only in-place runs change the file the panel describes; a save-as
            // run leaves the source as it was, so its live “完成” rows stay.
            if (refreshProbe && SamePath(target, CurrentSource()))
                _ = RefreshStepProbeAsync(CurrentSource());
        }
    }

    private void AppendLine(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(new Action(() => AppendLine(line)));
            return;
        }
        if (line.StartsWith("already:", StringComparison.Ordinal)) _noOp = true;
        if (line.IndexOf("would downgrade the launcher", StringComparison.OrdinalIgnoreCase) >= 0)
            _downgradeBlocked = true;
        if (line.IndexOf("cannot write to", StringComparison.OrdinalIgnoreCase) >= 0 ||
            line.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            line.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0)
            _writeDenied = true;
        _log.Append(line);
        _steps.Handle(line);
    }

    private void ShowResult(ResultTone tone, string message, bool showLog, bool showOpen)
    {
        var (background, foreground, glyph) = tone switch
        {
            ResultTone.Success => (Color.FromRgb(0xE9, 0xF7, 0xEF), PclUi.Green, "\uE73E"),
            ResultTone.Error => (Color.FromRgb(0xFD, 0xEC, 0xEC), PclUi.Red, "\uE783"),
            _ => (Color.FromRgb(0xEA, 0xF2, 0xFE), PclUi.Accent, "i")
        };
        _resultBanner.Background = PclUi.Frozen(background);
        _resultIcon.Text = glyph;
        _resultIcon.FontFamily = glyph == "i" ? PclUi.Font : PclUi.Icons;
        _resultIcon.FontWeight = glyph == "i" ? FontWeights.Bold : FontWeights.Normal;
        _resultIcon.Foreground = PclUi.Frozen(foreground);
        _resultText.Text = message;
        _resultLogBlock.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
        _resultOpenBlock.Visibility = showOpen ? Visibility.Visible : Visibility.Collapsed;
        _resultBanner.Visibility = Visibility.Visible;
        PclUi.FadeIn(_resultBanner, 160);
    }

    private void SetStatus(string text, StatusTone tone)
    {
        if (_statusPill is null) return;
        var (background, foreground) = tone switch
        {
            StatusTone.Busy => (Color.FromRgb(0xE8, 0xF1, 0xFF), PclUi.Accent),
            StatusTone.Ok => (Color.FromRgb(0xE9, 0xF7, 0xEF), PclUi.Green),
            StatusTone.Warn => (Color.FromRgb(0xFF, 0xF4, 0xE5), PclUi.Amber),
            StatusTone.Error => (Color.FromRgb(0xFD, 0xEC, 0xEC), PclUi.Red),
            _ => (Color.FromRgb(0xF0, 0xF4, 0xFA), PclUi.Muted)
        };
        _statusPill.Background = PclUi.Frozen(background);
        _statusDot.Background = PclUi.Frozen(foreground);
        _statusLabel.Text = text;
        _statusLabel.Foreground = PclUi.Frozen(foreground);

        if (tone == StatusTone.Busy)
        {
            _statusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        else
        {
            _statusDot.BeginAnimation(OpacityProperty, null);
            _statusDot.Opacity = 1;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _sourceBox.IsEnabled = !busy;
        _outputBox.IsEnabled = !busy;
        _dryRunBox.IsEnabled = !busy;
        _noUpdateBox.IsEnabled = !busy;
        _linkUnlockBox.IsEnabled = !busy;
        _stopPclBox.IsEnabled = !busy;
        _forceBox.IsEnabled = !busy;
        _startButton.IsEnabled = !busy;
        if (_detectButton is not null) _detectButton.IsEnabled = !busy;
        if (_browseButton is not null) _browseButton.IsEnabled = !busy;
        if (_startPill != null)
        {
            _startPill.Opacity = busy ? 0.55 : 1.0;
            _startPill.IsHitTestVisible = !busy;
        }

        UpdateActionButtons();
        RefreshBackupState();

        SetActivity(busy);
        TaskbarItemInfo.ProgressState = busy
            ? TaskbarItemProgressState.Indeterminate
            : TaskbarItemProgressState.None;
    }

    private void UpdateActionButtons()
    {
        if (_openButton is null) return;
        var open = _openReady && !_busy;
        var launch = _launchReady && !_busy;
        _openButton.Opacity = open ? 1.0 : 0.45;
        _openButton.IsHitTestVisible = open;
        _launchButton.Opacity = launch ? 1.0 : 0.45;
        _launchButton.IsHitTestVisible = launch;
    }

    private void SetActivity(bool busy)
    {
        if (_activityHost is null) return;
        if (!busy)
        {
            _activityHost.Visibility = Visibility.Collapsed;
            _activityBar.BeginAnimation(UIElement.RenderTransformProperty, null);
            return;
        }

        _activityHost.Visibility = Visibility.Visible;
        var transform = (TranslateTransform)_activityBar.RenderTransform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-170, 980, TimeSpan.FromMilliseconds(1250))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    // ------------------------------------------------------------------
    // Update check (About page)
    // ------------------------------------------------------------------

    private async void CheckUpdates_Click()
    {
        if (_updateChecking) return;

        // No readable version means no comparison: do exactly what the
        // unknown-version copy promises and open the release page.
        if (string.IsNullOrEmpty(UpdateChecker.CurrentVersion))
        {
            OpenInBrowser(UpdateChecker.ReleasesUrl);
            SetUpdateStatus("版本未知，无法自动比较新旧；已在浏览器打开 GitHub 发布页。", PclUi.Amber);
            return;
        }

        _updateChecking = true;
        _updateButton.IsEnabled = false;
        _updateButton.Opacity = 0.55;
        SetUpdateStatus("正在检查 GitHub 上的最新版本…", PclUi.Muted);

        UpdateCheckResult result;
        try
        {
            result = await UpdateChecker.CheckAsync();
        }
        catch (Exception ex)
        {
            result = new UpdateCheckResult
            {
                State = UpdateCheckState.Failed,
                CurrentVersion = UpdateChecker.CurrentVersion,
                Error = ex.Message,
                ReleaseUrl = UpdateChecker.ReleasesUrl
            };
        }

        _updateChecking = false;
        _updateButton.IsEnabled = true;
        _updateButton.Opacity = 1.0;
        ApplyUpdateResult(result);
    }

    /// <summary>
    /// Renders one check result in the About card. A newer release also asks
    /// whether to open the release page, so the answer is not buried in a
    /// one-line status.
    /// </summary>
    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        if (result is null) return;

        switch (result.State)
        {
            case UpdateCheckState.UpToDate:
                var comparison = UpdateChecker.CompareVersions(result.CurrentVersion, result.LatestVersion);
                SetUpdateStatus(comparison > 0
                        ? "已是最新：当前 v" + result.CurrentVersion + " 比 GitHub 最新发布 v" + result.LatestVersion + " 还新（刚刚检查）。"
                        : "已是最新版本 v" + result.CurrentVersion + "（刚刚检查）。",
                    PclUi.Green);
                break;

            case UpdateCheckState.NewerVersion:
                SetUpdateStatus("发现新版本 v" + result.LatestVersion + "，当前 v" + result.CurrentVersion +
                                "（刚刚检查）；可打开发布页查看更新说明。", PclUi.Amber);
                var open = PclDialog.Show(this,
                    "发现新版本",
                    "GitHub 上已有新版本：v" + result.LatestVersion + "\n" +
                    "当前版本：v" + result.CurrentVersion + "\n\n" +
                    "是否打开发布页查看更新说明与下载？",
                    "打开发布页", "稍后", DialogTone.Info);
                if (open) OpenInBrowser(result.ReleaseUrl);
                break;

            case UpdateCheckState.UnknownVersion:
                OpenInBrowser(UpdateChecker.ReleasesUrl);
                SetUpdateStatus("版本未知，无法自动比较新旧；已在浏览器打开 GitHub 发布页。", PclUi.Amber);
                break;

            default:
                SetUpdateStatus("检查失败：" + (string.IsNullOrEmpty(result.Error) ? "未知错误" : result.Error) +
                                "。可打开发布页手动确认。", PclUi.Red);
                break;
        }
    }

    private void SetUpdateStatus(string text, Color color)
    {
        if (_updateStatus is null) return;
        _updateStatus.Text = text;
        _updateStatus.Foreground = PclUi.Frozen(color);
    }

    private static void OpenInBrowser(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // A missing browser association must not crash the About page.
        }
    }

    private void OpenFolder_Click()
    {
        if (!_openReady || _busy) return;
        var output = _outputBox.Text;
        if (File.Exists(output))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{output}\"") { UseShellExecute = true });
            return;
        }
        var dir = IOPath.GetDirectoryName(output);
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void Launch_Click()
    {
        if (!_launchReady || _busy) return;
        var output = _outputBox.Text;
        if (!File.Exists(output)) return;

        var confirm = PclDialog.Show(this,
            "请支持 Minecraft 正版",
            "启动前请确认你已购买 Minecraft 正版。\n" +
            "本工具不提供游戏本体，也不会绕过正版验证。\n\n是否继续启动补丁版 PCL？",
            "继续启动", "取消", DialogTone.Question);
        if (!confirm) return;

        try
        {
            // 本窗口默认是提权的（app.manifest: requireAdministrator），直接启动
            // output 会让 PCL 连同 Java 一起继承管理员令牌。经 explorer.exe（普通
            // 权限的 shell 进程）代启，PCL 才会回到用户自己的完整性级别。
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{output}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            PclDialog.Show(this, "无法启动", ex.Message, "知道了", tone: DialogTone.Error);
        }
    }
}
