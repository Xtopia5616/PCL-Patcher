# PCL Patcher

> **仅供学习研究 · 24 小时内删除**
>
> 本项目仅用于个人学习、研究与技术验证，不得用于任何商业或违法用途；请在下载或
> 体验后 24 小时内删除。

> **合规提示（中国大陆）**
>
> 本项目**不提供、也不授权**以下行为：游戏下载与代下载、账号交易与共享、私服或
> 运营服务、绕过实名制/防沉迷/版号审批/区域限制/付费、商业倒卖，以及冒充官方或
> 暗示与 PCL 作者存在关联。补丁后的 PCL 属于修改版，**不得分发**。完整说明见下方
> [「合规与法律风险」](#合规与法律风险)与 [DISCLAIMER.md](DISCLAIMER.md)。

Plain Craft Launcher (PCL) 的第三方 IL 补丁工具。

许可证：[MIT](LICENSE)（原创部分；PCL 派生文件除外） · 免责声明：[DISCLAIMER.md](DISCLAIMER.md) ·
第三方说明：[NOTICE.md](NOTICE.md)

## 合规与法律风险

本节是风险提示，**不是法律意见**，也不构成对任何行为的许可、授权或背书。使用本项目
前，请确认你的行为在你所在法域合法；如不确定，请咨询有资质的律师，或不要使用。

### 1. 游戏出版、进口与运营

- 在中国大陆，网络游戏的出版、进口与运营通常需要取得网络出版服务许可、游戏版号/
  审批，并遵守内容审核、实名制与未成年人保护等要求，主要见于《网络出版服务管理
  规定》《互联网文化管理暂行规定》及国家新闻出版署的配套规定。
- Minecraft 在中国大陆由**网易**独家运营（《我的世界》中国版，<https://mc.163.com/>）；
  国际版并未在中国大陆取得同等的官方运营与发行安排。
- 本项目不提供、也不应被用于向公众提供游戏下载、代下载、账号共享、区域绕过、私服
  搭建或运营服务；把恢复后的入口用于上述目的，可能涉及游戏进口、出版、运营资质与
  独家授权等合规问题。

### 2. 技术措施与著作权

- PCL 作者关闭部分下载入口，是应合规要求做出的功能调整。本项目只在本机副本上验证
  IL 重建这一技术问题，不提供规避方法、工具或抗辩。
- 是否构成对技术措施的规避、是否存在合理使用等例外，需结合行为目的、对象、方式与
  后果由专业律师判断，本项目不对任何具体使用行为的合法性作出判断。
- 修改后的 PCL 不再是原作者发布的版本，其复制、传播与使用受 PCL 许可及著作权法约束。

### 3. 实名制、防沉迷与未成年人保护

- 本项目不提供账号、登录、支付能力，也不提供绕过实名认证、防沉迷或未成年人保护的
  任何功能；不得被用于此类目的。
- 未成年人使用游戏服务应遵守《未成年人保护法》及国家新闻出版署关于防沉迷的规定，
  监护人应尽到监护责任。

### 4. 网络安全、数据与个人信息

- 本项目不收集遥测，不主动上传文件；但补丁后的 PCL 仍会按其自身逻辑联网，相关数据
  处理以其隐私政策为准。
- 不得利用本项目侵入、干扰、破坏计算机信息系统，或非法获取、出售、提供个人信息，
  否则可能违反《网络安全法》《数据安全法》《个人信息保护法》等规定。

### 5. 平台、账号与安全风险

- 修改客户端可能触发安全软件、平台风控或账号处罚；账号封禁、数据丢失、设备损坏等
  后果由使用者自行承担。
- 请只从官方渠道获取 PCL 与 Minecraft，不要使用来源不明的“修复版”“增强版”“绿色版”。

### 6. PCL 与 Minecraft 的许可

- 本项目对 PCL 的使用按[《存储库合理使用指南》](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)
  处理：标注第三方、保留署名与赞助链接、公开源代码、保持蓝色主色。
- 本项目**不授予**任何 PCL、Minecraft 或第三方软件的权利；`PclTheme.xaml` 等 PCL
  派生内容不适用 MIT，详见 [NOTICE.md](NOTICE.md)。
- Minecraft 的下载、安装与游玩受 [Minecraft EULA](https://www.minecraft.net/eula) 约束；
  请购买正版：<https://www.minecraft.net/>。

### 7. 责任自担

在适用法律允许的最大范围内，本项目按“现状”提供，不附带任何担保；因使用本项目产生
的数据丢失、文件损坏、账号处罚、第三方索赔及法律与合规责任，由使用者自行承担。
完整条款见 [LICENSE](LICENSE) 与 [DISCLAIMER.md](DISCLAIMER.md)。

## 使用边界

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

- 本项目**原创部分**（`src/`、`build.ps1`、文档、图标）采用 [MIT](LICENSE)：
  可商用、可修改、可再分发，保留版权声明与许可文本即可。
- `PclTheme.xaml`、PCL 控件与字体、PCL 本体**不在 MIT 范围内**，权利归
  龙腾猫跃 / PCL 项目，按 [NOTICE.md](NOTICE.md) 的合理使用安排处理；
  第三方许可证全文见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- 用本工具修改 PCL 得到的**补丁产物**仍受 PCL 许可约束，**请勿分发**；
  MIT 只覆盖工具本身，不改变 PCL 的任何权利。
- 完整风险提示见 [DISCLAIMER.md](DISCLAIMER.md)。发布包**不包含**
  `Plain Craft Launcher 2.exe`、PCL 的嵌入 DLL 或 PCL 本体。
