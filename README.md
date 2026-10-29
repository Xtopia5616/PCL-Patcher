# PCL Patcher

Plain Craft Launcher (PCL) 的第三方 IL 补丁工具。仓库只保留两个可交付形态：

- **GUI** — `src/PclPatcher.Gui.Net48`：.NET Framework 4.8 WPF，复用本机 PCL 的控件与主题；
- **Patcher** — `src/PclPatcher`：.NET 10 命令行工具，用 dnlib 完成实际的补丁。

作者：**Xtopia**（开发协助：DeepSeek）
许可证：[LICENSE](LICENSE)（仅限个人学习研究） · 免责声明：[DISCLAIMER.md](DISCLAIMER.md) ·
第三方说明：[NOTICE.md](NOTICE.md)

> 本工具只在本机合法持有的 PCL 副本上做技术验证：默认原地替换并自动生成 `.bak`，
> 可随时还原；不修改 PCL 官方发布，也不分发修改版。补丁后的 PCL 属于 PCL 的
> 修改版，请勿再分发。

## 下载

预编译产物见 [Releases](https://github.com/Xtopia5616/PCL-Patcher/releases)：

| 文件 | 说明 |
|---|---|
| `PclPatcher.Gui.Net48.exe` | 单文件 GUI（.NET Framework 4.8），内嵌补丁器，双击即用 |
| `PclPatcher.exe` | 自包含命令行补丁器（win-x64 single-file），无需预装 .NET |

GUI 运行需要本机已安装 PCL。仓库源码本身不包含 PCL 本体，构建说明见下文。

## 目录结构

```text
patcher-lab/
├─ src/
│  ├─ PclPatcher/                   命令行补丁器（net10.0）
│  │  ├─ Program.cs                 入口、参数解析、主流程
│  │  ├─ Profiles.cs                已知版本、SHA-256 与方法指纹
│  │  ├─ Probes.cs                  PatchPlan 能力探测（不改模块）
│  │  ├─ Patches.Download.cs        下载页与 UI 入口补丁
│  │  ├─ Patches.Modpack.cs         整合包安装、加载器、自更新
│  │  ├─ FileOperations.cs          备份、还原、原子替换、结束 PCL
│  │  └─ IlHelpers.cs               IL 校验、指纹与通用工具
│  └─ PclPatcher.Gui.Net48/         单文件 GUI（net48 WPF）
│     ├─ App.xaml(.cs)              启动、PCL 程序集 / 字体 / 主题加载
│     ├─ MainWindow.cs              窗口字段与构造函数
│     ├─ Views/                     补丁页、日志页、关于页等分页代码
│     ├─ Controls/                  PCL 风格控件封装、日志面板、自绘对话框
│     ├─ Common/                    版本目录解析（读取 `--profiles`）
│     └─ Properties/AssemblyInfo.cs
├─ assets/icon/                     图标源文件、生成脚本
├─ build.ps1                        一键构建
├─ PclPatcher.sln                   解决方案（两个项目）
├─ Directory.Build.props            项目公共元数据
├─ Directory.Packages.props         中央包版本
├─ PCL-PATCHER.md                   补丁原理、IL 细节与实测记录
├─ LICENSE / DISCLAIMER.md / NOTICE.md / THIRD-PARTY-NOTICES.md
└─ dist/                            构建产物（已 gitignore）
```

GUI 的 `Assets` 不单独存图：图标统一来自 `assets/icon/`，通过链接资源打包。

## 环境要求

| 用途 | 要求 |
|---|---|
| 构建 | Windows 10/11 x64、.NET SDK 10.0.400+（`global.json` 固定 10.0.403） |
| GUI 编译 | 本机安装 PCL，默认 `D:\Minecraft\Plain Craft Launcher 2.exe` |
| GUI 运行 | .NET Framework 4.8（Windows 10/11 自带） |
| 补丁目标 | PCL 2.99.0.1+（见 `Profiles.cs`） |

PCL 只在编译期提供 `Plain Craft Launcher 2` 程序集引用，以及运行期加载控件 /
字体；**不会**被打进发布包。

## 构建

```powershell
pwsh .\build.ps1

# PCL 不在默认路径时
pwsh .\build.ps1 -PclLauncherPath "D:\Games\Plain Craft Launcher 2.exe"
```

产物：

| 文件 | 说明 |
|---|---|
| `dist/PclPatcher.exe` | 约 36 MB，自包含单文件，完整补丁工具 |
| `dist/PclPatcher.Gui.Net48.exe` | 约 38 MB，单文件 GUI，内嵌 patcher 与主题 |

GUI 编译时需要 `dist/PclPatcher.exe` 作为内嵌 payload。`build.ps1` 会先发布
patcher；直接 `dotnet build` GUI 项目时，csproj 也会自动补一次 publish。

也可以单独构建：

```powershell
dotnet build .\src\PclPatcher\PclPatcher.csproj -c Release
dotnet build .\src\PclPatcher.Gui.Net48\PclPatcher.Gui.Net48.csproj -c Release
```

## 使用

GUI：

1. 选择或确认 PCL 主程序；
2. 输出文件默认就是源文件（原地补丁）；
3. 点“开始补丁”，先结束正在运行的 PCL，再自动备份为 `.bak` 并原子替换；
4. 随时用“还原备份”恢复原版。

命令行：

```powershell
# 原地补丁，生成 PCL.exe.bak
.\dist\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe"

# 另存为副本，源文件不变
.\dist\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe" ".\PCL-patched.exe"

# 只探测，不写文件
.\dist\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe" --dry-run

# 还原
.\dist\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe" --restore
```

| 参数 | 作用 |
|---|---|
| `--dry-run` | 应用并校验，但不写文件 |
| `--restore` | 从 `<源文件>.bak` 还原 |
| `--force` | 绕过未知哈希 / 指纹校验 |
| `--allow-partial` | 缺失 required 步骤时继续（仅用于新版本探测） |
| `--no-update` | 把 `ModSecret.UpdateStart` 改成 `ret`，禁用自更新 |
| `--no-stop` | 不自动结束正在运行的 PCL |
| `--fingerprints` | 打印关键方法指纹 |
| `--profiles` | 输出机器可读的受支持版本列表（GUI 使用，无需源文件） |

## 补丁步骤

| 步骤 | 作用 |
|---|---|
| `DownloadLeftCtor` | 恢复“原版游戏”入口与默认子页 |
| `ModifyCard` | 恢复版本修改页的 MC 卡片 |
| `ExtraUiEntries` | 恢复更新提示“下载”、Mod 禁用页、启动页空状态、资源详情安装、导入整合包 |
| `ModpackInstall` | 重建七路 `switch` 与无参入口的文件选择 |
| `LoaderInit` | 重建真实的 `DlClientListLoader` |
| `DisableSelfUpdate` | 可选：`--no-update` 时 stub 掉自更新 |

每一步都有 `Probe`（不修改模块）、`Apply`、`Verify`；写盘前做全模块 IL 校验，
写盘后重新加载再校验。已打补丁的文件再次运行会直接返回 `already`。

## 版本适配

受支持版本表在 `src/PclPatcher/Profiles.cs`：`Version + SHA-256 + 7 个方法指纹`。
新增版本只需加一条 profile，GUI 会通过 `PclPatcher --profiles` 自动跟随，无需改 UI。

未知版本默认拒绝写入；可先用 `--dry-run` 观察每个步骤的 `Ready / Already /
Missing`，或用 `--force` 强制尝试（可能产出不可运行的文件）。

更完整的 IL 分析、控制流重建与实测结论见 [PCL-PATCHER.md](PCL-PATCHER.md)。

## 许可与不分发声明

- 本项目代码采用 [LICENSE](LICENSE)：仅限个人学习研究，禁止商业使用与再分发。
- 完整风险提示见 [DISCLAIMER.md](DISCLAIMER.md)。
- PCL 控件、主题、字体与 `PclTheme.xaml` 的权利归龙腾猫跃 / PCL 项目，见
  [NOTICE.md](NOTICE.md)；第三方许可证全文见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- 发布包**不包含** `Plain Craft Launcher 2.exe`、PCL 的嵌入 DLL 或 PCL 本体。
