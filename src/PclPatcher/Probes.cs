namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // Capability probes: never mutate; decide whether a step is applicable.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Machine readable per-step report used by the GUI when a file is selected:
    /// every step of the current option set with its Probe state, without
    /// touching the file. Unlike <c>--adopt</c> it needs no profile match, so an
    /// unlisted build still shows which steps are applicable.
    /// </summary>
    private static int ReportProbes(string source, bool includeLink, bool noUpdate)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("source not found", source);

        using var module = ModuleDefMD.Load(source);
        Console.WriteLine("probe\t1");
        Console.WriteLine($"version\t{FileVersionInfo.GetVersionInfo(source).FileVersion ?? "<unknown>"}");
        foreach (var step in BuildPlan(noUpdate, includeLink))
        {
            string state;
            string reason;
            try
            {
                var probe = step.Probe(module);
                (state, reason) = probe.State switch
                {
                    PatchState.Ready => ("ready", probe.Reason),
                    PatchState.AlreadyPatched => ("already", probe.Reason),
                    _ => ("missing", probe.Reason)
                };
            }
            catch (Exception ex)
            {
                state = "error";
                reason = ex.Message;
            }

            Console.WriteLine($"patch\t{step.Name}\t{state}\t{reason}");
        }
        return 0;
    }

    /// <summary>
    /// True when the module carries this tool's download patch: the three required
    /// anchors are already in their patched shape. Used to tell a patched copy
    /// apart from a newer executable that merely sits next to an old <c>.bak</c>;
    /// a build without the patch reports Ready for these steps instead.
    /// </summary>
    private static bool LooksPatched(ModuleDef module)
    {
        try
        {
            return ProbeDownloadLeftCtor(module).State == PatchState.AlreadyPatched
                   && ProbeModifyCard(module).State == PatchState.AlreadyPatched
                   && ProbeLoaderInit(module).State == PatchState.AlreadyPatched;
        }
        catch (Exception)
        {
            // Missing types/methods mean "not a patched copy of a known build".
            return false;
        }
    }

    private static ProbeResult ProbeDownloadLeftCtor(ModuleDef module)
    {
        var ctor = FindMethod(FindType(module, "PCL.PageDownloadLeft"), ".ctor");
        var body = ctor.Body ?? throw new InvalidOperationException("ctor has no body");
        var count = CountCallPattern(body, "get_ItemInstall", "set_Visibility", Code.Ldc_I4_2)
                    + CountCallPattern(body, "get_LabResource", "set_Visibility", Code.Ldc_I4_2)
                    + CountCallPattern(body, "get_ItemMod", "set_Checked", Code.Ldc_I4_1)
                    + CountPageIdPattern(body);

        if (count == 4) return new(PatchState.Ready, "4 hide statements");
        if (count == 0) return new(PatchState.AlreadyPatched, "no hide statements");
        return new(PatchState.Missing, $"found {count}/4 hide statements");
    }

    private static ProbeResult ProbeModifyCard(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageDownloadModify");
        var count = CountModifyCardPattern(type);
        if (count == 1) return new(PatchState.Ready, "CardMinecraft Collapsed");
        if (count == 0) return new(PatchState.AlreadyPatched, "no Collapsed CardMinecraft site");
        return new(PatchState.Missing, $"found {count} CardMinecraft visibility site(s)");
    }

    private static ProbeResult ProbeLoaderInit(ModuleDef module)
    {
        var page = FindType(module, "PCL.PageDownloadInstall");
        var method = FindMethod(page, "LoaderInit");
        var hasRealLoader = method.Body.Instructions.Any(i => i.Operand is IField f && f.Name == "DlClientListLoader");
        if (hasRealLoader) return new(PatchState.AlreadyPatched, "real DlClientListLoader already bound");

        var modDownload = module.Find("PCL.ModDownload", false);
        var hasField = modDownload?.Fields.Any(f => f.IsStatic && f.FieldType.FullName.Contains("DlClientListResult")) == true;
        var hasCallback = page.Methods.Any(m =>
            m.MethodSig is { Params.Count: 1 } &&
            m.MethodSig.Params[0].FullName.Contains("LoaderTask") &&
            m.HasBody &&
            m.Body.Instructions.Any(i => i.Operand is IMethod md && md.Name == "LoadMinecraft_OnFinish"));
        var hasPageLoaderInit = method.Body.Instructions.Any(i => i.Operand is IMethod md && md.Name == "PageLoaderInit");

        if (hasField && hasCallback && hasPageLoaderInit) return new(PatchState.Ready, "real loader anchors found");
        return new(PatchState.Missing, "DlClientListLoader/callback/PageLoaderInit anchor missing");
    }

    private static ProbeResult ProbeExtraUiEntries(ModuleDef module)
    {
        var pending = new List<string>();
        if (!IsUpdatePromptPatched(module)) pending.Add("update prompt");
        if (!IsModDisabledPatched(module)) pending.Add("mod-disabled button");
        if (!IsLaunchEmptyStatePatched(module)) pending.Add("launch empty state");
        var detailState = InspectResourceDetailInstall(module, out var detailReason);
        if (detailState == ResourceDetailInstallState.Missing) pending.Add("resource detail install");
        else if (detailState == ResourceDetailInstallState.WrongHandlerType)
            pending.Add("resource detail install (" + detailReason + ")");
        if (!IsSelectLeftImportPatched(module)) pending.Add("import modpack item");

        if (pending.Count == 0) return new(PatchState.AlreadyPatched, "all 5 UI sites already present");
        return new(PatchState.Ready, $"{pending.Count}/5 site(s) need patching: {string.Join(", ", pending)}");
    }

    // 2.99.0.0 still ships the pre-restriction shape for four of the five sites
    // (only the resource-detail install button was cut), so the probe has to be
    // per-site instead of keying off the update prompt alone.
    private static bool IsUpdatePromptPatched(ModuleDef module)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var ins = method.Body.Instructions;
            for (var i = 0; i < ins.Count; i++)
            {
                if (ins[i].OpCode != OpCodes.Ldstr || ins[i].Operand is not string s || s != "Minecraft 更新提示")
                    continue;

                // anchor on the "确定" button, then read the label that follows it
                for (var j = i + 1; j < Math.Min(i + 12, ins.Count); j++)
                {
                    if (ins[j].OpCode != OpCodes.Ldstr || ins[j].Operand is not string ok || ok != "确定") continue;
                    var next = ins.Skip(j + 1).Take(3).FirstOrDefault(x => x.OpCode == OpCodes.Ldstr);
                    return next?.Operand is string label && label == "下载";
                }
                return false;
            }
        }
        return false;
    }

    private static bool IsModDisabledPatched(ModuleDef module)
    {
        var ins = FindMethod(FindType(module, "PCL.PageInstanceModDisabled"), "BtnDownload_Loaded").Body?.Instructions;
        return ins is { Count: > 0 } &&
               ins[0].OpCode == OpCodes.Ldstr &&
               ins[0].Operand is string s && s == "UiHiddenPageDownload";
    }

    private static bool IsLaunchEmptyStatePatched(ModuleDef module)
    {
        var ins = FindMethod(FindType(module, "PCL.PageLaunchLeft"), "RefreshButtonsUI").Body?.Instructions;
        return ins?.Any(i => i.Operand is string s && s == "UiHiddenPageDownload") == true;
    }

    private static bool IsResourceDetailPatched(ModuleDef module)
        => InspectResourceDetailInstall(module, out _) == ResourceDetailInstallState.Patched;

    private static bool IsSelectLeftImportPatched(ModuleDef module)
    {
        var ins = FindMethod(FindType(module, "PCL.PageSelectLeft"), "McFolderListUI").Body?.Instructions;
        if (ins is null) return false;

        var titleIndex = -1;
        for (var i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Ldstr && ins[i].Operand is string s && s == "导入整合包")
            {
                titleIndex = i;
                break;
            }
        }
        if (titleIndex < 0) return false;

        for (var i = titleIndex; i < Math.Min(titleIndex + 30, ins.Count); i++)
        {
            if (ins[i].OpCode.Code is not (Code.Stloc_1 or Code.Stloc_S)) continue;
            var window = ins.Skip(i + 1).Take(12).ToList();
            return window.Any(x => x.OpCode == OpCodes.Ldsfld && x.Operand is IField f && f.Name == "FrmSelectLeft") &&
                   window.Any(x => x.Operand is IMethod m && m.Name == "get_PanList") &&
                   window.Any(x => x.Operand is IMethod m && m.Name == "Add");
        }
        return false;
    }

    private static ProbeResult ProbeModpackInstall(ModuleDef module)
    {
        var modpack = FindType(module, "PCL.ModModpack");
        var install = modpack.Methods.FirstOrDefault(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 3);
        if (install?.Body is null) return new(PatchState.Missing, "3-arg ModpackInstall missing");

        var hasRestriction = install.Body.Instructions.Any(i => i.Operand is string s && s.Contains("为遵守相关法律法规", StringComparison.Ordinal));
        var hasSwitch = install.Body.Instructions.Any(i => i.OpCode == OpCodes.Switch);
        if (hasSwitch && !hasRestriction) return new(PatchState.AlreadyPatched, "dispatch switch already present");

        var installMethods = modpack.Methods.Count(m => m.Name.String.StartsWith("InstallPack", StringComparison.Ordinal));
        if (hasRestriction && installMethods >= 7)
            return new(PatchState.Ready, "restriction throw + 7 install methods");

        return new(PatchState.Missing, "ModpackInstall shape mismatch");
    }

    private static ProbeResult ProbeForceFullPackage(ModuleDef module)
    {
        try
        {
            var (method, branch) = FindUpdateRoute(module, allowPatched: true);
            if (branch.OpCode == OpCodes.Br || branch.OpCode == OpCodes.Br_S)
                return TargetsDownloadUrls(branch, method.Body)
                    ? new(PatchState.AlreadyPatched, "updater already routed to DownloadUrls")
                    : new(PatchState.Missing, "unconditional branch does not target DownloadUrls");
            return new(PatchState.Ready, "UpdateMark gateway found");
        }
        catch (InvalidOperationException ex)
        {
            return new(PatchState.Missing, ex.Message);
        }
    }

    private static ProbeResult ProbeDisableSelfUpdate(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.ModSecret"), "UpdateStart");
        return method.Body.Instructions.Count == 1 && method.Body.Instructions[0].OpCode == OpCodes.Ret
            ? new(PatchState.AlreadyPatched, "UpdateStart already stubbed")
            : new(PatchState.Ready, "UpdateStart present");
    }

    private static int CountCallPattern(CilBody body, string getter, string setter, Code valueCode)
    {
        var ins = body.Instructions;
        var count = 0;
        for (var i = 1; i + 2 < ins.Count; i++)
        {
            if (ins[i - 1].OpCode != OpCodes.Ldarg_0) continue;
            if (ins[i].OpCode != OpCodes.Callvirt || ins[i].Operand is not IMethod get || get.Name != getter) continue;
            if (ins[i + 1].OpCode.Code != valueCode) continue;
            if (ins[i + 2].OpCode != OpCodes.Callvirt || ins[i + 2].Operand is not IMethod set || set.Name != setter) continue;
            count++;
        }
        return count;
    }

    private static int CountPageIdPattern(CilBody body)
    {
        var ins = body.Instructions;
        var count = 0;
        for (var i = 1; i + 1 < ins.Count; i++)
        {
            if (ins[i - 1].OpCode == OpCodes.Ldarg_0 &&
                ins[i].OpCode == OpCodes.Ldc_I4_2 &&
                ins[i + 1].OpCode == OpCodes.Stfld &&
                ins[i + 1].Operand is IField f && f.Name == "PageID")
                count++;
        }
        return count;
    }

    private static int CountModifyCardPattern(TypeDef type)
    {
        var count = 0;
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var ins = method.Body.Instructions;
            for (var i = 1; i + 1 < ins.Count; i++)
            {
                if (ins[i - 1].OpCode == OpCodes.Callvirt &&
                    ins[i - 1].Operand is IMethod getter && getter.Name == "get_CardMinecraft" &&
                    ins[i].OpCode == OpCodes.Ldc_I4_2 &&
                    ins[i + 1].OpCode == OpCodes.Callvirt &&
                    ins[i + 1].Operand is IMethod setter && setter.Name == "set_Visibility")
                    count++;
            }
        }
        return count;
    }
}
