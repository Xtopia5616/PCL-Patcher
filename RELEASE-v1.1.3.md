# PCL Patcher 1.1.3

## 启动速度

- **窗口先出现，初始化后置**：`OnLoaded` 里原本同步执行的 patcher 解析与源文件填充，改为在首帧之后以 `DispatcherPriority.Background` 执行。窗口不再等到 PCL 副本、主题、36MB 内嵌 patcher 都处理完才显示。
- **日志页 / 关于页按需构建**：启动时只构造“补丁”页，Log/About 在第一次导航时创建；补丁或还原开始前会确保日志页已存在（`EnsureLogPage`），因此实时日志仍然可用。
- **显式路径优先**：`PclPatcher.Gui.exe X:\path\PCL.exe` 以前可能被“上次使用的路径”抢走（定位评分更高），现在命令行给出的有效 PCL 路径直接作为源文件，不再悄悄打开另一个 PCL。

## 动效

- `PclDialog` 的警告 / 确认框：淡入 + 轻微缩放 + 上浮落位；关闭时先淡出再真正关闭，并有 300ms 兜底，动画被打断时不会把弹窗卡在屏幕上。
- “补丁 / 日志 / 关于”页面切换：按方向左右滑入 + 淡入（190ms）。
- 结果横幅：淡入 + 上浮；拖放文件提示层：淡入 / 淡出；查找 PCL 窗口：淡入；免责声明窗口：淡入 + 轻微缩放。
- 补丁选项勾选：整行背景做一次很淡的蓝色 tint 过渡。
- 所有动画遵循 Windows 的“显示动画”设置（`SystemParameters.ClientAreaAnimation`）；关闭系统动画时直接到最终状态。

## 兼容性

- **补丁步骤与 IL 改写没有变化**：`Profiles.cs` 未改动，仍支持 PCL 2.99.0.0+ 的四个已收录构建；对相同输入，补丁后的字节与 1.1.2 一致。
- 命令行参数、退出码、`--profiles` / `--probe` / `--identify` 的机器可读格式未变。
- 唯一的行为变化是 GUI 启动路径与动画，不影响源文件选择之外的补丁结果。

## 升级方式

直接替换两个 exe 即可：

```powershell
# 先退出 PCL，然后照旧
.\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe"
```

## 产物

| 文件 | 说明 | SHA-256 |
|---|---|---|
| `PclPatcher.exe` | 自包含命令行补丁器（win-x64 single-file），约 36.5 MB | `cddaece141117c3cd5de70b13f942ba78c722a00137db7a9ddb8c9ee3f8204c8` |
| `PclPatcher.Gui.Net48.exe` | 单文件 GUI（.NET Framework 4.8），内嵌 1.1.3 补丁器与主题，约 38.3 MB | `22915ab2cf36baf5918203ce1fa56d2c4d6b4b3cf583d46648f57b504ee19895` |

发布包**不包含** `Plain Craft Launcher 2.exe`、PCL 的嵌入 DLL 或 PCL 本体，也不分发补丁后的启动器。

## 验证

- 解决方案 Release 构建：0 警告、0 错误；两个产物均为 `1.1.3.0`。
- 无提权 GUI 冒烟：窗口启动、显式路径选中测试副本、补丁/日志/关于页面切换、按需构建页面均正常。
- Patcher 回归未改动：`--profiles`、`--identify` 输出与 1.1.2 一致，启动横幅版本为 `1.1.3`。
- 动画在无提权构建中编译并运行；弹窗关闭路径带 300ms 兜底定时器，避免动画异常时阻塞 `ShowDialog`。

细节见 [PCL-PATCHER.md](PCL-PATCHER.md) 与 [README.md](README.md)。
