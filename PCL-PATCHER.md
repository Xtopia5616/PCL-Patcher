# PCL 2.99.0.x IL patcher：实现、实测与更多方式

作者：**Xtopia**（开发协助：DeepSeek）

> 许可证：[LICENSE](LICENSE)（MIT；PCL 派生文件除外） ·
> 免责声明：[DISCLAIMER.md](DISCLAIMER.md)

目标文件：`D:\Minecraft\Plain Craft Launcher 2.exe`
版本：`2.99.0.1`，SHA-256 `0db34e19d9eeb6d496d20fabe786ec16bbd36ba6b0ccdc8d29cb7e909c114397`
也支持 `2.99.0.0`，SHA-256 `6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386`
以及快照版 `2.99.0.0`（2026-10-29），SHA-256
`5b64d6d540c376390470cea3ab99d47661e8bca81be2ba647ea83e7d2e9428fa`

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
| `CurseForgeMirror` | `ModNet.SendRequest` 入口把 `api.curseforge.com` 改写到 `mod.mcimirror.top/curseforge`，官方 API 连不上时搜索/详情仍可用 |
| `RestoreLinkEntry` | `PageSetupUI.HiddenRefresh`：联机页签和设置项从写死的 `Collapsed` 恢复为 `Visible` |
| `LinkIgnoreDisable` | 把 `InitConfig` 里的 `DisableReason2` / `MinVersionCode` 查询键改名，官方配置不再触发联机kill switch |
| `LinkRequireCustomPeer` | 重建 `GetTargetPeers`：没有填自定义节点时给出明确提示并中止，不再请求已失效的节点列表 |
| `LinkPrecheckCustomPeer` | 在“创建房间/加入房间”入口先检查 `LinkCustomPeer`，为空时直接提示，不再先弹端口/邀请码输入框 |
| `LinkNoticeText` | `PageLinkMain.InitializeComponent` 加载后把联机页的示例文本按钮改成节点停服/自建说明 |
| `LinkPeerHint` | `PageSetupLink.InitializeComponent` 加载后改写“自定义节点”输入框的 ToolTip（原文案还在推荐已停服的公共节点） |
| `DisableSelfUpdate` | 可选 `--no-update`：把 `ModSecret.UpdateStart` 直接 `ret`，防止更新器覆盖补丁（GUI 默认勾选“禁用更新”） |

`RestoreLinkEntry` 起七个联机步骤只有在启用联机范围时才加入 PatchPlan；
`--download-only` 会跳过它们，不会触碰 `PageLinkMain` / `PageSetupUI`。
CurseForge 镜像属于下载组，`--download-only` 仍会应用。

每个步骤都有 `Apply` + `Verify`；写盘前跑全模块 IL 校验，写盘后再重新加载校验。

每个步骤都有 `Probe + Apply + Verify`；`Probe` 不修改模块，返回：

- `Ready`：锚点齐全，可以应用；
- `AlreadyPatched`：已经是补丁后的形状，跳过（幂等）；
- `Missing`：锚点缺失，required 步骤默认中止，optional 步骤跳过。

加 `--allow-partial` 可以强制跳过缺失的 required 步骤，用于新版本探测；不建议直接写盘。

## 2. 版本适配与指纹

`Profiles` 里保存已知版本的 `Version + SHA256 + 方法指纹`。当前已知：

```
version: 2.99.0.0
sha256 : 6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386

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

### 2.1 2.99.0.0 与 2.99.0.1 的差异

两个版本的 7 个指纹里有 4 个不同，但不是所有差异都意味着“需要多打补丁”：

| 站点 | 2.99.0.0 | 2.99.0.1 | 处理 |
|---|---|---|---|
| `PageDownloadLeft..ctor` | 已经是 `PageID = 0`、无隐藏语句 | 4 段隐藏调用 + `PageID = 2` | 0.0 跳过，0.1 删 4 段 |
| 更新提示“下载”按钮 | 原本就有 | 空字符串占位 | 0.0 跳过，0.1 改字符串 |
| Mod 禁用页下载按钮 | 原本就有 `UiHiddenPageDownload` 逻辑 | 被写死为 `Collapsed` | 0.0 跳过，0.1 重建 |
| 启动页空状态 | 原本就有 `UiHiddenPageDownload` 逻辑 | 被写死为 state 1 | 0.0 跳过，0.1 重建 |
| 资源详情安装按钮 | **被删掉**（无 `Install_Click`） | 被删掉 | 两个版本都要补 |
| 版本选择“导入整合包” | 原本已插回面板 | 被删掉 | 0.0 跳过，0.1 补插 |

因此 `ExtraUiEntries` 的探测改成了**逐站点判断**（`IsUpdatePromptPatched` 等 5 个
`Is*Patched`），`Apply` 只补缺失的站点、最后统一 `Verify`，不再要求一次正好改 5 处：

```text
2.99.0.0  patch : ExtraUiEntries  1     # 只补资源详情安装按钮
2.99.0.1  patch : ExtraUiEntries  5
```

旧实现用“更新提示是否有下载按钮”一个信号代表全部 5 个站点，会在 2.99.0.0 上误判为
`AlreadyPatched` 并把整组跳过，结果是资源详情页仍然没有“安装”按钮。

### 2.2 快照版与“后续版本”的自动适配

PCL 的快照版（快照版渠道）和正式版**共用数字版本号**：快照版日志写
`程序版本：快照版 2.99.0.0`，但 `FileVersion` 还是 `2.99.0.0`。因此指纹表里
`2.99.0.0` 有两条 profile：正式版与 2026-10-29 快照版，靠 `Channel` 区分。

构建身份按三段解析（`BuildIdentity.cs`）：

| 顺序 | 判据 | 结果 | 是否需要 `--force` |
|---|---|---|---|
| 1 | SHA-256 命中 profile | `exact`，用该 profile 的指纹复核 | 否 |
| 2 | 7 个锚点方法指纹全部命中某个 profile | `structural`，按该版本方案补丁 | 否 |
| 3 | 都不命中 | `unknown` | 是（GUI：允许未知版本） |

快照重建通常只改时间戳/资源/非锚点代码，锚点指纹不变，于是第 2 段直接放行——
这就是“后续所有快照版不用改代码也能打”的机制。第 2 段仍然要过全部 `Probe`、
写盘前后两轮 `Verify` 和全模块 IL 校验，所以不会因为跳过哈希而少做安全检查。

渠道识别不联网、不读配置：启动器自己把 `快照版 2.99.0.0` / `正式版 2.99.0.0`
写在程序集的 `#US` 堆里（`PCL 版本：快照版 2.99.0.0`、`PCL 已更新至快照版 …`），
`DetectChannel` 直接扫描 PE 里的 UTF-16 字符串。同一数字版本号下，`release` 与
`snapshot` 用同渠道的 profile 优先匹配。

如果某个新版本的锚点真的移动了（第 2 段也匹配不上），用一条命令把它收录：

```powershell
# 打印渠道、SHA-256、7 个指纹、全部 Probe 结论和可粘贴的 C# 条目
.\dist\PclPatcher.exe "新的 PCL.exe" --adopt

# 直接写入 %LOCALAPPDATA%\PclPatcher\profiles.tsv（不用重新编译，GUI 也立即跟随）
.\dist\PclPatcher.exe "新的 PCL.exe" --adopt --save

# 便携用法：把 profiles.tsv 放到指定路径
$env:PCLPATCHER_PROFILES = '.\PclPatcher.profiles.tsv'
.\dist\PclPatcher.exe "新的 PCL.exe" --adopt --save
```

外部 profile 文件是一行一个构建的 TSV：

```text
build <TAB> 版本 <TAB> sha256 <TAB> 渠道 <TAB> 构建日期 <TAB> 方法=指纹;方法=指纹;…
```

`--profiles` 会同时列出内置与外部条目；重复的 SHA-256 以先出现的为准。
输出头部的 `profiles 4` 之后有 `feature probe` 能力标记：GUI 只有看到它才会调用
`--probe` 读取逐步骤状态；旧 patcher 不认识未知开关，会把它当成一次正常的原地补丁，
这个握手就是用来避免误写的。

GUI 对未收录的构建会先跑一次 `PclPatcher --identify`：返回 `match=structural`
时版本行显示“2.99.0.0 快照版（结构匹配）”，可以直接开始补丁，不需要勾选
“允许未知版本”。只有 `match=unknown` 才会弹“版本不匹配”。


## 3. 补丁流程

补丁分为**下载**与**联机**两组：默认两组都应用；`--download-only`（GUI 取消
“同时解锁联机”）只应用下载组，命令行输出里的 `scope` 会标明本次范围。

| 步骤 | 作用 |
|---|---|
| `DownloadLeftCtor` | 恢复“原版游戏”入口和默认子页 |
| `ModifyCard` | 恢复版本修改页的 MC 卡片 |
| `ExtraUiEntries` | 更新提示“下载”、Mod 禁用页、启动页空状态、资源详情安装、导入整合包 |
| `ModpackInstall` | 七路 `switch` + 无参入口文件选择 |
| `LoaderInit` | 真实 `DlClientListLoader` |
| `CurseForgeMirror` | CurseForge API 走 MCIMirror 反代（下载组） |
| `RestoreLinkEntry` | 恢复标题栏“联机”页签和设置里的“联机”项（联机组） |
| `LinkIgnoreDisable` | 忽略官方配置里的 `DisableReason2` / `MinVersionCode`（联机组） |
| `LinkRequireCustomPeer` | 没有自定义节点时提示并中止（联机组，不内置默认节点） |
| `LinkPrecheckCustomPeer` | 建房/加入入口先检查节点（联机组） |
| `LinkNoticeText` | 替换联机页示例文本为节点说明（联机组） |
| `LinkPeerHint` | 改写“设置 → 联机 → 自定义节点”的悬停说明（联机组） |
| `LinkPeerButtons` | 隐藏“节点状态”按钮，把“贡献节点”按钮换成 EasyTier 官网链接（联机组） |

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

### 5.1 联机（无默认节点）

联机属于可选范围：只有勾选“同时解锁联机”才会写入以下七个步骤。联机部分
**不内置任何节点**，也不尝试恢复官方节点列表（`uptime.easytier.cn` 与
`easytier.meloong.com` 均已失效）。打上补丁后：

1. 标题栏出现“联机”，设置里出现“联机”项；
2. `InitConfig` 不再因为 `DisableReason2` / `MinVersionCode` 中止；
3. 点“创建房间/加入房间”时**先**检查 `LinkCustomPeer`：为空就直接弹
   “未填写自定义节点”并说明去哪里填，不会先弹端口/邀请码输入框；
   `GetTargetPeers` 也重建为同样的提示作为兜底，然后抛 `OperationCanceledException`；
4. 用户必须在 **设置 → 联机 → 自定义节点** 里填写一个可用 EasyTier 节点
   （例如 `tcp://183.66.27.22:39409`），房主和加入方填同一个；
5. EasyTier 模块未安装时，PCL 仍会按官方配置里的 `Downloads` 尝试下载
   `easytier-windows-x86_64-v2.4.5.zip`；下载失败时可手动解压到
   `%TEMP%\PCL\EasyTier\`：
   - `easytier-core.exe` → `联机模块.exe`
   - `easytier-cli.exe` → `联机模块 CLI.exe`
   - 保留 `Packet.dll`
   - 若手动预装模块，可把 `HKCU\Software\PCL\LinkEasyTierVersion` 设为字符串
     `1`（与官方配置的 `Link.EasyTierVersion` 一致），让 PCL 跳过重新下载。

联机入口踩过的两个 IL 坑（2.99.0.1 实测）：

- **静态入口不能 `ldarg.0`**：`Join()` / `Join(String)` 是 `Shared`（静态）方法，
  只有 `Create_MouseLeftButtonUp` 是实例方法。给静态入口插 `ldarg.0` 去调用实例的
  `Panic` 时，`Join()` 只有 0 个参数槽 → 非法 IL，CLR 在**第一次 JIT 该方法**时抛
  `InvalidProgramException`（“公共语言运行时检测到无效的程序”），弹窗顶层栈帧就是
  `PCL.PageLinkMain.Join`。表现正是“没填自定义节点 → 点加入房间 → 崩溃”。
  现在入口守卫按 `method.IsStatic` 分叉：静态入口调静态的 `ModMain.MyMsgBox`，
  实例入口才用 `Panic`。
- **不要用宿主运行时的 `Importer`**：patcher 自己跑在 net10 上，
  `new Importer(module).Import(typeof(X))` 解析的是**宿主**运行时，
  `typeof(OperationCanceledException)` 会在产物 AssemblyRef 里留下
  `System.Private.CoreLib 10.0.0.0`；.NET Framework 加载不到这个程序集，
  `GetTargetPeers` 一 JIT 就 `FileNotFoundException`。现在改为复用目标模块里已有的
  `mscorlib` 引用（`FindCoreLibCtor`）；`string.IsNullOrWhiteSpace` /
  `string.Concat` 因为 dnlib 对 `typeof(string)` 有特判，仍落在
  `module.CorLibTypes.String`，不受影响。

注意：

- 这个补丁只是让本机副本恢复联机入口，**没有改变“联机已关闭”的事实**；公开分发
  修改版仍受 PCL 许可与合规约束。
- 节点列表已死，所以自定义节点是必填项；不填只会得到明确提示，不会静默失败。
- 自定义节点由 PCL 设置页写入 `PCL\Setup.ini` 的 `LinkCustomPeer` 项（`Sources.Normal`），
  不是注册表；手动编辑时请保持 `Key:Value` 格式。
- `UiHiddenPageLink` / `UiHiddenSetupLink` 在 `Settings.Get` 的 `ForcedSettings` 里被写死为
  `true`，所以即使恢复了可见性，“隐藏联机页”开关也不会生效。
- 联机页底部的“示例文本 www.meloong.com”原本是 PCL 自带 XAML 里 `PanAlways` 下的示例
  按钮；`LinkNoticeText` 会在 `InitializeComponent` 加载后把它替换成节点停服/自建说明，
  并设置对应的 ToolTip。
- “设置 → 联机 → 自定义节点”的说明文字是 `PageSetupLink.xaml` 里 `TextCustomPeer` 的
  **ToolTip**（不是单独的 TextBlock），原文案还在让你填 `public.easytier.cn` 这类已经停服
  的公共节点。`LinkPeerHint` 在 `PageSetupLink.InitializeComponent` 加载后直接
  `TextCustomPeer.ToolTip = 新文案`，同样不改 BAML/.resources，也就不会碰上资源长度和偏移问题。
- 卡片下面的两个按钮也一起处理了（`LinkPeerButtons`）：它们没有 `x:Name`，取不到字段，于是往
  `PCL.PageSetupLink` 里新增一个私有静态方法 `FixLinkPeerButtons(DependencyObject)`，从 `PanMain`
  往下递归 `Panel.Children`，用 `MyButton.Text` 判断是哪一个：
  - 含“贡献”的按钮 → `Text = "EasyTier 官网"`、换 ToolTip，并把 `CustomEventService.EventData`
    改成 `https://easytier.cn/`（点击仍走原来的“打开网页”事件）；
  - 含“状态”的按钮（`节点状态`，指向已下线的 `uptime.easytier.cn`）→ `Visibility = Collapsed`。

  `MyCard` 本身就是 `Grid`，卡片内容也是 `Grid`/`StackPanel`，整棵子树基本是 `Panel`，所以不需要
  `VisualTreeHelper`；递归只认 `Panel.Children` 和 `Decorator.Child`（`MyCard` 内部可能用 `Border`
  包一层内容），因此也不用等布局完成，`InitializeComponent` 里就能走完。
- 官方配置若以后删掉 `Link` 段，模块下载与启动参数会缺失，需要重新适配。
- `PageSetupUI.HiddenRefresh` 的写死可见性意味着“设置 → 界面 → 隐藏联机页”开关
  不再生效。

### 5.2 CurseForge 与“官方源”

- **现象**：下载页的“整合包 / Mod / 数据包 / 资源包 / 光影包”列表由 `ResourceSearcher`
  同时查 CurseForge 和 Modrinth。搜索用的是写死的 `https://api.curseforge.com/...`，
  **不经过** `ModDownload.DlModRequest` 的“官方源 → 镜像源”回退，所以官方 API 一旦连不上
  （日志常见 `由于远程方已关闭传输流，身份验证失败`），列表就只剩 Modrinth 的内容，
  PCL 还会提示“无法连接到 CurseForge……”。
- **PCL 自带的镜像**：`ModDownload.DlSourceModGet` 里已经有
  `api.curseforge.com → mod.mcimirror.top/curseforge` 的替换，但只用于图标、文件地址等
  少数调用，不覆盖搜索与详情。
- **补丁做法**：在 `PCL.ModNet.SendRequest(string Url, …)` 入口插入 5 条 IL：
  ```
  ldarg.0
  ldstr "https://api.curseforge.com"
  ldstr "https://mod.mcimirror.top/curseforge"
  callvirt System.String::Replace(System.String, System.String)
  starg Url
  ```
  `SendRequest` 是所有 API 请求的公共入口（搜索、详情、版本列表、指纹查询都走它），
  改写一次即可覆盖全部 CurseForge API 调用；文件 CDN（`edge.forgecdn.net` /
  `mediafilez.forgecdn.net`）不在改写范围，仍然直连。
- **副作用与取舍**：
  - 改写是无条件的：即使官方 API 当前可连通也会走反代（PCL 自己的安装流程在选择
    “镜像源”时用的也是这套映射）。
  - 改写发生在 `SecretHeadersSign` 之前，`x-api-key` 不会再被加上；MCIMirror 不校验该头，
    实测 `v1/mods/search`、`v1/mods/{id}`、`v1/mods/{id}/files`、`v1/fingerprints/432`
    都返回 200。
  - 反代由第三方维护，若其下线需要换地址，或改回官方直连。
- **实测**（2.99.0.1，2026-10）：打补丁后点开“下载 → 整合包”，`开始 CurseForge 搜索`
  后约 2 秒返回 40 个工程，列表共 80 条；同一台机器上未打补丁时官方 API 会 TLS 被重置。

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
对已打补丁的文件再次运行会先从 `.bak` 还原原版，再按当前参数重新补丁：这样改过
`--download-only` / `--no-update` 后不会把新选项叠在旧补丁上，`.bak` 也不会被覆盖。

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
--download-only        # 只解锁下载，跳过联机七步
--no-update            # 把 ModSecret.UpdateStart 改成 ret，禁用自更新
--no-stop              # 不自动结束正在运行的 PCL
--fingerprints         # 打印关键方法指纹，供新版本适配
--probe                # 机器可读的逐步骤状态（GUI 选中文件时读取，不写文件）
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

- **补丁**：主程序 / 输出 / 选项，右侧是完整的补丁流程与运行环境（步骤随解锁范围增减，选中文件后先逐项探测并显示 待应用 / 已应用 / 未匹配）；窗口变窄时自动收成单列；
- **日志**：实时彩色日志、自动滚动、复制、清空；patcher 的启动横幅（署名 + 题记
  “Some birds aren't meant to be caged…”）会作为每次运行的日志开头；
- **关于**：应用信息、龙腾猫跃署名、PCL 项目与许可、Minecraft 正版提示。

“输出文件”默认就是源文件：点“开始补丁”会先确认、自动备份为 `.bak`，再原子替换；
旁边的“还原备份”随时把源文件恢复为原版。“另存为”仍然可以写到副本，此时源文件不变。
“补丁选项”里的“同时解锁联机”默认勾选：勾选 = 下载 + 联机，取消 = 仅解锁下载
（命令行 `--download-only`）。“自动退出 PCL”默认勾选，开始前会结束正在运行的 PCL，
避免文件被锁；不需要时取消勾选（命令行为 `--no-stop`）。

GUI 默认带 `app.manifest` 的 `requireAdministrator`：双击启动时 Windows 就会弹一次
UAC，拿到管理员权限后再点“开始补丁”/“还原备份”不会二次提权，也不会丢掉已经选好的
源文件。若用 `-p:PclPatcherNoElevation=true` 构建（自动化 / 截图用），或把清单改成
`highestAvailable`，则退回旧行为：非管理员点“开始补丁”先提示以管理员身份重启（UAC），
选“仍然继续”则以普通权限尝试，写入失败时给出对应的权限/安全软件提示。

提权后的两个限制：一是从资源管理器拖文件进窗口会被 UIPI 拦住，请用“浏览…”按钮；
二是“启动 PCL”改走 `explorer.exe` 代启，这样 PCL 和它拉起的 Java 仍是普通权限，
不会凭空继承管理员令牌。

关于页署名：**Xtopia**（开发协助：DeepSeek，本工具作者），并保留 PCL 作者龙腾猫跃的控件与设计署名。

警告、覆盖确认、启动前正版提示使用 PCL 风格的自绘对话框，不再弹出系统 MessageBox。
可拖入 exe；`Ctrl+O` 浏览、`F5` 开始、`Ctrl+1/2/3` 切页。选中文件或切换补丁选项时，
GUI 调 `PclPatcher --probe` 把每个步骤的 Probe 结果填进「补丁流程」（待应用 / 已应用 /
未匹配，悬停可看原因），不用先点开始；运行日志再按步骤名同步更新，失败时把当前步骤标红。

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

界面里的“版本”是**只读的自动识别结果**（一个不可点击的灰底字段，不是下拉框）：
按所选 PCL 的 SHA-256 在 `Profiles` 里查，补丁版则退回用同名 `.bak` 的 SHA-256 查；
查不到时显示“未识别版本”，没选文件时留空。它不提供手动选择，避免界面显示的版本
和实际被打补丁的文件不一致——这也正是旧版“明明显示 0.0、启动出来却是 0.1”的来源：
旧版那行显示的是**支持范围的下限**（`2.99.0.0`），跟当前文件无关。
字段的悬停提示会列出全部已收录版本和 `2.99.0.0+` 范围。

范围由 patcher 的 `Profiles` 表决定：GUI 启动后运行 `PclPatcher --profiles` 读取，
新增版本只需在 `src\PclPatcher\Profiles.cs` 的 `Profiles` 里加一条 profile，GUI 会自动跟随。
界面上统一显示为 `2.99.0.0+`。

选择逻辑有独立回归用例（`patcher-lab/.test/catalogtest`，纯 net48 控制台）：
0.0 原版 → `2.99.0.0`，0.1 原版 → `2.99.0.1`，0.1 已打补丁 → 靠 `.bak` 仍显示 `2.99.0.1`，
未知哈希 → `未识别版本`。

“查找 PCL”列表里的补丁状态同样按内容判断，不看 `.bak` 是否存在：还原会保留 `.bak`，
所以只有源文件 SHA-256 与同名 `.bak` 不同时才标“已打过补丁”，两者相同则标
“有原版备份”（`Common\PclPatchState.cs`，回归用例同上）。

关于页按重度使用规则在显著位置署名“龙腾猫跃”并放赞助链接，启动补丁版前提示购买 Minecraft 正版；
详见 [NOTICE.md](NOTICE.md)。

## 7. 校验与回归

Patcher 内置：

- 源文件版本 + SHA-256 白名单（`--force` 可绕过）；
- 临时文件写入 → 重新加载 → 逐步骤 Verify → 备份原文件并校验 → 原子替换；
- 备份只在需要时创建：已有 `.bak` 不覆盖；源文件是已知原版而备份过期时才刷新；
- `--restore` 先校验备份的 SHA-256 与 `PCL.ModMain` 类型，再经临时文件原子还原；
- 已打补丁的文件再次运行会从 `.bak` 原版重新补丁，不叠加旧补丁；`.bak` 原样保留；
  “已补丁”必须同时满足“源文件哈希未知 + `.bak` 是已知原版 + 三个必选下载锚点已是
  补丁形状”，避免把“新 PCL 旁边留着旧 `.bak`”误当成补丁产物而被还原成旧版本；
- `MaxStackCalculator` 计算最大栈深；
- 全模块 IL 校验：分支/switch 目标、异常处理边界、空方法体；
- 参数索引校验：`ldarg.*` 超出方法实参槽位（典型是往静态方法里塞 `ldarg.0`）立即中止——
  这类非法 IL 只有 JIT 时才炸，结构校验看不出来；
- 宿主运行时引用校验：产物里一旦出现 `System.Private.CoreLib` / `System.Runtime`
  引用就中止，避免把 net10 的 CoreLib 带进 .NET Framework 程序；
- 单文件 GUI 的内嵌 patcher 按 SHA-256 命名释放并复用，AppData/Temp 不可写时回退到
  exe 同目录。

原地补丁 + 备份 + 还原回归（不碰 `D:\` 的原文件）：

```powershell
# 复制一份 PCL 到工作目录
Copy-Item "D:\Minecraft\Plain Craft Launcher 2.exe" .\PCL-inplace.exe

.\dist\PclPatcher.exe .\PCL-inplace.exe            # 原地补丁，生成 .bak
.\dist\PclPatcher.exe .\PCL-inplace.exe --dry-run  # 已补丁：按 .bak 原版探测，不写盘
.\dist\PclPatcher.exe .\PCL-inplace.exe --no-update # 改选项再补：先还原原版，结果等同全新补丁
.\dist\PclPatcher.exe .\PCL-inplace.exe --restore  # 从 .bak 还原
```

GUI 的手工回归：拖入副本 → 右侧流程应先逐项显示状态（原版为“待应用”）→ 开始补丁 →
日志页应依次出现 13 个 `patch :` 步骤（`--no-update` 时为 14 个）与
`verify : output reloaded`；再点“还原备份”，源文件 SHA-256 应回到原始值，
右侧重新探测后应回到“待应用”。

不开界面就能验证 IL 合法性：用 Windows PowerShell 5.1（.NET Framework）加载补丁产物，
`RuntimeHelpers.PrepareMethod` 会强制 JIT 目标方法，非法 IL 会立刻抛异常：

```powershell
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes('.\PCL-inplace.exe'))
$t = $asm.GetType('PCL.PageLinkMain')
foreach ($m in $t.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance') |
               Where-Object Name -in 'Join', 'GetTargetPeers') {
    try { [System.Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($m.MethodHandle); "OK  $($m.Name)" }
    catch { "BAD $($m.Name): $($_.Exception.InnerException.GetType().Name)" }
}
```

`Join()` / `Join(String)` / `GetTargetPeers()` 都应为 `OK`（外部依赖缺失时会是
`FileNotFoundException`，与 IL 是否合法无关）。

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
| `PageSetupUI.HiddenRefresh` / `PageLinkMain.InitializeComponent` | 恢复联机页签/设置项，并把示例文本换成节点说明 |

现在默认会恢复上述所有 UI 入口；如果只想恢复“原版游戏”下载页，可以取消勾选
“同时解锁联机”（命令行 `--download-only`），此时不会应用联机七个步骤。

## 10. 对 IL 形状的依赖

已经实现的降低依赖措施：

1. **能力探测**：每个步骤先跑 `Probe`（不修改模块），返回 `Ready / AlreadyPatched / Missing`；optional 步骤缺失自动跳过，required 缺失默认中止。
2. **语义锚点**：`PageSelectLeft` 的 local 不再写死 `Ldloc_1`，而是从 `stloc` 指令取实际 Local；`ModpackInstall` 会校验 `ldstr + newobj + throw` 三连，并从 `finally` handler 的 `HandlerStart` 找外层 finally，而不是硬算偏移。
3. **版本适配表**：`Profiles` 保存 `2.99.0.0` / `2.99.0.1` 的 SHA-256 和各自的 7 个方法指纹；未知版本允许 `--dry-run` 探测，写盘需要 `--force`。
4. **失败关闭**：ctor 必须 4/4、ExtraUi 的 5 个站点必须逐个 Verify 通过、Modpack 必须找到限制 throw；数量不对立即中止，不写半坏文件。
5. **幂等**：对已打补丁的 exe 再跑，会先从 `.bak` 还原原版再应用，结果与全新补丁一致；
   没有可用备份时 `Probe` 返回 `AlreadyPatched`，不会重复应用。

仍然依赖 2.99.0.0 / 2.99.0.1 的部分：

- `PageDownloadLeft..ctor` 的 4 段隐藏调用模式（只在 2.99.0.1 上需要删；0.0 已是补丁形状）；
- `LoaderInit` 的 callback/字段/`PageLoaderInit` 形状；
- `ModpackInstall` 的 `IL_02DA` 汇合点、7 个 `InstallPack*` 方法、无参入口；
- `PageDownloadResourceDetail` 的双委托 `ToListItem` 循环（两个版本都要补）；
- `PageSelectLeft` 的 `"导入整合包"` 字符串和对象初始化结构。

还可以继续做：

- 增加更多版本的 `PclProfile` 和指纹；
- 用 Harmony/MonoMod 做运行时 detour，按方法签名适配而不是 IL 偏移；
- 如果有源码和构建链，直接源码级修改更稳。

## 11. 边界

- 默认原地替换源文件；原文件保存在同目录的 `<源文件>.bak`，可用 `--restore` 或 GUI 的
  “还原备份”恢复。
- 如果 PCL 正在运行，Windows 会锁住 exe，替换/还原会失败；先退出 PCL 再重试。
- 如果日志出现 `cannot write to ... Access is denied`，源文件不会被修改；GUI 启动时
  已经通过 `app.manifest` 提权（一次 UAC），若仍被拦截，按第 6 节处理：
  把 patcher 加入安全软件信任、用 `--output` 写到可写目录。
- `.bak` 只保留最近一次确认的原版：已有备份不会在重复补丁时被覆盖；源文件是已验证原版而
  备份明显过期时才会刷新备份。
- 补丁强依赖 2.99.0.0 / 2.99.0.1 的 IL 形状；PCL 升级后 `--force` 可能写出不可运行的文件，必须先重新分析。
- `--no-update` 会关闭 PCL 的更新路径，包括手动检查更新；不需要时不要加。
- 不分发修改版；PCL 作者是按合规要求关闭功能的，第三方“修复版”也有安全风险。
- 许可与免责：本项目原创代码以 [MIT](LICENSE) 发布（PCL 派生文件除外），完整风险提示见
  [DISCLAIMER.md](DISCLAIMER.md)；补丁后的 PCL 属于修改版，不得再分发。中国大陆的
  游戏出版/进口/运营政策与 PCL 许可限制见该文第 4 节。




