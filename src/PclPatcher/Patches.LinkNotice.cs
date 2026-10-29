namespace PclPatcher;

internal static partial class Program
{
    // The 联机 page ships a leftover "示例文本 www.meloong.com" text button in
    // its XAML (PanAlways grid). It has no x:Name, so the string only exists in
    // the BAML stream; instead of rewriting the .resources container we replace
    // it at load time, right after InitializeComponent's LoadComponent() call.
    private const string LinkNoticeText =
        "官方 EasyTier 节点已暂停服务，可自建节点后在设置中填写";

    private const string LinkNoticeTip =
        "PCL 官方的 EasyTier 公共节点（uptime.easytier.cn、easytier.meloong.com）已经停止服务。" +
        "可以在自己的服务器上自建 EasyTier 节点，再到 设置 → 联机 → 自定义节点 里填写" +
        "（房主与加入方要填同一个节点）。";

    private static ProbeResult ProbeLinkNoticeText(ModuleDef module)
    {
        var body = InitializeComponentBody(module);
        if (HasString(body, LinkNoticeText)) return new(PatchState.AlreadyPatched, "notice text already installed");
        return body.Instructions.Any(i => i.OpCode.Code is Code.Call or Code.Callvirt && i.Operand is IMethod m && m.Name == "LoadComponent")
            ? new(PatchState.Ready, "InitializeComponent LoadComponent anchor found")
            : new(PatchState.Missing, "InitializeComponent LoadComponent anchor not found");
    }

    private static int PatchLinkNoticeText(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageLinkMain");
        var buttonType = FindType(module, "PCL.MyTextButton");
        var body = InitializeComponentBody(module);

        var getPanAlways = FindMethod(type, "get_PanAlways");
        var setText = FindMethod(buttonType, "set_Text");
        var getChildren = FindMemberRef(module, "System.Windows.Controls.Panel", "get_Children", 0);
        var getCount = FindMemberRef(module, "System.Windows.Controls.UIElementCollection", "get_Count", 0);
        var getItem = FindMemberRef(module, "System.Windows.Controls.UIElementCollection", "get_Item", 1);
        var setToolTip = FindMemberRef(module, "System.Windows.FrameworkElement", "set_ToolTip", 1);

        var loadComponent = body.Instructions.FirstOrDefault(i => i.OpCode.Code is Code.Call or Code.Callvirt && i.Operand is IMethod m && m.Name == "LoadComponent")
                           ?? throw new InvalidOperationException("InitializeComponent has no LoadComponent call");

        var indexLocal = body.Variables.Any(v => v.Type.FullName == "System.Int32")
            ? body.Variables.First(v => v.Type.FullName == "System.Int32")
            : null;
        if (indexLocal is null)
        {
            indexLocal = new Local(module.CorLibTypes.Int32);
            body.Variables.Add(indexLocal);
        }

        var buttonLocal = body.Variables.Any(v => v.Type.FullName == "PCL.MyTextButton")
            ? body.Variables.First(v => v.Type.FullName == "PCL.MyTextButton")
            : null;
        if (buttonLocal is null)
        {
            buttonLocal = new Local(buttonType.ToTypeSig());
            body.Variables.Add(buttonLocal);
        }

        body.InitLocals = true;

        var headCheck = LoadLocal(body, indexLocal);
        var increment = LoadLocal(body, indexLocal);
        var end = Instruction.Create(OpCodes.Nop);

        var guard = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getPanAlways),
            Instruction.Create(OpCodes.Brfalse, end),
            Instruction.Create(OpCodes.Ldc_I4_0),
            StoreLocal(body, indexLocal),
            headCheck,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getPanAlways),
            Instruction.Create(OpCodes.Callvirt, getChildren),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Bge, end),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getPanAlways),
            Instruction.Create(OpCodes.Callvirt, getChildren),
            LoadLocal(body, indexLocal),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Isinst, buttonType),
            StoreLocal(body, buttonLocal),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Brfalse, increment),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Ldstr, LinkNoticeText),
            Instruction.Create(OpCodes.Callvirt, setText),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Ldstr, LinkNoticeTip),
            Instruction.Create(OpCodes.Callvirt, setToolTip),
            Instruction.Create(OpCodes.Br, end),
            increment,
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            StoreLocal(body, indexLocal),
            Instruction.Create(OpCodes.Br, headCheck),
            end
        };

        var insertAt = body.Instructions.IndexOf(loadComponent) + 1;
        for (var i = 0; i < guard.Count; i++) body.Instructions.Insert(insertAt + i, guard[i]);

        SetMaxStack(body);
        return 1;
    }

    private static void VerifyLinkNoticeText(ModuleDef module)
    {
        var body = InitializeComponentBody(module);
        if (!HasString(body, LinkNoticeText))
            throw new InvalidOperationException("notice text missing from InitializeComponent");
        if (!body.Instructions.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod textSetter && textSetter.Name == "set_Text"))
            throw new InvalidOperationException("notice text is not written to MyTextButton.Text");
        if (!body.Instructions.Any(i => i.OpCode == OpCodes.Isinst && i.Operand is ITypeDefOrRef button && button.Name == "MyTextButton"))
            throw new InvalidOperationException("notice lookup is not type checked");
    }

    private static CilBody InitializeComponentBody(ModuleDef module)
        => FindMethod(FindType(module, "PCL.PageLinkMain"), "InitializeComponent").Body
           ?? throw new InvalidOperationException("PageLinkMain.InitializeComponent has no body");

    // ---------------------------------------------------------------------
    // 设置 → 联机 → 自定义节点: the text box only carries its long explanation
    // as a ToolTip (PageSetupLink.xaml), and that text still points at the dead
    // public nodes. Rewrite the ToolTip right after the page is loaded.
    // ---------------------------------------------------------------------
    private const string LinkPeerHint =
        "PCL 官方的 EasyTier 公共节点（uptime.easytier.cn、easytier.meloong.com）已经停止服务。\n" +
        "请先在一台有公网 IP 的服务器上自建 EasyTier 节点，再把节点地址填在这里，PCL 就会连接它。\n\n" +
        "多个节点用逗号分隔，例如：\n" +
        "tcp://你的服务器地址:11010\n\n" +
        "房主和加入方必须填写相同的节点，才能互相连上。";

    private static ProbeResult ProbeLinkPeerHint(ModuleDef module)
    {
        var body = SetupLinkComponentBody(module);
        if (HasString(body, LinkPeerHint)) return new(PatchState.AlreadyPatched, "custom-peer hint already rewritten");
        return body.Instructions.Any(i => i.OpCode.Code is Code.Call or Code.Callvirt && i.Operand is IMethod m && m.Name == "LoadComponent")
            ? new(PatchState.Ready, "PageSetupLink InitializeComponent anchor found")
            : new(PatchState.Missing, "PageSetupLink InitializeComponent anchor not found");
    }

    private static int PatchLinkPeerHint(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageSetupLink");
        var body = SetupLinkComponentBody(module);
        var getTextCustomPeer = FindMethod(type, "get_TextCustomPeer");
        var setToolTip = FindMemberRef(module, "System.Windows.FrameworkElement", "set_ToolTip", 1);

        var loadComponent = body.Instructions.FirstOrDefault(i => i.OpCode.Code is Code.Call or Code.Callvirt &&
                                                                  i.Operand is IMethod m && m.Name == "LoadComponent")
                            ?? throw new InvalidOperationException("PageSetupLink InitializeComponent has no LoadComponent call");

        var patch = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getTextCustomPeer),
            Instruction.Create(OpCodes.Ldstr, LinkPeerHint),
            Instruction.Create(OpCodes.Callvirt, setToolTip)
        };

        var insertAt = body.Instructions.IndexOf(loadComponent) + 1;
        for (var i = 0; i < patch.Count; i++) body.Instructions.Insert(insertAt + i, patch[i]);

        SetMaxStack(body);
        return 1;
    }

    private static void VerifyLinkPeerHint(ModuleDef module)
    {
        var body = SetupLinkComponentBody(module);
        if (!HasString(body, LinkPeerHint))
            throw new InvalidOperationException("custom-peer hint missing from PageSetupLink.InitializeComponent");
        if (!body.Instructions.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod m && m.Name == "set_ToolTip"))
            throw new InvalidOperationException("custom-peer hint is not assigned to ToolTip");
    }

    // ---------------------------------------------------------------------
    // 设置 → 联机 → 自定义节点 下面的两个按钮：隐藏“节点状态”（uptime 站点已挂），
    // 把“光荣地贡献节点！”改成 EasyTier 官网链接。两个按钮都没有 x:Name，
    // 所以在加载后从 PanMain 往下递归找 MyButton，按文字判断是哪一个。
    // ---------------------------------------------------------------------
    private const string LinkPeerButtonText = "EasyTier 官网";
    private const string LinkPeerButtonTip = "EasyTier 官方站点：下载、文档与自建节点的说明。";
    private const string LinkPeerButtonUrl = "https://easytier.cn/";
    private const string LinkPeerWalkerName = "FixLinkPeerButtons";

    private static ProbeResult ProbeLinkPeerButtons(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageSetupLink");
        if (type.Methods.Any(m => m.Name == LinkPeerWalkerName))
            return new(PatchState.AlreadyPatched, "custom-peer buttons already patched");
        var body = SetupLinkComponentBody(module);
        return body.Instructions.Any(i => i.OpCode.Code is Code.Call or Code.Callvirt && i.Operand is IMethod m && m.Name == "LoadComponent")
            ? new(PatchState.Ready, "PageSetupLink InitializeComponent anchor found")
            : new(PatchState.Missing, "PageSetupLink InitializeComponent anchor not found");
    }

    private static int PatchLinkPeerButtons(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageSetupLink");
        var body = SetupLinkComponentBody(module);
        var dependencyObject = FindTypeRefOrDef(module, "System.Windows.DependencyObject");

        var walker = new MethodDefUser(
            LinkPeerWalkerName,
            MethodSig.CreateStatic(module.CorLibTypes.Void, dependencyObject.ToTypeSig()),
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig);
        type.Methods.Add(walker);
        walker.Body = BuildPeerButtonWalker(module, walker);

        var getPanMain = FindMethod(type, "get_PanMain");
        var loadComponent = body.Instructions.FirstOrDefault(i => i.OpCode.Code is Code.Call or Code.Callvirt &&
                                                                  i.Operand is IMethod m && m.Name == "LoadComponent")
                            ?? throw new InvalidOperationException("PageSetupLink InitializeComponent has no LoadComponent call");

        var patch = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getPanMain),
            Instruction.Create(OpCodes.Call, walker)
        };

        var insertAt = body.Instructions.IndexOf(loadComponent) + 1;
        for (var i = 0; i < patch.Count; i++) body.Instructions.Insert(insertAt + i, patch[i]);

        SetMaxStack(body);
        return 1;
    }

    private static CilBody BuildPeerButtonWalker(ModuleDef module, MethodDef walker)
    {
        var buttonType = FindType(module, "PCL.MyButton");
        var panelType = FindTypeRefOrDef(module, "System.Windows.Controls.Panel");

        var getText = FindMethod(buttonType, "get_Text");
        var setText = FindMethod(buttonType, "set_Text");
        var setToolTip = FindMemberRef(module, "System.Windows.FrameworkElement", "set_ToolTip", 1);
        var setVisibility = FindMemberRef(module, "System.Windows.UIElement", "set_Visibility", 1);
        var getChildren = FindMemberRef(module, "System.Windows.Controls.Panel", "get_Children", 0);
        var getChild = FindMemberRef(module, "System.Windows.Controls.Decorator", "get_Child", 0);
        var decoratorType = FindTypeRefOrDef(module, "System.Windows.Controls.Decorator");
        var getCount = FindMemberRef(module, "System.Windows.Controls.UIElementCollection", "get_Count", 0);
        var getItem = FindMemberRef(module, "System.Windows.Controls.UIElementCollection", "get_Item", 1);
        var contains = FindMemberRef(module, "System.String", "Contains", 1);
        var isNullOrWhiteSpace = FindMemberRef(module, "System.String", "IsNullOrWhiteSpace", 1);
        var setEventData = FindMethod(FindType(module, "PCL.CustomEventService"), "SetEventData");
        if (setEventData.MethodSig.Params.Count != 2)
            throw new InvalidOperationException("CustomEventService.SetEventData signature changed");

        var body = new CilBody { MaxStack = 4, InitLocals = true };
        var buttonLocal = new Local(buttonType.ToTypeSig());
        var panelLocal = new Local(panelType.ToTypeSig());
        var decoratorLocal = new Local(FindTypeRefOrDef(module, "System.Windows.Controls.Decorator").ToTypeSig());
        var indexLocal = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(buttonLocal);
        body.Variables.Add(panelLocal);
        body.Variables.Add(decoratorLocal);
        body.Variables.Add(indexLocal);

        var notButton = Instruction.Create(OpCodes.Ldarg_0);
        var decoratorCheck = Instruction.Create(OpCodes.Ldarg_0);
        var retButton = Instruction.Create(OpCodes.Ret);
        var statusCheck = LoadLocal(body, buttonLocal);
        var checkHead = LoadLocal(body, indexLocal);
        var loopHead = LoadLocal(body, panelLocal);
        var panelDone = Instruction.Create(OpCodes.Ret);
        var retDone = Instruction.Create(OpCodes.Ret);

        void Emit(params Instruction[] instructions)
        {
            foreach (var instruction in instructions) body.Instructions.Add(instruction);
        }

        Emit(
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Isinst, buttonType),
            StoreLocal(body, buttonLocal),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Brfalse, notButton),

            // 文字可能为 null，Contains 会抛空引用
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Callvirt, getText),
            Instruction.Create(OpCodes.Call, isNullOrWhiteSpace),
            Instruction.Create(OpCodes.Brtrue, retButton),

            // “光荣地贡献节点！”→ EasyTier 官网
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Callvirt, getText),
            Instruction.Create(OpCodes.Ldstr, "贡献"),
            Instruction.Create(OpCodes.Callvirt, contains),
            Instruction.Create(OpCodes.Brfalse, statusCheck),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Ldstr, LinkPeerButtonText),
            Instruction.Create(OpCodes.Callvirt, setText),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Ldstr, LinkPeerButtonTip),
            Instruction.Create(OpCodes.Callvirt, setToolTip),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Ldstr, LinkPeerButtonUrl),
            Instruction.Create(OpCodes.Call, setEventData),
            Instruction.Create(OpCodes.Ret),

            // “节点状态”→ 隐藏
            statusCheck,
            Instruction.Create(OpCodes.Callvirt, getText),
            Instruction.Create(OpCodes.Ldstr, "状态"),
            Instruction.Create(OpCodes.Callvirt, contains),
            Instruction.Create(OpCodes.Brfalse, retButton),
            LoadLocal(body, buttonLocal),
            Instruction.Create(OpCodes.Ldc_I4_2),
            Instruction.Create(OpCodes.Callvirt, setVisibility),
            retButton,

            // 不是按钮：是 Panel 就继续往下找
            notButton,
            Instruction.Create(OpCodes.Isinst, panelType),
            StoreLocal(body, panelLocal),
            LoadLocal(body, panelLocal),
            Instruction.Create(OpCodes.Brfalse, decoratorCheck),
            Instruction.Create(OpCodes.Ldc_I4_0),
            StoreLocal(body, indexLocal),

            // while (i < panel.Children.Count)：先判空再取，空面板不能 get_Item(0)
            checkHead,
            LoadLocal(body, panelLocal),
            Instruction.Create(OpCodes.Callvirt, getChildren),
            Instruction.Create(OpCodes.Callvirt, getCount),
            Instruction.Create(OpCodes.Bge, panelDone),

            loopHead,
            Instruction.Create(OpCodes.Callvirt, getChildren),
            LoadLocal(body, indexLocal),
            Instruction.Create(OpCodes.Callvirt, getItem),
            Instruction.Create(OpCodes.Call, walker),
            LoadLocal(body, indexLocal),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            StoreLocal(body, indexLocal),
            Instruction.Create(OpCodes.Br, checkHead),

            panelDone,

            // 卡片内容可能包在 Border 之类（Decorator）里，跟着 Child 再走一层
            decoratorCheck,
            Instruction.Create(OpCodes.Isinst, decoratorType),
            StoreLocal(body, decoratorLocal),
            LoadLocal(body, decoratorLocal),
            Instruction.Create(OpCodes.Brfalse, retDone),
            LoadLocal(body, decoratorLocal),
            Instruction.Create(OpCodes.Callvirt, getChild),
            Instruction.Create(OpCodes.Brfalse, retDone),
            LoadLocal(body, decoratorLocal),
            Instruction.Create(OpCodes.Callvirt, getChild),
            Instruction.Create(OpCodes.Call, walker),
            retDone);

        return body;
    }

    private static void VerifyLinkPeerButtons(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageSetupLink");
        var walker = type.Methods.FirstOrDefault(m => m.Name == LinkPeerWalkerName)
                     ?? throw new InvalidOperationException("peer button walker is missing");
        if (!walker.IsStatic || walker.MethodSig.Params.Count != 1 || walker.Body is null)
            throw new InvalidOperationException("peer button walker signature changed");
        if (!walker.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string url && url == LinkPeerButtonUrl))
            throw new InvalidOperationException("peer button walker lost the EasyTier url");
        if (!walker.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string hide && hide == "状态"))
            throw new InvalidOperationException("peer button walker no longer hides the status button");

        var body = SetupLinkComponentBody(module);
        if (!body.Instructions.Any(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == LinkPeerWalkerName))
            throw new InvalidOperationException("PageSetupLink.InitializeComponent does not call the walker");
    }

    private static CilBody SetupLinkComponentBody(ModuleDef module)
        => FindMethod(FindType(module, "PCL.PageSetupLink"), "InitializeComponent").Body
           ?? throw new InvalidOperationException("PageSetupLink.InitializeComponent has no body");

    private static Instruction LoadLocal(CilBody body, Local local)
        => body.Variables.IndexOf(local) switch
        {
            0 => Instruction.Create(OpCodes.Ldloc_0),
            1 => Instruction.Create(OpCodes.Ldloc_1),
            2 => Instruction.Create(OpCodes.Ldloc_2),
            3 => Instruction.Create(OpCodes.Ldloc_3),
            _ => Instruction.Create(OpCodes.Ldloc, local)
        };

    private static Instruction StoreLocal(CilBody body, Local local)
        => body.Variables.IndexOf(local) switch
        {
            0 => Instruction.Create(OpCodes.Stloc_0),
            1 => Instruction.Create(OpCodes.Stloc_1),
            2 => Instruction.Create(OpCodes.Stloc_2),
            3 => Instruction.Create(OpCodes.Stloc_3),
            _ => Instruction.Create(OpCodes.Stloc, local)
        };
}




