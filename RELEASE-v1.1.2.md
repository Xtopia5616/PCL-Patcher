## 新功能：关于页的版本号与「检查更新」

「关于」页新增「版本与更新」卡片：

- **版本号不再手写**：取自 `Directory.Build.props` 的 `<Version>`；GUI 卡片与
  `PclPatcher` 启动横幅读的是同一份程序集元数据，不会再出现“文档写 1.1.1、程序里是别的数”。
- **检查更新**：点按钮读取 GitHub 最新 Release 并比较本机版本——
  已是最新给绿色状态；发现新版本给橙色状态并弹窗询问是否打开发布页；
  失败时红色状态写明原因（DNS / 连接中断 / TLS / 403），发布页链接仍然可用。
- **GitHub 地址**：页头直接写出 `github.com/Xtopia5616/PCL-Patcher`，
  卡片与「署名与许可」里也挂了仓库与发布页链接。
- **只在点击时联网**：一次 GET，不发送任何本机信息，不自动检查；
  读不到版本号时显示「版本未知」，按钮直接打开 GitHub 发布页。

## 1.1.2 的改动

- 新增 `src/PclPatcher.Gui.Net48/Common/UpdateChecker.cs`：
  先用 GitHub REST API 读 `tag_name` / `html_url`；API 不可用（限流、403、代理拦截）时
  退回 `releases.atom`，不占 API 配额。
- 版本比较逐段进行：`v` 前缀、`+build` 后缀、`-rc` 预发布、缺省段（`1.1.2` 与
  `1.1.2.0` 等价）都处理，正式版高于同号预发布版。
- 版本号 1.1.1 → **1.1.2**；patcher 横幅改用程序集版本，不再硬编码。
- 关于页的支持版本徽章加上 `PCL ` 前缀（`PCL 2.99.0.0+`），避免与工具自身版本混淆。
- 补丁页「运行环境 → 网络」由「无需联网」改为「检查更新时联网」。
- `PCLPATCHER_UPDATE_API` 可覆盖 API 地址（镜像 / 本地假 feed 回归用，回环地址不走系统代理）。
- `Profiles.cs` 的四个内置构建未改动，仍支持 PCL 2.99.0.0+。

## 补丁器行为改进

- **`--force` 与降级解耦**：`--force` 只跳过 SHA-256 / 指纹校验；从更旧的 `.bak`
  重打（降级）必须显式加 `--allow-downgrade`。以前 `--force` 会顺带接受降级，现在默认拒绝并说明原因。
- **未知构建给出“最接近版本”**：日志先打印 `closest: <版本> (n/7)` 和最多三个不一致的锚点，
  只用于提示，不改变补丁步骤（每一步仍由自己的 Probe 决定）。
- **不再反复要求 `--force`**：`--force` 原地补丁成功后，把本次补丁的 `.bak` 作为
  **未验证**构建写入 `%LOCALAPPDATA%\PclPatcher\profiles.tsv`（附注释行、行尾 `auto` 标记），
  下次重打或 `--restore` 不用再加 `--force`；已补丁的旧产物不会被收录，只打印告警。
- **退出码 `2`**：没有任何步骤被应用时返回 2 并列出被跳过的步骤，不再冒充“已是补丁状态”；
  另存为模式只警告。
- `--identify`、`--probe` 增加 `closest` 字段；`--probe` 同时输出
  `sha256 / channel / match / profile`（格式 1 → 2），`--profiles` 增加 `auto` 列（格式 4 → 5）。
- GUI 源信息条显示“未识别版本 · 最接近 2.99.0.1 快照版（6/7）”；勾选“允许未知版本”后，
  确认对话框会写明“本次不校验 SHA-256 与方法指纹”；遇到降级场景弹出“允许降级”确认并自动带
  `--allow-downgrade` 重跑；退出码 2 显示为“没有可应用的步骤”而不是失败。
- 内部：指纹比较合并为单一实现并缓存 profile key 解析；渠道识别改为按字节搜索版本横幅，
  不再把整个 exe 解码两次。

## 兼容性

- 补丁步骤与 IL 改写**没有变化**：对已收录构建，补丁后的字节与 1.1.1 相同
  （实测 2.99.0.1 原版 → `96974c42…`）。变的是判定、提示与退出码。
- 唯一的不兼容：过去靠 `--force` 顺带降级的命令行需要改成 `--allow-downgrade`，
  否则会被拒绝（源文件不会被修改）。
- 解析 `--profiles` / `--probe` 的第三方脚本：新增的是尾部字段与前置行，旧解析器可忽略。

## 升级方式

直接替换两个 exe 即可：

```powershell
# 先退出 PCL，然后照旧
.\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe"
```

## 产物

| 文件 | 说明 | SHA-256 |
|---|---|---|
| `PclPatcher.exe` | 自包含命令行补丁器（win-x64 single-file），约 36.5 MB | `72a7a4db77694b38b116b72b0702fefb6a43f020d02113feed3edd01b122b26a` |
| `PclPatcher.Gui.Net48.exe` | 单文件 GUI（.NET Framework 4.8），内嵌 1.1.2 补丁器与主题，约 38.3 MB | `f1e5fd5f63d5855fa174d208462c58fbce7423e39b6c9b648486cff2f29afc65` |

发布包**不包含** `Plain Craft Launcher 2.exe`、PCL 的嵌入 DLL 或 PCL 本体，也不分发补丁后的启动器。

## 验证

- 本地假 feed（`http://127.0.0.1:8123/release.json`）回归三种结果：
  `v9.9.9` → 发现新版本并弹窗；`v1.1.2` → 已是最新；`v1.0.0` → 本机构建比线上新。
- 真实 GitHub API：本构建环境子进程 TLS 不可用（`与 GitHub 的连接被中断`），
  因此只验证了失败分支；成功分支与失败分支共用同一段解析/比较代码，已由假 feed 覆盖。
- 补丁器回归（真实构建 + 构造样本）：2.99.0.1 原版补丁/重打/还原；`--force` 不再允许降级、
  `--allow-downgrade` 才允许；未知但结构可探的构建（6/7 指纹）在 `--force` 后自动收录、
  再次运行无需 `--force`；0 步可打时退出码 2 且不写盘；GUI 中源信息条、确认框与步骤面板正常。
- 截图：`.test/shots/gui/about-newer-*.png`、`about-same-*.png`、`about-real-*.png`。

细节见 [PCL-PATCHER.md](PCL-PATCHER.md) §2.2 / §7 与 [README.md](README.md)「使用」。
