# PCL 2.99.0.1 IL patcher：实现、实测与更多方式

作者：**Xtopia**（开发协助：DeepSeek）

> 许可证：[LICENSE](LICENSE)（仅限个人学习研究，禁止商业使用与再分发） ·
> 免责声明：[DISCLAIMER.md](DISCLAIMER.md)

目标文件：`D:\Minecraft\Plain Craft Launcher 2.exe`
版本：`2.99.0.1`，SHA-256 `0db34e19d9eeb6d496d20fabe786ec16bbd36ba6b0ccdc8d29cb7e909c114397`

> 本机副本上的研究性 PoC：默认原地替换源文件，替换前自动备份为 `.bak`，可用 `--restore` 还原。
> 不要分发修改版，也不要把来源不明的“修复版”当成可信文件。

## 0. 目标结构

PCL 不是 .NET single-file bundle，而是正常的托管 PE（.NET Framework 4.x，WPF）：

```
runtime  : v4.0.30319
machine  : I386
types    : 665
resources: 2
```

因此 dnlib 可以直接 `ModuleDefMD.Load(PCL.exe)`，不需要先抽取内嵌程序集。
`ModSecret` 里的常量是 **literal**：

```
PCL.ModSecret::RestrictMcDownload  literal=True  const=True
--- instructions referencing RestrictMcDownload ---
  count=0
```

所有使用点都被编译器折叠掉了，所以“把 True 改成 False”没有用。

## 1. PatchPlan

`PclPatcher` 现在把补丁拆成可独立验证的步骤：

| 步骤 | 作用 |
|---|---|
| `DownloadLeftCtor` | 删除 ctor 里隐藏“原版游戏”入口的 4 段 UI 调用 |
| `ModifyCard` | `CardMinecraft.Visibility`：Collapsed → Visible |
| `ExtraUiEntries` | 更新提示“下载”按钮、Mod 禁用页下载按钮、启动页空状态、资源详情安装按钮、版本选择“导入整合包” |
| `ModpackInstall` | 重建 `IL_02DA` 的七路 `switch` + 恢复无参入口的文件选择 |
| `LoaderInit` | 重建为真正的 `DlClientListLoader` |
| `DisableSelfUpdate` | 可选 `--no-update`：把 `ModSecret.UpdateStart` 直接 `ret`，防止更新器覆盖补丁 |

每个步骤都有 `Apply` + `Verify`；写盘前跑全模块 IL 校验，写盘后再重新加载校验。

每个步骤都有 `Probe + Apply + Verify`；`Probe` 不修改模块，返回：

- `Ready`：锚点齐全，可以应用；
- `AlreadyPatched`：已经是补丁后的形状，跳过（幂等）；
- `Missing`：锚点缺失，required 步骤默认中止，optional 步骤跳过。

加 `--allow-partial` 可以强制跳过缺失的 required 步骤，用于新版本探测；不建议直接写盘。

## 2. 版本适配与指纹

`Profiles` 里保存已知版本的 `Version + SHA256 + 方法指纹`。当前已知：

```
version: 2.99.0.1
sha256 : 0db34e19d9eeb6d496d20fabe786ec16bbd36ba6b0ccdc8d29cb7e909c114397
```

会校验 7 个关键方法的 IL 指纹：

```
PageDownloadLeft..ctor
PageDownloadInstall.LoaderInit
ModModpack.ModpackInstall/3
PageSelectLeft.McFolderListUI
PageDownloadResourceDetail.InitializeStackResourceVersions
PageInstanceModDisabled.BtnDownload_Loaded
PageLaunchLeft.RefreshButtonsUI
```

指纹不一致时默认中止；`--dry-run` / `--force` 下只警告并继续。
查看任意版本的指纹：

```powershell
.\dist\PclPatcher.exe "PCL.exe" --fingerprints
```

新版本可以先跑：

```powershell
.\dist\PclPatcher.exe "PCL.exe" --dry-run
```

它会打印每个步骤的 `Ready / AlreadyPatched / Missing` 和指纹对比，不会写文件。

## 3. 补丁流程

现在只有一套补丁流程，不再提供 `const` / `ui` / `minimal` 分支。一次补丁包含：

| 步骤 | 作用 |
|---|---|
| `DownloadLeftCtor` | 恢复“原版游戏”入口和默认子页 |
| `ModifyCard` | 恢复版本修改页的 MC 卡片 |
| `ExtraUiEntries` | 更新提示“下载”、Mod 禁用页、启动页空状态、资源详情安装、导入整合包 |
| `ModpackInstall` | 七路 `switch` + 无参入口文件选择 |
| `LoaderInit` | 真实 `DlClientListLoader` |

成功日志：

```
[DlClientList Mojang] 发起网络请求（GET，https://launchermeta.mojang.com/mc/game/version_manifest.json）
[ModDownload] Mojang 官方源加载耗时：437ms，可优先使用官方源
[ModLoader] 加载器 LoaderTask`2 DlClientList Mojang 状态改变：Finished
```

## 4. 关键方法体

### 4.1 `PageDownloadLeft..ctor`

2.99 末尾多出：

```
ldarg.0; callvirt get_ItemInstall; ldc.i4.2; callvirt set_Visibility
ldarg.0; callvirt get_LabResource; ldc.i4.2; callvirt set_Visibility
ldarg.0; ldc.i4.2; stfld PageID
ldarg.0; callvirt get_ItemMod; ldc.i4.1; callvirt set_Checked
```

Patcher 不再整体重建 ctor，而是按模式删除这四段，保留未来版本可能追加的初始化代码。
删除前会检查分支/异常处理边界没有指向被删区间。

### 4.2 `PageDownloadInstall.LoaderInit`

2.99 的假加载器：

```
ldstr "关闭下载"
newobj LoaderTask<...>("关闭下载", 必定抛异常的 lambda, Nothing, 2)
call PageLoaderInit<...>(..., fakeTask, ...)
```

重建为：

```
ldarg.0; call ControlInit
ldarg.0; ldarg.0; callvirt get_LoadMinecraft
ldarg.0; callvirt get_PanLoad
ldarg.0; callvirt get_PanAllBack
ldnull
ldsfld ModDownload::DlClientListLoader
ldarg.0; ldftn _Lambda$__R3-4; newobj Action<...>
ldnull
ldc.i4.1
call PageLoaderInit<...>
ret
```

`_Lambda$__R3-4` 不再按名字硬编码：改为按签名 `void(LoaderTask<string,DlClientListResult>)` 且内部调用 `LoadMinecraft_OnFinish` 来定位。

### 4.3 `ModpackInstall`：IL_02DA 的七路 switch

2.99 把原来的 `Select Case PackType` 整段替换成一条 `throw`，而且这个位置是
解析 `try` 块里所有 `leave` 的汇合点：

```
IL_02DA: ldstr "为遵守相关法律法规……"
IL_02DF: newobj Exception::.ctor(string)
IL_02E4: throw
IL_02E5: ... finally 清理 ...
```

**单纯 NOP 没用**：会直接落到 `finally`，什么都不安装。Patcher 的做法：

1. 在 `IL_02DA` 原位置放 `ldloc.s V_4`（PackType）；
2. 插入 `switch`，7 个 case 分别调用：
   - `InstallPackCurseForge`
   - `InstallPackHMCL`
   - `InstallPackMMC`
   - `InstallPackMCBBS`
   - `InstallPackModrinth`
   - `InstallPackLauncherPack`
   - `InstallPackCompress`（default）
3. 每个 case 写日志、压 `File/Archive/ArchiveBaseFolder/InstanceName/Logo`、`call`、`stloc.0`、`leave retBlock`；
4. 在 `endfinally` 后插入 `handlerEnd: nop; retBlock: ldloc.0; ret`；
5. 把外层 `Finally` 的 `HandlerEnd` 指向 `handlerEnd`，让 `ret` 位于 handler 外；
6. 用 `ReplaceTargets` 把原来指向旧 `throw` 的 `leave` 和异常处理边界改到新的 dispatch 首指令。

无参 `ModpackInstall()` 也重建为：

```
Dialogs.SelectFile("选择整合包压缩文件", False, Nothing, Nothing)
→ FirstOrDefault<string>
→ ModpackInstall(File, Nothing, Nothing)
```

`SelectFile` 的 filter 参数是 `Nullable<OneOf<...>>`，这里用 `ldloca; initobj` 传默认值（不限制扩展名），避免构造复杂的嵌套泛型。

## 5. 额外入口

| 入口 | 做法 |
|---|---|
| `ModMinecraft` 更新提示的“下载”按钮 | 找到 `"Minecraft 更新提示"` 后第一个 `ldstr ""`，改成 `"下载"` |
| `PageInstanceModDisabled.BtnDownload_Loaded` | 重建 `Settings.Get<bool>("UiHiddenPageDownload") + HiddenForceShow + ShowHidden` 条件 |
| `PageLaunchLeft.RefreshButtonsUI` | 无版本时按 `UiHiddenPageDownload` 决定 state 1/2；不再无条件 1 |
| `PageDownloadResourceDetail.InitializeStackResourceVersions` | 在版本列表循环里按 `TargetResourceType == ModPack` 选择双委托 `ToListItem(Install_Click, Save_Click, ...)` 或单委托 |
| `PageSelectLeft.McFolderListUI` | 在 `stloc.1` 后插入 `PanList.Children.Add(ItemInstall)`，恢复“导入整合包” |

## 6. 构建与运行

默认**原地替换源文件**，替换前自动备份为 `<源文件>.bak`；只有显式传入第二个路径时才写到副本。

命令行：

```powershell
cd patcher-lab
dotnet build .\src\PclPatcher\PclPatcher.csproj -c Release
.\src\PclPatcher\bin\Release\net10.0\PclPatcher.exe `
  "D:\Minecraft\Plain Craft Launcher 2.exe"
```

首次运行会先备份、再原子替换；日志里的 `backup :` 就是可还原的 `.bak`。
对已打补丁的文件重复运行只会重新校验，不会再次写入，也不会覆盖备份。

还原：

```powershell
.\src\PclPatcher\bin\Release\net10.0\PclPatcher.exe `
  "D:\Minecraft\Plain Craft Launcher 2.exe" --restore
```

另存为（源文件不变）：

```powershell
.\src\PclPatcher\bin\Release\net10.0\PclPatcher.exe `
  "D:\Minecraft\Plain Craft Launcher 2.exe" `
  ".\PCL-patched.exe"
```

可选：

```powershell
--restore              # 从 <源文件>.bak 还原
--dry-run              # 只应用 + 校验，不写文件
--force                # 绕过已知哈希/指纹校验
--allow-partial        # 缺失 required 步骤时跳过继续（用于新版本探测）
--no-update            # 把 ModSecret.UpdateStart 改成 ret，禁用自更新
--no-stop              # 不自动结束正在运行的 PCL
--fingerprints         # 打印关键方法指纹，供新版本适配
--profiles             # 输出受支持的版本列表（GUI 读取，无源文件也可运行）
```

原地补丁/还原前会先结束正在运行的 PCL（按进程名或 exe 路径匹配），避免 Windows
锁住文件导致替换失败；不想自动退出时加 `--no-stop`。

一键构建：

```powershell
pwsh .\build.ps1
# PCL 不在默认路径时
pwsh .\build.ps1 -PclLauncherPath "D:\Games\Plain Craft Launcher 2.exe"
```

`dist\` 下得到两个文件：

| 文件 | 说明 |
|---|---|
| `PclPatcher.exe` | 约 36 MB，自包含单文件，完整补丁工具 |
| `PclPatcher.Gui.Net48.exe` | 约 38 MB，GUI 单文件；内嵌 `PclTheme.xaml`、自包含 `PclPatcher.exe`，以及供 `dotnet` 托管的 `PclPatcher.dll` 副本 |

GUI 的运行方式：

- 如果本机有 .NET 10 运行时，GUI 优先用系统已签名的 `dotnet.exe` 运行托管副本；
  否则使用内嵌的自包含 exe；
- 释放位置：自包含 exe 在 `%LOCALAPPDATA%\PclPatcher\bin`，托管副本在
  `%LOCALAPPDATA%\PclPatcher\managed`；两个目录不可写时都依次回退到 `%TEMP%`
  和 exe 同目录的 `.pclpatcher`；
- `dist\PclPatcher.exe` 同时是 GUI 构建时的 payload 输入；GUI 编译阶段会读取它。

> 为什么还要托管副本：部分安全软件（如联想电脑管家/火绒）会拦截**未签名 exe**
> 写入 `D:\Minecraft` 等目录，但允许签名过的 `dotnet.exe`/`powershell.exe` 写入。
> 这正是 `PATCHER_ERROR: Access to the path '...tmp' is denied` 的常见原因。
> 如果 GUI 本身被运行在受限沙箱里（例如 DSH 的 workspace-write 环境），子进程会继承
> 沙箱，签名 `dotnet` 也写不了沙箱外；补丁失败对话框会提供“以管理员身份重启”，
> 或把 exe 复制到桌面后直接运行。
> GUI 在检测到 .NET 10 时会自动走托管模式；命令行用户可改用
> `dotnet PclPatcher.dll "PCL.exe"`，或把 `PclPatcher.exe` 加入安全软件信任。

GUI 单独构建：

```powershell
dotnet build .\src\PclPatcher.Gui.Net48\PclPatcher.Gui.Net48.csproj -c Release `
  -p:PclLauncherPath="D:\Minecraft\Plain Craft Launcher 2.exe"
.\src\PclPatcher.Gui.Net48\bin\Release\net48\PclPatcher.Gui.Net48.exe
```

或直接使用发布包：

```powershell
.\dist\PclPatcher.Gui.Net48.exe
```

GUI 优先使用内嵌的 patcher；只有内嵌资源缺失时才回退到同目录、`dist` 或构建目录里的
`PclPatcher.exe`。GUI 是 **net48 WPF**，直接使用 PCL 的
`MyCard`、`MyButton`、`MyTextBox`、`MyComboBox`、`MyCheckBox`、`MyScrollBar` 控件；
控件模板和样式来自 PCL `Application.xaml` 的 `PclTheme.xaml`，不是简单模仿颜色。
PCL 控件不是独立 DLL，所以运行时从用户本机选择的 `Plain Craft Launcher 2.exe` 加载，
并通过 `AssemblyResolve` 从 `PCL.Resources.resources` 提取 `MeloongCore` / `PCLCS` 等依赖。

窗口按 PCL `FormMain` 的观感重做：56 px 蓝色标题栏 + 页面药丸（补丁 / 日志 / 关于），
120 px 白色左侧导航，#D5E6FD 页面底色，所有内容都放进 PCL `MyCard`。标题栏、
最小化和关闭按钮、拖拽、缩放由 `WindowChrome` 自己处理；最小化 / 关闭悬停是
PCL 风格的圆形高亮 + 轻阴影，窗口不提供最大化按钮（与 PCL 一致，双击标题栏仍可最大化）。
页面切换有淡入，开始补丁使用 PCL `MyButton` 的 `Highlight` 样式（白底蓝边/蓝字）。

页面分为三页：

- **补丁**：主程序 / 输出 / 选项，右侧是 6 步补丁流程与运行环境；窗口变窄时自动收成单列；
- **日志**：实时彩色日志、自动滚动、复制、清空；patcher 的启动横幅（署名 + 题记
  “Some birds aren't meant to be caged…”）会作为每次运行的日志开头；
- **关于**：应用信息、龙腾猫跃署名、PCL 项目与许可、Minecraft 正版提示。

“输出文件”默认就是源文件：点“开始补丁”会先确认、自动备份为 `.bak`，再原子替换；
旁边的“还原备份”随时把源文件恢复为原版。“另存为”仍然可以写到副本，此时源文件不变。
“补丁选项”里的“自动退出 PCL”默认勾选，开始前会结束正在运行的 PCL，避免文件被锁；
不需要时取消勾选（命令行为 `--no-stop`）。

如果当前不是管理员，点“开始补丁”或“还原备份”会先提示以管理员身份重启（UAC）；
选择“仍然继续”则以普通权限尝试，写入失败时会给出对应的权限/安全软件提示。

关于页署名：**Xtopia**（开发协助：DeepSeek，本工具作者），并保留 PCL 作者龙腾猫跃的控件与设计署名。

警告、覆盖确认、启动前正版提示使用 PCL 风格的自绘对话框，不再弹出系统 MessageBox。
可拖入 exe；`Ctrl+O` 浏览、`F5` 开始、`Ctrl+1/2/3` 切页。启动日志按步骤名同步更新
「补丁流程」，失败时把当前步骤标红。

字体 `PCL English` 在运行时从用户本机 PCL 的 `resources/font.ttf` 提取到
`%LOCALAPPDATA%\PclPatcher\Fonts` 再加载，不随发布包分发。

PCL 的 `ModAnimation` 动画线程和 PCL 主窗体生命周期绑定，直接启动会去找 PCL 入口程序集，
所以这里不启动它，改用等价的 WPF 动画补交互。下拉框的 Popup 使用标准 `ScrollViewer` +
`ItemsPresenter`，不再嵌 `MyScrollViewer`/`MyScrollBar`，否则在自定义 `WindowChrome` 下
打开下拉会触发 `Popup.OnWindowResize` 的 `NullReferenceException`。

剩余差异主要是补丁页采用两栏（左操作、右流程）而 PCL 设置页是单栏，以及 PCL 官方
图标资源没有逐一对齐；控件、色板、标题栏、圆角、卡片阴影都来自 PCL 本体。

发布包**不包含 PCL 本体**；首次启动会自动找 `D:\Minecraft\Plain Craft Launcher 2.exe`，
找不到时弹文件选择框。

界面里的版本下拉框显示的是**支持范围**（如 `2.99.0.1+`），不会硬编码单个版本号。
范围由 patcher 的 `Profiles` 表决定：GUI 启动后运行 `PclPatcher --profiles` 读取，
新增版本只需在 `src\PclPatcher\Profiles.cs` 的 `Profiles` 里加一条 profile，GUI 会自动跟随。

关于页按重度使用规则在显著位置署名“龙腾猫跃”并放赞助链接，启动补丁版前提示购买 Minecraft 正版；
详见 [NOTICE.md](NOTICE.md)。

## 7. 校验与回归

Patcher 内置：

- 源文件版本 + SHA-256 白名单（`--force` 可绕过）；
- 临时文件写入 → 重新加载 → 逐步骤 Verify → 备份原文件并校验 → 原子替换；
- 备份只在需要时创建：已有 `.bak` 不覆盖；源文件是已知原版而备份过期时才刷新；
- `--restore` 先校验备份的 SHA-256 与 `PCL.ModMain` 类型，再经临时文件原子还原；
- 已打补丁的文件再次运行只返回 `already`，不会重复写入；
- `MaxStackCalculator` 计算最大栈深；
- 全模块 IL 校验：分支/switch 目标、异常处理边界、空方法体；
- 单文件 GUI 的内嵌 patcher 按 SHA-256 命名释放并复用，AppData/Temp 不可写时回退到
  exe 同目录。

原地补丁 + 备份 + 还原回归（不碰 `D:\` 的原文件）：

```powershell
# 复制一份 PCL 到工作目录
Copy-Item "D:\Minecraft\Plain Craft Launcher 2.exe" .\PCL-inplace.exe

.\dist\PclPatcher.exe .\PCL-inplace.exe            # 原地补丁，生成 .bak
.\dist\PclPatcher.exe .\PCL-inplace.exe --dry-run  # 已打补丁：只校验，不写盘
.\dist\PclPatcher.exe .\PCL-inplace.exe --restore  # 从 .bak 还原
```

GUI 的手工回归：拖入副本 → 开始补丁 → 日志页应依次出现 5 个 `patch :` 步骤与
`verify : output reloaded`；再点“还原备份”，源文件 SHA-256 应回到原始值。

## 8. 更多方式与取舍

| 方式 | 现状 | 评价 |
|---|---|---|
| literal patch | 已移除（曾用于验证） | 无效，0 处 IL 引用 |
| 局部常量 patch | `ModifyCard` 在用 | 只能改 UI 可见性 |
| 方法体重建 | `LoaderInit` / ctor 删除 / 入口重建 | 主力手段 |
| 控制流重建 | `ModpackInstall` 已实现 | 最难，需处理 EH 边界和 `leave` 目标 |
| 调用点/委托重定向 | 资源详情页双委托 | 依赖编译器生成的闭包结构，需按签名匹配 |
| 运行时 Hook | 未实现 | Harmony/MonoMod/EasyHook；不改文件，但需要注入/启动器 |
| 代理启动器 | 未实现 | 启动时复制并补丁到临时文件；原文件不变 |
| 源码重编译 | 未实现 | 官方版有未公开补丁，重编译不等价 |

## 9. UI 到底改了什么

没有重建 XAML/BAML，也没有重画窗口。`InitializeComponent` 仍然加载 PCL 自己的 UI，
Patcher 只改 code-behind 里对 UI 属性的赋值：

| 位置 | UI 影响 |
|---|---|
| `PageDownloadLeft..ctor` | 删除 `ItemInstall/LabResource` 的 `Collapsed` 和 `PageID=DownloadMod`，让“原版游戏”入口可见、默认子页回到下载页 |
| `PageDownloadModify` | `CardMinecraft.Visibility` 从 Collapsed 改成 Visible |
| `ModMinecraft` 更新提示 | 第 4 个参数从 `""` 改成 `"下载"` |
| `PageInstanceModDisabled` / `PageLaunchLeft` | 重建隐藏条件，按钮/状态按设置显示 |
| `PageDownloadResourceDetail` / `PageSelectLeft` | 恢复“安装”按钮和“导入整合包”列表项 |

现在只有一套补丁流程，因此会恢复上述所有 UI 入口；如果只想恢复“原版游戏”下载页，需要自行删减 PatchPlan 中的步骤。

## 10. 对 IL 形状的依赖

已经实现的降低依赖措施：

1. **能力探测**：每个步骤先跑 `Probe`（不修改模块），返回 `Ready / AlreadyPatched / Missing`；optional 步骤缺失自动跳过，required 缺失默认中止。
2. **语义锚点**：`PageSelectLeft` 的 local 不再写死 `Ldloc_1`，而是从 `stloc` 指令取实际 Local；`ModpackInstall` 会校验 `ldstr + newobj + throw` 三连，并从 `finally` handler 的 `HandlerStart` 找外层 finally，而不是硬算偏移。
3. **版本适配表**：`Profiles` 保存 `2.99.0.1` 的 SHA-256 和 7 个方法指纹；未知版本允许 `--dry-run` 探测，写盘需要 `--force`。
4. **失败关闭**：ctor 必须 4/4、ExtraUi 必须 5/5、Modpack 必须找到限制 throw；数量不对立即中止，不写半坏文件。
5. **幂等**：对已打补丁的 exe 再跑，`Probe` 会返回 `AlreadyPatched`，不会重复应用。

仍然依赖 2.99.0.1 的部分：

- `PageDownloadLeft..ctor` 的 4 段隐藏调用模式；
- `LoaderInit` 的 callback/字段/`PageLoaderInit` 形状；
- `ModpackInstall` 的 `IL_02DA` 汇合点、7 个 `InstallPack*` 方法、无参入口；
- `PageDownloadResourceDetail` 的双委托 `ToListItem` 循环；
- `PageSelectLeft` 的 `"导入整合包"` 字符串和对象初始化结构。

还可以继续做：

- 增加更多版本的 `PclProfile` 和指纹；
- 用 Harmony/MonoMod 做运行时 detour，按方法签名适配而不是 IL 偏移；
- 如果有源码和构建链，直接源码级修改更稳。

## 11. 边界

- 默认原地替换源文件；原文件保存在同目录的 `<源文件>.bak`，可用 `--restore` 或 GUI 的
  “还原备份”恢复。
- 如果 PCL 正在运行，Windows 会锁住 exe，替换/还原会失败；先退出 PCL 再重试。
- 如果日志出现 `cannot write to ... Access is denied`，源文件不会被修改；按第 6 节处理：
  GUI 会提示以管理员身份重启（UAC），或把 patcher 加入安全软件信任、用 `--output`
  写到可写目录。
- `.bak` 只保留最近一次确认的原版：已有备份不会在重复补丁时被覆盖；源文件是已验证原版而
  备份明显过期时才会刷新备份。
- 补丁强依赖 2.99.0.1 的 IL 形状；PCL 升级后 `--force` 可能写出不可运行的文件，必须先重新分析。
- `--no-update` 会关闭 PCL 的更新路径，包括手动检查更新；不需要时不要加。
- 不分发修改版；PCL 作者是按合规要求关闭功能的，第三方“修复版”也有安全风险。
- 许可与免责：本项目代码仅限个人学习研究（[LICENSE](LICENSE)），完整风险提示见
  [DISCLAIMER.md](DISCLAIMER.md)；补丁后的 PCL 属于修改版，不得再分发。中国大陆的
  游戏出版/进口/运营政策与 PCL 许可限制见该文第 4 节。




