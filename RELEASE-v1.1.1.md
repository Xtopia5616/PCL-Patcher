## 修复：整合包详情页展开版本卡片必崩

补丁后的 PCL 打开「下载 → 整合包 → 任意整合包 → 展开版本卡片」时直接报错退出：

```text
[A]ClickEventHandler 无法强制转换为 [B]ClickEventHandler
   在 PCL.MyIconButton.add_Click
   在 PCL.ResourceVersion._Closure$__27-0._Lambda$__0
   在 PCL.MyVirtualizingElement`1.Init
错误类型：System.InvalidCastException
```

原因：`PCL.ResourceVersion.ToListItem` 的两个处理器参数是**两个同名但不同的嵌套委托**——

| 参数 | 类型 | 用途 |
|---|---|---|
| 0 | `PCL.MyListItem/ClickEventHandler` | 整行点击（整合包走 `Install_Click`） |
| 1 | `PCL.MyIconButton/ClickEventHandler` | 行尾「另存为」图标（`Save_Click`） |
| 2 | `System.Boolean` | `IsBadDisplay` |

1.1.0 及更早的 `PatchResourceDetailInstall` 把调用点里现成的 `newobj` 复用给两个参数，
于是图标槽拿到的是 `MyListItem` 委托。IL 校验与 `Verify` 都能通过（两者 `Invoke` 形状同为
`(Object, EventArgs)`），直到行被虚拟化创建、`MyIconButton.Click += handler` 在
`add_Click` 里 `DirectCast` 才炸。

**2.99.0.0 / 2.99.0.1 的正式版与 414 / 416 快照全部受影响。**

## 1.1.1 的改动

- 不再复用调用点里的 `newobj`：从 `ToListItem` 的**签名**取两个参数类型，各自
  `FindMethod(".ctor")` 后分别构造；参数类型漂移会直接报错，而不是产出一个“能加载、
  运行时才崩”的文件。
- `Probe` / `Verify` 断言图标槽必须是 `MyIconButton/ClickEventHandler`。旧产物会明确提示：
  `icon button bound to PCL.MyListItem/ClickEventHandler instead of PCL.MyIconButton/ClickEventHandler`。
- 旧补丁产物支持 **`--force` 原地修复**（只改那一条 `newobj`），结果与从原版重打字节一致。
- 调用点不再复用同一条 `ldloc` 指令对象。
- 版本号 1.1.0 → 1.1.1；`Profiles.cs` 未改动，仍支持 PCL 2.99.0.0+。

## 升级方式

已打过补丁的启动器（推荐，自动从 `.bak` 原版重建，不叠加旧补丁）：

```powershell
# 先退出 PCL
.\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe"
```

只有补丁产物、没有 `.bak` 时原地修复：

```powershell
.\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe" --force
```

只想确认现状、不写文件：

```powershell
.\PclPatcher.exe "D:\Minecraft\Plain Craft Launcher 2.exe" --probe
```

## 产物

| 文件 | 说明 | SHA-256 |
|---|---|---|
| `PclPatcher.exe` | 自包含命令行补丁器（win-x64 single-file），约 36 MB | `b7cc7d6056780774ecf1616a00560ed5bc633e4476dce1e4226ded87afa05ee9` |
| `PclPatcher.Gui.Net48.exe` | 单文件 GUI（.NET Framework 4.8），内嵌 1.1.1 补丁器与主题，约 38 MB | `2e6ca5fcb05dc1d9a5a5c579808efa9a603c326e2654eb998601596ebdf52fe2` |

发布包**不包含** `Plain Craft Launcher 2.exe`、PCL 的嵌入 DLL 或 PCL 本体，也不分发补丁后的启动器。

## 各构建的补丁后哈希（1.1.1）

| 构建 | 默认选项 | `--no-update` |
|---|---|---|
| 2.99.0.0 正式版 | `d4fbe59e…5f0660a0` | — |
| 2.99.0.0 快照 414 | `6bb73c76…dafb3c3b9` | `ffdd04b1…f96945c3e` |
| 2.99.0.1 正式版 415 | `96974c42…89a1ea84` | — |
| 2.99.0.1 快照 416 | `ba320da6…ae4b6b38` | `b2ee11de…9dfa0c28` |

源文件 SHA-256 不变（见 README「已验证构建」）。补丁后哈希是参考值：选项或 patcher
版本不同都会变，判断构建身份请看源文件哈希。

## 验证

- 四个构建各打一次，dump 确认 `InitializeStackResourceVersions` 的两条 `newobj` 分别是
  `MyListItem/ClickEventHandler`（`Install_Click`）与 `MyIconButton/ClickEventHandler`（`Save_Click`）。
- 写盘后重新加载，`Verify` + 全模块 IL 校验通过；旧产物 `--force` 修复与从原版重打字节一致。
- 该页面的实机点击需要外网（资源列表走 CurseForge/Modrinth）；本次构建环境子进程 TLS
  不可用（`安全包中没有可用的凭证`），因此以 IL/委托类型断言代替实机复现。

细节见 [VERSIONS.md](VERSIONS.md) §4.6 与 [PCL-PATCHER.md](PCL-PATCHER.md) §5.0。
