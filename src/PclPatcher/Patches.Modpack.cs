namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // Patch 4: restore ModpackInstall's seven-way switch and no-arg entry.
    // ---------------------------------------------------------------------
    private static int PatchModpackInstall(ModuleDef module)
    {
        var changes = 0;
        changes += PatchModpackSwitch(module);
        changes += PatchModpackEntry(module);
        return changes;
    }

    private static void VerifyModpackInstall(ModuleDef module)
    {
        var modpack = FindType(module, "PCL.ModModpack");
        var install = modpack.Methods.First(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 3);
        var ins = install.Body.Instructions;
        if (ins.Any(i => i.Operand is string s && s.Contains("为遵守相关法律法规", StringComparison.Ordinal)))
            throw new InvalidOperationException("ModpackInstall still contains the restriction throw");
        if (!ins.Any(i => i.OpCode == OpCodes.Switch))
            throw new InvalidOperationException("ModpackInstall has no dispatch switch");
        if (!ins.Any(i => i.Operand is IMethod m && m.Name == "InstallPackCurseForge"))
            throw new InvalidOperationException("ModpackInstall has no CurseForge branch");

        var entry = modpack.Methods.First(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 0);
        if (!entry.Body.Instructions.Any(i => i.Operand is IMethod m && m.Name == "SelectFile"))
            throw new InvalidOperationException("ModpackInstall() has no file picker");
    }

    private static int PatchModpackSwitch(ModuleDef module)
    {
        var modpack = FindType(module, "PCL.ModModpack");
        var method = modpack.Methods.First(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 3);
        var body = method.Body ?? throw new InvalidOperationException("ModpackInstall has no body");
        var ins = body.Instructions;

        var start = -1;
        for (var i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Ldstr &&
                ins[i].Operand is string s && s.Contains("为遵守相关法律法规", StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }
        if (start < 0)
            throw new InvalidOperationException("ModpackInstall restriction throw not found; IL shape changed");

        if (ins[start + 1].OpCode != OpCodes.Newobj || ins[start + 2].OpCode != OpCodes.Throw)
            throw new InvalidOperationException("ModpackInstall restriction throw shape changed");

        var finallyStart = ins[start + 3];
        var loggerInfo = ins.Where(i => i.Operand is IMethod m && m.Name == "Info" && m.DeclaringType.FullName.Contains("Logger"))
            .Select(i => (IMethod)i.Operand!).First();
        var logPath = ins.Where(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && s.Contains("ModModpack.vb"))
            .Select(i => (string)i.Operand!).First();

        var retBlock = OpCodes.Ldloc_0.ToInstruction();

        List<Instruction> MakeCase(string message, IMethod install, bool instance, bool logo)
        {
            var list = new List<Instruction>
            {
                OpCodes.Ldstr.ToInstruction(message),
                OpCodes.Ldc_I4_0.ToInstruction(),
                OpCodes.Ldstr.ToInstruction(logPath),
                OpCodes.Call.ToInstruction(loggerInfo),
                OpCodes.Ldarg_0.ToInstruction(),
                OpCodes.Ldloc_1.ToInstruction(),
                OpCodes.Ldloc_2.ToInstruction()
            };
            if (instance) list.Add(OpCodes.Ldarg_1.ToInstruction());
            if (logo) list.Add(OpCodes.Ldarg_2.ToInstruction());
            list.Add(OpCodes.Call.ToInstruction(install));
            list.Add(OpCodes.Stloc_0.ToInstruction());
            list.Add(OpCodes.Leave.ToInstruction(retBlock));
            return list;
        }

        var endfinally = ins.Last(i => i.OpCode == OpCodes.Endfinally);
        var handlerEnd = OpCodes.Nop.ToInstruction();

        var case0List = MakeCase("整合包种类：CurseForge", FindMethod(modpack, "InstallPackCurseForge"), true, true);
        var case1List = MakeCase("整合包种类：HMCL", FindMethod(modpack, "InstallPackHMCL"), false, false);
        var case2List = MakeCase("整合包种类：MMC", FindMethod(modpack, "InstallPackMMC"), false, false);
        var case3List = MakeCase("整合包种类：MCBBS", FindMethod(modpack, "InstallPackMCBBS"), true, false);
        var case4List = MakeCase("整合包种类：Modrinth", FindMethod(modpack, "InstallPackModrinth"), true, true);
        var case9List = MakeCase("整合包种类：带启动器的压缩包", FindMethod(modpack, "InstallPackLauncherPack"), false, false);
        var caseDefaultList = MakeCase("整合包种类：未能识别，假定为压缩包", FindMethod(modpack, "InstallPackCompress"), false, false);

        var switchTargets = new Instruction[10];
        switchTargets[0] = case0List[0];
        switchTargets[1] = case1List[0];
        switchTargets[2] = case2List[0];
        switchTargets[3] = case3List[0];
        switchTargets[4] = case4List[0];
        switchTargets[5] = caseDefaultList[0];
        switchTargets[6] = caseDefaultList[0];
        switchTargets[7] = caseDefaultList[0];
        switchTargets[8] = caseDefaultList[0];
        switchTargets[9] = case9List[0];

        var block = new List<Instruction>
        {
            OpCodes.Ldloc_S.ToInstruction(body.Variables[4]),
            OpCodes.Switch.ToInstruction(switchTargets)
        };
        block.AddRange(caseDefaultList);
        block.AddRange(case0List);
        block.AddRange(case1List);
        block.AddRange(case2List);
        block.AddRange(case3List);
        block.AddRange(case4List);
        block.AddRange(case9List);

        // Replace the throw in place so exception-handler boundaries that point
        // at IL_02DA keep pointing at a valid instruction.
        var oldStart = ins[start];
        ins[start] = block[0];
        ReplaceTargets(body, oldStart, block[0]);
        for (var k = 1; k < block.Count; k++) ins.Insert(start + k, block[k]);
        for (var k = 0; k < 2; k++) ins.RemoveAt(start + block.Count);

        var endIndex = ins.IndexOf(endfinally);
        ins.Insert(endIndex + 1, handlerEnd);
        ins.Insert(endIndex + 2, retBlock);
        ins.Insert(endIndex + 3, OpCodes.Ret.ToInstruction());

        var outerFinally = body.ExceptionHandlers.FirstOrDefault(eh =>
            eh.HandlerType == ExceptionHandlerType.Finally && eh.HandlerStart == finallyStart);
        if (outerFinally is null) throw new InvalidOperationException("outer finally handler not found");
        outerFinally.HandlerEnd = handlerEnd;

        SetMaxStack(body);
        return 1;
    }

    private static int PatchModpackEntry(ModuleDef module)
    {
        var modpack = FindType(module, "PCL.ModModpack");
        var method = modpack.Methods.First(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 0);
        var install3 = modpack.Methods.First(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 3);

        var selectFile = FindMethodRef(module, "SelectFile")
                         ?? throw new InvalidOperationException("Dialogs.SelectFile not found");
        var firstOrDefault = FindGenericMethodRef(module, "FirstOrDefault", "System.String")
                             ?? throw new InvalidOperationException("Enumerable.FirstOrDefault<string> not found");

        // SelectFile(string, bool, string, Nullable<OneOf<...>>)
        var filterType = selectFile.MethodSig.Params[3];
        var filterLocal = new Local(filterType);

        var body = new CilBody { InitLocals = true, MaxStack = 6 };
        var fileLocal = new Local(module.CorLibTypes.String);
        body.Variables.Add(fileLocal);
        body.Variables.Add(filterLocal);

        var ret = OpCodes.Ret.ToInstruction();
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction("选择整合包压缩文件"));
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Ldloca_S.ToInstruction(filterLocal));
        body.Instructions.Add(OpCodes.Initobj.ToInstruction(filterType.ToTypeDefOrRef()));
        body.Instructions.Add(OpCodes.Ldloc_S.ToInstruction(filterLocal));
        body.Instructions.Add(OpCodes.Call.ToInstruction(selectFile));
        body.Instructions.Add(OpCodes.Call.ToInstruction(firstOrDefault));
        body.Instructions.Add(OpCodes.Stloc_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldloc_0.ToInstruction());
        body.Instructions.Add(OpCodes.Brfalse.ToInstruction(ret));
        body.Instructions.Add(OpCodes.Ldloc_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(install3));
        body.Instructions.Add(OpCodes.Pop.ToInstruction());
        body.Instructions.Add(ret);

        method.Body = body;
        SetMaxStack(body);
        return 1;
    }

    private static IMethod? FindGenericMethodRef(ModuleDef module, string name, string genericArg)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is MethodSpec spec &&
                spec.Method.Name == name &&
                spec.GenericInstMethodSig?.GenericArguments.Count == 1 &&
                spec.GenericInstMethodSig.GenericArguments[0].FullName == genericArg)
                return spec;
        }
        return null;
    }

    private static IMethod? FindMethodRef(ModuleDef module, string name)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is IMethod m && m.Name == name) return m;
        }
        return null;
    }
    private static int PatchLoaderInit(ModuleDef module)
    {
        var page = FindType(module, "PCL.PageDownloadInstall");
        var method = FindMethod(page, "LoaderInit");
        var controlInit = FindMethod(page, "ControlInit");
        var getLoad = FindMethod(page, "get_LoadMinecraft");
        var getPanLoad = FindMethod(page, "get_PanLoad");
        var getPanAllBack = FindMethod(page, "get_PanAllBack");

        var loader = FindLoaderField(module);
        var callback = FindCallback(page);
        var oldBody = method.Body ?? throw new InvalidOperationException("LoaderInit has no body");

        var actionCtor = oldBody.Instructions
            .Where(i => i.OpCode == OpCodes.Newobj && i.Operand is IMethod md &&
                        md.DeclaringType is not null && md.DeclaringType.FullName.Contains("System.Action"))
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault() ?? throw new InvalidOperationException("Action ctor not found");

        var pageLoaderInit = oldBody.Instructions
            .Where(i => i.OpCode == OpCodes.Call && i.Operand is IMethod md && md.Name == "PageLoaderInit")
            .Select(i => (IMethod)i.Operand)
            .FirstOrDefault() ?? throw new InvalidOperationException("PageLoaderInit call not found");

        var body = new CilBody { InitLocals = false, MaxStack = 9 };
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(controlInit));
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getLoad));
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getPanLoad));
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getPanAllBack));
        body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(loader));
        body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldftn.ToInstruction(callback));
        body.Instructions.Add(OpCodes.Newobj.ToInstruction(actionCtor));
        body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Ldc_I4_1.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(pageLoaderInit));
        body.Instructions.Add(OpCodes.Ret.ToInstruction());

        method.Body = body;
        SetMaxStack(body);
        return body.Instructions.Count;
    }

    private static void VerifyLoaderInit(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.PageDownloadInstall"), "LoaderInit");
        var body = method.Body ?? throw new InvalidOperationException("LoaderInit has no body");

        if (!body.Instructions.Any(i => i.Operand is IField f && f.Name == "DlClientListLoader"))
            throw new InvalidOperationException("LoaderInit does not reference DlClientListLoader");

        if (body.Instructions.Any(i => i.Operand is string s && s.Contains("关闭下载", StringComparison.Ordinal)))
            throw new InvalidOperationException("LoaderInit still contains the fake task");
    }

    private static FieldDef FindLoaderField(ModuleDef module)
    {
        var modDownload = FindType(module, "PCL.ModDownload");
        return modDownload.Fields.FirstOrDefault(f =>
                   f.IsStatic && f.FieldType.FullName.Contains("DlClientListResult"))
               ?? throw new InvalidOperationException("DlClientListLoader field not found");
    }

    private static MethodDef FindCallback(TypeDef page)
    {
        var callback = page.Methods.FirstOrDefault(m =>
            m.MethodSig is { Params.Count: 1 } &&
            m.MethodSig.Params[0].FullName.Contains("LoaderTask") &&
            m.HasBody &&
            m.Body.Instructions.Any(i => i.Operand is IMethod md && md.Name == "LoadMinecraft_OnFinish"));

        return callback ?? throw new InvalidOperationException("loader callback not found");
    }

    // ---------------------------------------------------------------------
    // Optional: stop PCL from replacing the patched exe with an official one.
    // ---------------------------------------------------------------------
    private static int PatchDisableSelfUpdate(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.ModSecret"), "UpdateStart");
        var body = new CilBody { InitLocals = false, MaxStack = 0 };
        body.Instructions.Add(OpCodes.Ret.ToInstruction());
        method.Body = body;
        return 1;
    }

    private static void VerifyDisableSelfUpdate(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.ModSecret"), "UpdateStart");
        if (method.Body.Instructions.Count != 1 || method.Body.Instructions[0].OpCode != OpCodes.Ret)
            throw new InvalidOperationException("UpdateStart was not stubbed");
    }
}
