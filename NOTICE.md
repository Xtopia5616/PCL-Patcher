# NOTICE / 第三方说明

本工具名称：**Plain Craft Launcher (PCL) Patcher（第三方）**

作者：**Xtopia**（开发协助：DeepSeek）
版权人：**Xtopia**

- 本项目许可证：[LICENSE](LICENSE)（MIT；PCL 派生文件除外）
- 完整免责声明：[DISCLAIMER.md](DISCLAIMER.md)
- 第三方许可证全文：[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

> 本文件说明本项目使用的第三方作品及其许可，不改变第三方许可证下的
> 任何权利与义务。若本文件与第三方许可证原文冲突，以第三方许可证为准。

## 1. 本工具自身的许可

`patcher-lab` 中由版权人原创的源代码、构建脚本、文档、图标与配置，按
[LICENSE](LICENSE) 以 **MIT** 授权：可以商用、修改、再分发，只需在副本或
实质性部分中保留版权声明与许可证文本。

以下内容**不在** MIT 范围内：

- PCL 本体、PCL 的嵌入程序集与 PCL 的界面资源；
- `PclTheme.xaml`（从 PCL `Application.xaml` 提取的控件样式）；
- PCL 字体 `PCL English`；
- dnlib、.NET / WPF 等第三方依赖（适用各自的 MIT 等许可证）；
- 用本工具修改 PCL 得到的补丁产物；
- `bin/`、`obj/`、`dist/` 中内嵌上述第三方内容的构建产物。

本项目属于对 PCL 的**重度使用**：按 PCL《存储库合理使用指南》以 MIT 公开
源代码、标注第三方来源并按要求署名。MIT 只覆盖原创部分，不能也不应被理解为
对 PCL 内容的再授权；补丁后的 PCL 仍按 PCL 许可处理，**不得分发**。

## 2. PCL 署名

UI 控件与设计来自 **Plain Craft Launcher (PCL)**，作者：**龙腾猫跃**。

- PCL 项目：<https://github.com/Meloong-Git/PCL>
- 赞助 PCL：<https://meloong.com/afd/a/LTCat>
- PCL 许可全文与合理使用指南：<https://github.com/Meloong-Git/PCL/blob/main/LICENCE>

## 3. 许可与使用方式

本工具属于对 PCL 相关内容的**重度使用**，按 PCL 的《存储库合理使用指南》处理：

- 名称以 `Plain Craft Launcher (PCL)` 开头，并标注“第三方”；
- 关于页面显著位置保留龙腾猫跃署名与赞助链接；
- 界面主色保持 PCL 的蓝色系；
- 以 MIT 公开源代码，并继续遵守 PCL 的合理使用指南；
- 启动 Minecraft 前提示购买正版。

本工具是第三方非官方工具，**不是** PCL 的官方组件、官方分支或官方发行版，
也未获得 PCL 作者的授权、认可或赞助。

上述署名与合理使用安排**不构成** PCL 对修改、再分发的许可；修改 PCL 仍
可能违反 PCL 的分发有限许可。详见 [DISCLAIMER.md](DISCLAIMER.md) 第 4.3 节。

## 4. 第三方组件清单

| 组件 | 权利归属 | 许可证 / 说明 |
|---|---|---|
| Plain Craft Launcher (PCL) | 龙腾猫跃 | PCL 分发有限许可 / 合理使用指南 |
| `PclTheme.xaml`、PCL 控件样式 | 龙腾猫跃 / PCL 项目 | 不适用 MIT；仅按合理使用指南复用 |
| PCL 字体 `PCL English` | 龙腾猫跃 / PCL 项目 | 运行时从本机 PCL 提取，不随发布包分发 |
| [dnlib](https://github.com/0xd4d/dnlib) 4.5.0 | 0xd4d 及贡献者 | MIT |
| .NET / WPF | .NET Foundation 及贡献者 | MIT |
| Microsoft.NETFramework.ReferenceAssemblies | .NET Foundation | MIT（仅构建期使用） |
| Minecraft | Mojang Studios / Microsoft | Minecraft EULA |

各依赖的完整许可证文本以其发布仓库 / NuGet 包内所附文本为准；dnlib 与 .NET
的 MIT 文本已收录在 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
若你需要再分发任何第三方组件，请自行满足对应许可证的要求。

## 5. 不分发 PCL 本体

`dist/` 下有两个独立文件：

```text
PclPatcher.exe             # 完整补丁器（自包含）
PclPatcher.Gui.Net48.exe   # 单文件 GUI
```

GUI 内嵌 `PclTheme.xaml`（从 PCL `Application.xaml` 提取的控件样式）、自包含
命令行 `PclPatcher.exe`，以及一份供 `dotnet` 托管的 `PclPatcher.dll` 副本。
首次启动时把 patcher 释放到 `%LOCALAPPDATA%\PclPatcher`（不可写时回退到
`%TEMP%` 或 exe 同目录 `.pclpatcher`）；检测到 .NET 10 运行时则优先用系统
`dotnet` 运行托管副本。

**不包含** `Plain Craft Launcher 2.exe`、PCL 的嵌入 DLL 或 PCL 本体。

`PclTheme.xaml` 是从 PCL `Application.xaml` 提取的控件样式（属于重度使用
范围，按本说明署名并开源）。GUI 在启动时从**用户本机的 PCL 安装**加载
PCL.exe，并通过 `AssemblyResolve` 从该 exe 的 `PCL.Resources.resources`
中按需加载 `MeloongCore`、`PCLCS` 等依赖。

界面字体 `PCL English` 同样在运行时从本机 PCL 的 `resources/font.ttf` 提取
到 `%LOCALAPPDATA%\PclPatcher\Fonts` 后加载，**不随发布包分发**；删除本机
PCL 后字体会自动回退到 Microsoft YaHei UI。

## 6. Minecraft

本工具不提供 Minecraft 游戏本体，也不绕过正版验证。游玩 Minecraft 请购买正版：

- <https://www.minecraft.net/>

Minecraft 的名称、素材与客户端归 Mojang Studios / Microsoft 所有；在中国
大陆的运营与授权情况请以权利人公告为准。

## 7. 中国大陆合规提示

本项目恢复的是 PCL 因合规要求关闭的下载入口。在中国大陆，网络游戏的出版、
进口与运营通常需要相应审批/版号，Minecraft 由网易独家运营；修改 PCL 还可
能违反 PCL 的分发有限许可。本项目不提供下载服务，也不授权任何规避行为。
完整提示见 [DISCLAIMER.md](DISCLAIMER.md) 第 4 节。

## 8. 免责声明摘要

本工具是**第三方非官方工具**，与 PCL 作者、成都瓜皮龙科技有限公司、网易、
Microsoft、Mojang 无关。本工具按“现状”提供，不附带任何担保；使用本工具
可能损坏文件、导致数据丢失或产生法律与合规风险，由使用者自行承担。

补丁后的 PCL 属于 **PCL 的修改版**，**请勿分发**。修改版再分发的著作权、
商标与平台责任由再分发者自行承担。

以上仅为摘要，完整条款以 [DISCLAIMER.md](DISCLAIMER.md) 与
[LICENSE](LICENSE) 为准。
