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



