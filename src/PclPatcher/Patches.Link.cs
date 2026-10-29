namespace PclPatcher;

internal static partial class Program
{
    private const string LinkNodeTitle = "未填写自定义节点";
    private const string LinkNodeHint =
        "请先在 设置 → 联机 → 自定义节点 中填写与房主相同的节点地址（例如 tcp://183.66.27.22:39409），再创建或加入房间。";

    // ---------------------------------------------------------------------
    // Patch 6: the 联机 entry is force-collapsed in PageSetupUI.HiddenRefresh,
    // regardless of the normal "hidden pages" settings. Flip those two
    // unconditional Collapsed assignments back to Visible.
    // ---------------------------------------------------------------------
    private static ProbeResult ProbeRestoreLinkEntry(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageSetupUI"), "HiddenRefresh").Body
                   ?? throw new InvalidOperationException("HiddenRefresh has no body");

        var collapsed = CountLinkVisibility(body, Code.Ldc_I4_2);
        var visible = CountLinkVisibility(body, Code.Ldc_I4_0);
        if (collapsed == 2) return new(PatchState.Ready, "联机 tab/item are Collapsed");
        if (collapsed == 0 && visible == 2) return new(PatchState.AlreadyPatched, "联机 tab/item already Visible");
        return new(PatchState.Missing, $"found collapsed={collapsed}, visible={visible}, expected 2/0");
    }

    private static int PatchRestoreLinkEntry(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageSetupUI"), "HiddenRefresh").Body
                   ?? throw new InvalidOperationException("HiddenRefresh has no body");

        var changed = 0;
        foreach (var getter in new[] { "get_BtnTitleSelect2", "get_ItemLink" })
        {
            var index = FindLinkVisibility(body, getter, Code.Ldc_I4_2);
            if (index < 0) throw new InvalidOperationException($"{getter}: Collapsed anchor not found");
            body.Instructions[index].OpCode = OpCodes.Ldc_I4_0;
            body.Instructions[index].Operand = null;
            changed++;
        }

        SetMaxStack(body);
        return changed;
    }

    private static void VerifyRestoreLinkEntry(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageSetupUI"), "HiddenRefresh").Body
                   ?? throw new InvalidOperationException("HiddenRefresh has no body");
        foreach (var getter in new[] { "get_BtnTitleSelect2", "get_ItemLink" })
        {
            if (FindLinkVisibility(body, getter, Code.Ldc_I4_2) >= 0)
                throw new InvalidOperationException($"{getter} is still force-collapsed");
            if (FindLinkVisibility(body, getter, Code.Ldc_I4_0) < 0)
                throw new InvalidOperationException($"{getter} visibility was not restored");
        }
    }

    private static int CountLinkVisibility(CilBody body, Code code)
        => new[] { "get_BtnTitleSelect2", "get_ItemLink" }
            .Count(getter => FindLinkVisibility(body, getter, code) >= 0);

    private static int FindLinkVisibility(CilBody body, string getter, Code code)
    {
        var ins = body.Instructions;
        for (var i = 0; i + 2 < ins.Count; i++)
        {
            if (ins[i].OpCode != OpCodes.Callvirt ||
                ins[i].Operand is not IMethod method ||
                method.Name != getter) continue;
            if (ins[i + 1].OpCode.Code != code) continue;
            if (ins[i + 2].OpCode != OpCodes.Callvirt ||
                ins[i + 2].Operand is not IMethod setter ||
                setter.Name != "set_Visibility") continue;
            return i + 1;
        }
        return -1;
    }

    // ---------------------------------------------------------------------
    // Patch 7: InitConfig refuses to continue when the server config carries
    // Link.DisableReason2 (or a MinVersionCode newer than this build). Rename
    // those two lookup keys so the official config can no longer trip the
    // kill switch, without touching the rest of the config.
    // ---------------------------------------------------------------------
    private static ProbeResult ProbeLinkIgnoreDisable(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageLinkMain"), "InitConfig").Body
                   ?? throw new InvalidOperationException("InitConfig has no body");

        var hasOriginal = HasString(body, "DisableReason2") || HasString(body, "MinVersionCode");
        var hasSentinel = HasString(body, "DisableReason2Ignored") || HasString(body, "MinVersionCodeIgnored");
        if (hasSentinel && !hasOriginal) return new(PatchState.AlreadyPatched, "kill-switch keys already renamed");
        if (hasOriginal) return new(PatchState.Ready, "kill-switch lookup present");
        return new(PatchState.AlreadyPatched, "no kill-switch lookup to rename");
    }

    private static int PatchLinkIgnoreDisable(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageLinkMain"), "InitConfig").Body
                   ?? throw new InvalidOperationException("InitConfig has no body");

        var changed = 0;
        foreach (var ins in body.Instructions)
        {
            if (ins.OpCode != OpCodes.Ldstr || ins.Operand is not string s) continue;
            switch (s)
            {
                case "DisableReason2":
                    ins.Operand = "DisableReason2Ignored";
                    changed++;
                    break;
                case "MinVersionCode":
                    ins.Operand = "MinVersionCodeIgnored";
                    changed++;
                    break;
            }
        }

        if (!HasString(body, "DisableReason2Ignored") && !HasString(body, "MinVersionCodeIgnored"))
            throw new InvalidOperationException("kill-switch anchor not found");
        return changed;
    }

    private static void VerifyLinkIgnoreDisable(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageLinkMain"), "InitConfig").Body
                   ?? throw new InvalidOperationException("InitConfig has no body");
        if (HasString(body, "DisableReason2") || HasString(body, "MinVersionCode"))
            throw new InvalidOperationException("InitConfig still checks the original kill-switch keys");
        if (!HasString(body, "DisableReason2Ignored") && !HasString(body, "MinVersionCodeIgnored"))
            throw new InvalidOperationException("InitConfig sentinel key missing");
    }

    // ---------------------------------------------------------------------
    // Patch 8: there is no usable default node list any more. Rebuild
    // PageLinkMain.GetTargetPeers (only reached when LinkCustomPeer is empty)
    // so it shows a clear hint and aborts instead of touching the dead APIs.
    // ---------------------------------------------------------------------
    private static ProbeResult ProbeLinkRequireCustomPeer(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageLinkMain"), "GetTargetPeers").Body;
        if (body is null) return new(PatchState.Missing, "GetTargetPeers has no body");
        if (HasString(body, LinkNodeTitle)) return new(PatchState.AlreadyPatched, "custom-node gate already installed");
        return body.Instructions.Any(i => i.Operand is string s && s.Contains("源站获取节点列表失败", StringComparison.Ordinal))
            ? new(PatchState.Ready, "dead node-list fallback still present")
            : new(PatchState.Missing, "node-list fallback anchor not found");
    }

    private static int PatchLinkRequireCustomPeer(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageLinkMain");
        var method = FindMethod(type, "GetTargetPeers");
        var panic = FindMethod(type, "Panic");
        if (panic.MethodSig.Params.Count != 2)
            throw new InvalidOperationException("PageLinkMain.Panic signature changed");

        // Reuse the reference that already lives in the target module: importing
        // `typeof(...)` resolves against the *host* runtime, so a net10 patcher
        // emits a [System.Private.CoreLib] reference that .NET Framework cannot
        // load, and GetTargetPeers then fails at JIT time.
        var ctor = FindCoreLibCtor(module, "OperationCanceledException", 0);

        var body = new CilBody { MaxStack = 3 };
        body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, LinkNodeTitle));
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, LinkNodeHint));
        body.Instructions.Add(Instruction.Create(OpCodes.Call, panic));
        body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
        body.Instructions.Add(Instruction.Create(OpCodes.Throw));
        method.Body = body;
        return 1;
    }

    private static void VerifyLinkRequireCustomPeer(ModuleDef module)
    {
        var body = FindMethod(FindType(module, "PCL.PageLinkMain"), "GetTargetPeers").Body
                   ?? throw new InvalidOperationException("GetTargetPeers has no body");
        if (!HasString(body, LinkNodeTitle))
            throw new InvalidOperationException("custom-node hint missing");
        if (!body.Instructions.Any(i => i.OpCode == OpCodes.Throw))
            throw new InvalidOperationException("GetTargetPeers does not abort after the hint");
    }

    // ---------------------------------------------------------------------
    // Patch 9: ask for a node before the create/join dialogs, not after the
    // port dialog and module download. This is the first thing a user hits.
    // ---------------------------------------------------------------------
    private static ProbeResult ProbeLinkPrecheckCustomPeer(ModuleDef module)
    {
        var methods = LinkEntryMethods(module).ToList();
        var patched = methods.Count(m => m.HasBody && HasString(m.Body, LinkNodeTitle));
        if (patched == methods.Count) return new(PatchState.AlreadyPatched, "entry-level node gate already installed");
        if (patched == 0) return new(PatchState.Ready, "create/join entry anchors found");
        return new(PatchState.Missing, $"partial node gate: {patched}/{methods.Count}");
    }

    private static int PatchLinkPrecheckCustomPeer(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageLinkMain");
        var panic = FindMethod(type, "Panic");
        if (panic.MethodSig.Params.Count != 2 || panic.IsStatic)
            throw new InvalidOperationException("PageLinkMain.Panic signature changed");

        var settings = FindType(module, "PCL.Settings");
        var get = settings.Methods.FirstOrDefault(m =>
                      m.Name == "Get" && m.GenericParameters.Count == 1 && m.MethodSig.Params.Count == 2 &&
                      m.MethodSig.Params[1].FullName.Contains("McInstance", StringComparison.Ordinal))
                  ?? throw new InvalidOperationException("Settings.Get<T>(string, McInstance) not found");
        var getString = new MethodSpecUser(get, new GenericInstMethodSig(module.CorLibTypes.String));
        var isNullOrWhiteSpace = new Importer(module).Import(
            typeof(string).GetMethod("IsNullOrWhiteSpace", new[] { typeof(string) })
            ?? throw new InvalidOperationException("string.IsNullOrWhiteSpace not found"));
        var concat = new Importer(module).Import(
            typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) })
            ?? throw new InvalidOperationException("string.Concat(string,string) not found"));

        // Join() and Join(string) are static, so they have no `this` to pass to
        // the instance Panic(). Emitting ldarg.0 there produced InvalidProgram-
        // Exception at runtime (Join crashed as soon as it was JIT-compiled).
        var msgBox = FindType(module, "PCL.ModMain").Methods.FirstOrDefault(m =>
                         m.Name == "MyMsgBox" && m.IsStatic && m.MethodSig.Params.Count == 11 &&
                         m.MethodSig.Params[0].FullName == "System.String")
                     ?? throw new InvalidOperationException("ModMain.MyMsgBox(string x5, bool x3, Action x3) not found");

        var changed = 0;
        foreach (var method in LinkEntryMethods(module))
        {
            var body = method.Body ?? throw new InvalidOperationException($"{method.Name} has no body");
            if (HasString(body, LinkNodeTitle)) continue;

            var first = body.Instructions[0];
            var guard = new List<Instruction>
            {
                Instruction.Create(OpCodes.Ldstr, "LinkCustomPeer"),
                Instruction.Create(OpCodes.Ldnull),
                Instruction.Create(OpCodes.Call, getString),
                Instruction.Create(OpCodes.Call, isNullOrWhiteSpace),
                Instruction.Create(OpCodes.Brfalse, first)
            };

            if (method.IsStatic)
            {
                // Same title/content pair Panic() would use ("联机出错：" + brief).
                guard.AddRange(new[]
                {
                    Instruction.Create(OpCodes.Ldstr, LinkNodeHint),
                    Instruction.Create(OpCodes.Ldstr, "联机出错："),
                    Instruction.Create(OpCodes.Ldstr, LinkNodeTitle),
                    Instruction.Create(OpCodes.Call, concat),
                    Instruction.Create(OpCodes.Ldstr, "确定"),
                    Instruction.Create(OpCodes.Ldstr, ""),
                    Instruction.Create(OpCodes.Ldstr, ""),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Ldc_I4_1),
                    Instruction.Create(OpCodes.Ldc_I4_0),
                    Instruction.Create(OpCodes.Ldnull),
                    Instruction.Create(OpCodes.Ldnull),
                    Instruction.Create(OpCodes.Ldnull),
                    Instruction.Create(OpCodes.Call, msgBox),
                    Instruction.Create(OpCodes.Pop)
                });
            }
            else
            {
                guard.AddRange(new[]
                {
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldstr, LinkNodeTitle),
                    Instruction.Create(OpCodes.Ldstr, LinkNodeHint),
                    Instruction.Create(OpCodes.Call, panic)
                });
            }

            guard.Add(Instruction.Create(OpCodes.Ret));
            for (var i = 0; i < guard.Count; i++) body.Instructions.Insert(i, guard[i]);
            SetMaxStack(body);
            changed++;
        }

        if (changed == 0) throw new InvalidOperationException("no create/join entry needed the node gate");
        return changed;
    }

    private static void VerifyLinkPrecheckCustomPeer(ModuleDef module)
    {
        foreach (var method in LinkEntryMethods(module))
        {
            var body = method.Body ?? throw new InvalidOperationException($"{method.Name} has no body");
            if (!HasString(body, LinkNodeTitle))
                throw new InvalidOperationException($"{method.Name} is missing the node gate");
            if (body.Instructions[0].OpCode != OpCodes.Ldstr ||
                (string)body.Instructions[0].Operand != "LinkCustomPeer")
                throw new InvalidOperationException($"{method.Name} node gate is not at the entry");

            // The gate ends at its own ret; only that region is inspected.
            var ret = body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ret)
                      ?? throw new InvalidOperationException($"{method.Name} gate has no ret");
            var gate = body.Instructions.TakeWhile(i => i != ret).ToList();
            if (method.IsStatic)
            {
                if (gate.Any(i => i.OpCode.Code is Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3))
                    throw new InvalidOperationException($"{method.Name} static gate references an argument");
                if (!gate.Any(i => i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == "MyMsgBox"))
                    throw new InvalidOperationException($"{method.Name} static gate is missing MyMsgBox");
            }
        }
    }

    private static IEnumerable<MethodDef> LinkEntryMethods(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageLinkMain");
        yield return FindMethod(type, "Create_MouseLeftButtonUp");

        var joins = type.Methods.Where(m => m.Name == "Join").ToList();
        var noArg = joins.FirstOrDefault(m => m.MethodSig.Params.Count == 0)
                    ?? throw new InvalidOperationException("PageLinkMain.Join() not found");
        var codeArg = joins.FirstOrDefault(m => m.MethodSig.Params.Count == 1 &&
                                                m.MethodSig.Params[0].FullName == "System.String")
                      ?? throw new InvalidOperationException("PageLinkMain.Join(string) not found");
        yield return noArg;
        yield return codeArg;
    }

    /// <summary>
    /// Reuses an existing corlib-scoped .ctor reference. Building one with
    /// <see cref="Importer"/> binds it to the *host* runtime, so a net10 patcher
    /// would write [System.Private.CoreLib] into a .NET Framework assembly.
    /// </summary>
    private static IMethod FindCoreLibCtor(ModuleDef module, string typeName, int paramCount)
    {
        foreach (var reference in module.GetMemberRefs())
        {
            if (reference.Name != ".ctor") continue;
            if (reference.Signature is not MethodSig signature || signature.Params.Count != paramCount) continue;
            if (reference.DeclaringType is not TypeRef declaring) continue;
            if (declaring.Namespace != "System" || declaring.Name != typeName) continue;
            if (declaring.Scope is not AssemblyRef scope || scope.Name != module.CorLibTypes.AssemblyRef.Name) continue;
            return reference;
        }

        throw new InvalidOperationException(
            $"System.{typeName}..ctor/{paramCount} is not referenced by the target module");
    }

    /// <summary>
    /// Finds a member reference the target module already carries. Reusing it
    /// keeps new IL bound to the same assembly the file was compiled against.
    /// </summary>
    private static IMethod FindMemberRef(ModuleDef module, string declaringType, string name, int paramCount)
    {
        foreach (var reference in module.GetMemberRefs())
        {
            if (reference.Name != name) continue;
            if (reference.Signature is not MethodSig signature || signature.Params.Count != paramCount) continue;
            if (reference.DeclaringType?.FullName != declaringType) continue;
            return reference;
        }

        throw new InvalidOperationException(
            $"{declaringType}::{name}/{paramCount} is not referenced by the target module");
    }

    private static bool HasString(CilBody body, string value)
        => body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && s == value);
}
