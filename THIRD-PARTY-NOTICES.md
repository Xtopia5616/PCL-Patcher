# 第三方许可与声明

本项目会再分发、内嵌或依赖以下第三方组件。各组件版权归其作者所有，适用
其各自的许可证。本文件不改变 [LICENSE](LICENSE) 的适用范围，也不改变任何
第三方许可证下的权利与义务。

> 本文件不是法律意见。若你需要再分发本项目或其构建产物，请自行核对并满足
> 对应第三方许可证的全部要求。

## 1. dnlib 4.5.0

本项目通过 NuGet 引用 dnlib，并将其嵌入 `PclPatcher` / GUI 发布产物。

```text
Copyright (C) 2012-2019 de4dot@gmail.com

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

- 项目主页：<https://github.com/0xd4d/dnlib>
- NuGet 包：<https://www.nuget.org/packages/dnlib/4.5.0>

## 2. .NET / WPF

`PclPatcher` 的自包含发布与 `dist` 下的单文件 GUI 内嵌 .NET 运行时，
因此会再分发 .NET 运行时的部分组件。

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- .NET 许可证：<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>
- .NET 第三方声明：<https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>
- .NET 项目：<https://github.com/dotnet>

> 自包含发布还可能包含 Microsoft 提供的原生运行时组件、ICU 数据或其他
> 第三方材料，其许可见 .NET 官方 `THIRD-PARTY-NOTICES.TXT`。

## 3. Microsoft.NETFramework.ReferenceAssemblies（仅构建期）

仅用于在非 Windows 环境或缺少 .NET Framework 参考程序集时构建 net48 项目，
**不随发布包分发**。

- 许可证：MIT
- NuGet 包：<https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies/>

## 4. Plain Craft Launcher (PCL)

PCL 本体、嵌入程序集、控件样式、字体与界面资源**不适用**本项目的
[LICENSE](LICENSE)，其权利归龙腾猫跃及 PCL 项目所有。本项目不分发 PCL 本体。

- 项目主页：<https://github.com/Meloong-Git/PCL>
- PCL 许可与合理使用指南：<https://github.com/Meloong-Git/PCL/blob/main/LICENCE>
- 本项目对 PCL 的使用说明：[NOTICE.md](NOTICE.md)

> 修改 PCL 可能超出 PCL 分发有限许可的范围。本项目不授予任何 PCL 权利，
> 也不为修改行为提供许可或抗辩；详见 [DISCLAIMER.md](DISCLAIMER.md) 第 4.3 节。

## 5. Minecraft

本项目不包含、也不分发 Minecraft 客户端、素材或服务。Minecraft 的权利归
Mojang Studios / Microsoft 所有，使用受 Minecraft EULA 约束。

- 官方网站：<https://www.minecraft.net/>
- EULA：<https://www.minecraft.net/eula>
