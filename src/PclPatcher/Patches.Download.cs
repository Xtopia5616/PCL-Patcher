namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // Patch 1: remove the "hide download entry" statements from the ctor.
    // ---------------------------------------------------------------------
    private static int PatchDownloadLeftCtor(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageDownloadLeft");
        var ctor = FindMethod(type, ".ctor");
        var body = ctor.Body ?? throw new InvalidOperationException("ctor has no body");

        var removed = 0;
        removed += RemoveCallPattern(body, "get_ItemInstall", "set_Visibility", Code.Ldc_I4_2);
        removed += RemoveCallPattern(body, "get_LabResource", "set_Visibility", Code.Ldc_I4_2);
        removed += RemoveCallPattern(body, "get_ItemMod", "set_Checked", Code.Ldc_I4_1);
        removed += RemovePageIdPattern(body);

        if (removed is not (0 or 4))
            throw new InvalidOperationException($"PageDownloadLeft..ctor shape changed: removed {removed}/4 hide statements");

        SetMaxStack(body);
        return removed;
    }

    private static void VerifyDownloadLeftCtor(ModuleDef module)
    {
        var ctor = FindMethod(FindType(module, "PCL.PageDownloadLeft"), ".ctor");
        var body = ctor.Body ?? throw new InvalidOperationException("ctor has no body");
        var ins = body.Instructions;

        if (ins.Any(i => i.Operand is IMethod m &&
                         m.Name.String is "get_ItemInstall" or "get_LabResource" or "get_ItemMod"))
            throw new InvalidOperationException("ctor still contains UI-hiding getters");

        for (var i = 1; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Stfld && ins[i].Operand is IField f && f.Name == "PageID" &&
                ins[i - 1].OpCode == OpCodes.Ldc_I4_2)
                throw new InvalidOperationException("ctor still sets PageID to DownloadMod");
        }

        if (ins.Count == 0 || ins[^1].OpCode != OpCodes.Ret)
            throw new InvalidOperationException("ctor does not end with ret");
    }

    // ---------------------------------------------------------------------
    // Patch 2: PageDownloadModify's MC card visibility.
    // ---------------------------------------------------------------------
    private static int PatchModifyCard(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageDownloadModify");
        var changed = 0;

        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var ins = method.Body.Instructions;
            for (var i = 1; i + 1 < ins.Count; i++)
            {
                if (ins[i - 1].OpCode != OpCodes.Callvirt ||
                    ins[i - 1].Operand is not IMethod getter ||
                    getter.Name != "get_CardMinecraft") continue;
                if (ins[i].OpCode != OpCodes.Ldc_I4_2) continue;
                if (ins[i + 1].OpCode != OpCodes.Callvirt ||
                    ins[i + 1].Operand is not IMethod setter ||
                    setter.Name != "set_Visibility") continue;

                ins[i] = OpCodes.Ldc_I4_0.ToInstruction();
                changed++;
            }
        }

        if (changed != 1)
            throw new InvalidOperationException($"PageDownloadModify CardMinecraft shape changed: patched {changed} site(s)");

        return changed;
    }

    private static void VerifyModifyCard(ModuleDef module)
    {
        var type = FindType(module, "PCL.PageDownloadModify");
        var found = type.Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Any(i => i.OpCode == OpCodes.Ldc_I4_0);

        if (!found) throw new InvalidOperationException("CardMinecraft visibility was not patched");
    }

    // ---------------------------------------------------------------------
    // Patch 3: restore the remaining download entry points that were hidden
    // behind RestrictMcDownload (update prompt, mod-disabled page, launch page).
    // ---------------------------------------------------------------------
    private static int PatchExtraUiEntries(ModuleDef module)
    {
        var changes = 0;
        changes += PatchUpdatePrompt(module);
        changes += PatchModDisabledButton(module);
        changes += PatchLaunchEmptyState(module);
        changes += PatchResourceDetailInstall(module);
        changes += PatchSelectLeftImport(module);
        if (changes != 5)
            throw new InvalidOperationException($"extra UI entry shape changed: patched {changes}/5 site(s)");
        return changes;
    }

    private static void VerifyExtraUiEntries(ModuleDef module)
    {
        var disabled = FindMethod(FindType(module, "PCL.PageInstanceModDisabled"), "BtnDownload_Loaded");
        if (!disabled.Body.Instructions.Any(i => i.Operand is string s && s == "UiHiddenPageDownload"))
            throw new InvalidOperationException("PageInstanceModDisabled still hides the download button unconditionally");

        var launch = FindMethod(FindType(module, "PCL.PageLaunchLeft"), "RefreshButtonsUI");
        if (!launch.Body.Instructions.Any(i => i.Operand is string s && s == "UiHiddenPageDownload"))
            throw new InvalidOperationException("PageLaunchLeft still disables the download state unconditionally");

        var prompt = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .Any(m => m.Body.Instructions.Any(i => i.Operand is string s && s == "Minecraft 更新提示") &&
                      m.Body.Instructions.Any(i => i.Operand is string s2 && s2 == "下载"));
        if (!prompt) throw new InvalidOperationException("Minecraft update prompt still has no download button");

        var detail = FindMethod(FindType(module, "PCL.PageDownloadResourceDetail"), "InitializeStackResourceVersions");
        if (!detail.Body.Instructions.Any(i => i.Operand is IMethod m && m.Name == "Install_Click"))
            throw new InvalidOperationException("resource detail still has no install button");

        var selectLeft = FindMethod(FindType(module, "PCL.PageSelectLeft"), "McFolderListUI");
        var importAt = selectLeft.Body.Instructions
            .Select((ins, index) => (ins, index))
            .FirstOrDefault(x => x.ins.OpCode == OpCodes.Ldstr && x.ins.Operand is string s && s == "导入整合包");
        if (importAt.ins is null) throw new InvalidOperationException("import-modpack item not found");
        if (!selectLeft.Body.Instructions.Skip(importAt.index).Take(30)
                .Any(i => i.OpCode.Code is Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc_S))
            throw new InvalidOperationException("import-modpack item is not stored in a local");
    }

    private static int PatchSelectLeftImport(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.PageSelectLeft"), "McFolderListUI");
        var body = method.Body ?? throw new InvalidOperationException("McFolderListUI has no body");
        var ins = body.Instructions;

        var titleIndex = -1;
        for (var i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode == OpCodes.Ldstr && ins[i].Operand is string s && s == "导入整合包")
            {
                titleIndex = i;
                break;
            }
        }
        if (titleIndex < 0) return 0;

        var storeIndex = -1;
        for (var i = titleIndex; i < Math.Min(titleIndex + 30, ins.Count); i++)
        {
            if (ins[i].OpCode.Code is Code.Stloc_1 or Code.Stloc_S)
            {
                storeIndex = i;
                break;
            }
        }
        if (storeIndex < 0) return 0;

        var store = ins[storeIndex];
        var local = store.OpCode.Code switch
        {
            Code.Stloc_0 => body.Variables[0],
            Code.Stloc_1 => body.Variables[1],
            Code.Stloc_2 => body.Variables[2],
            Code.Stloc_3 => body.Variables[3],
            Code.Stloc_S when store.Operand is Local l => l,
            _ => throw new InvalidOperationException("unexpected import item store instruction")
        };

        var frm = ins.Skip(storeIndex).Select(i => i.Operand).OfType<IField>()
            .FirstOrDefault(f => f.Name.String == "FrmSelectLeft")
            ?? FindField(FindType(module, "PCL.ModMain"), "FrmSelectLeft");
        var getPanList = ins.Skip(storeIndex).Where(i => i.OpCode == OpCodes.Callvirt)
            .Select(i => (IMethod)i.Operand!).FirstOrDefault(m => m.Name == "get_PanList")
            ?? FindMethod(FindType(module, "PCL.PageSelectLeft"), "get_PanList");
        var getChildren = ins.Skip(storeIndex).Where(i => i.OpCode == OpCodes.Callvirt)
            .Select(i => (IMethod)i.Operand!).FirstOrDefault(m => m.Name == "get_Children");
        var add = ins.Skip(storeIndex).Where(i => i.OpCode == OpCodes.Callvirt)
            .Select(i => (IMethod)i.Operand!).FirstOrDefault(m => m.Name == "Add");
        if (getChildren is null || add is null) return 0;

        var replacement = new List<Instruction>
        {
            OpCodes.Ldsfld.ToInstruction(frm),
            OpCodes.Callvirt.ToInstruction(getPanList),
            OpCodes.Callvirt.ToInstruction(getChildren),
            OpCodes.Ldloc_S.ToInstruction(local),
            OpCodes.Callvirt.ToInstruction(add),
            OpCodes.Pop.ToInstruction()
        };

        for (var k = 0; k < replacement.Count; k++) ins.Insert(storeIndex + 1 + k, replacement[k]);
        SetMaxStack(body);
        return 1;
    }

    private static int PatchResourceDetailInstall(ModuleDef module)
    {
        var page = FindType(module, "PCL.PageDownloadResourceDetail");
        var method = FindMethod(page, "InitializeStackResourceVersions");
        var body = method.Body ?? throw new InvalidOperationException("InitializeStackResourceVersions has no body");
        var ins = body.Instructions;

        var targetField = FindField(page, "TargetResourceType");
        var install = FindMethod(page, "Install_Click");
        var save = FindMethod(page, "Save_Click");

        for (var start = 0; start + 10 < ins.Count; start++)
        {
            if (ins[start].OpCode != OpCodes.Ldarg_1) continue;
            if (ins[start + 1].OpCode != OpCodes.Callvirt ||
                ins[start + 1].Operand is not IMethod children || children.Name != "get_Children") continue;
            if (ins[start + 2].OpCode.Code is not (Code.Ldloc_S or Code.Ldloc)) continue;
            if (ins[start + 3].OpCode != OpCodes.Ldarg_0) continue;
            if (ins[start + 4].OpCode != OpCodes.Ldftn ||
                ins[start + 4].Operand is not IMethod saved || saved.Name != "Save_Click") continue;
            if (ins[start + 5].OpCode != OpCodes.Newobj) continue;
            if (ins[start + 6].OpCode != OpCodes.Ldnull) continue;
            if (ins[start + 7].OpCode.Code is not (Code.Ldloc_1 or Code.Ldloc_S)) continue;
            if (ins[start + 8].OpCode != OpCodes.Callvirt ||
                ins[start + 8].Operand is not IMethod toList || toList.Name != "ToListItem") continue;
            if (ins[start + 9].OpCode != OpCodes.Callvirt ||
                ins[start + 9].Operand is not IMethod add || add.Name != "Add") continue;
            if (ins[start + 10].OpCode != OpCodes.Pop) continue;

            var clickCtor = (IMethod)ins[start + 5].Operand!;
            var addIns = ins[start + 9];
            var elseLabel = OpCodes.Ldarg_0.ToInstruction();
            var ldloc = ins[start + 7].OpCode == OpCodes.Ldloc_1
                ? OpCodes.Ldloc_1.ToInstruction()
                : OpCodes.Ldloc_S.ToInstruction((Local)ins[start + 7].Operand!);

            var replacement = new List<Instruction>
            {
                ins[start],
                ins[start + 1],
                ins[start + 2],
                OpCodes.Ldarg_0.ToInstruction(),
                OpCodes.Ldfld.ToInstruction(targetField),
                OpCodes.Ldc_I4_2.ToInstruction(),
                OpCodes.Bne_Un.ToInstruction(elseLabel),

                OpCodes.Ldarg_0.ToInstruction(),
                OpCodes.Ldftn.ToInstruction(install),
                OpCodes.Newobj.ToInstruction(clickCtor),
                OpCodes.Ldarg_0.ToInstruction(),
                OpCodes.Ldftn.ToInstruction(save),
                OpCodes.Newobj.ToInstruction(clickCtor),
                ins[start + 7],
                OpCodes.Callvirt.ToInstruction((IMethod)ins[start + 8].Operand!),
                OpCodes.Br.ToInstruction(addIns),

                elseLabel,
                OpCodes.Ldftn.ToInstruction(save),
                OpCodes.Newobj.ToInstruction(clickCtor),
                OpCodes.Ldnull.ToInstruction(),
                ldloc,
                OpCodes.Callvirt.ToInstruction((IMethod)ins[start + 8].Operand!)
            };

            for (var k = 0; k < replacement.Count; k++) ins.Insert(start + k, replacement[k]);
            for (var k = 0; k < 9; k++) ins.RemoveAt(start + replacement.Count);
            SetMaxStack(body);
            return 1;
        }

        return 0;
    }

    private static int PatchUpdatePrompt(ModuleDef module)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var ins = method.Body.Instructions;
            for (var i = 0; i < ins.Count; i++)
            {
                if (ins[i].OpCode != OpCodes.Ldstr || ins[i].Operand is not string s || s != "Minecraft 更新提示")
                    continue;

                for (var j = i + 1; j < Math.Min(i + 12, ins.Count); j++)
                {
                    if (ins[j].OpCode == OpCodes.Ldstr && ins[j].Operand is string text && text.Length == 0)
                    {
                        ins[j] = OpCodes.Ldstr.ToInstruction("下载");
                        return 1;
                    }
                }
            }
        }
        return 0;
    }

    private static int PatchModDisabledButton(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.PageInstanceModDisabled"), "BtnDownload_Loaded");
        var body = method.Body ?? throw new InvalidOperationException("BtnDownload_Loaded has no body");
        var ins = body.Instructions;
        if (ins.Count < 2 || ins[0].OpCode != OpCodes.Ldc_I4_2 || ins[1].OpCode != OpCodes.Stloc_0)
            throw new InvalidOperationException("unexpected BtnDownload_Loaded prologue");

        var settingsGet = FindSettingsGetBool(module);
        var hidden = FindMethod(FindType(module, "PCL.PageSetupUI"), "get_HiddenForceShow");
        var frm = FindField(FindType(module, "PCL.ModMain"), "FrmSelectRight");
        var showHidden = FindField(FindType(module, "PCL.PageSelectRight"), "ShowHidden");

        var checkShow = OpCodes.Ldsfld.ToInstruction(frm);
        var visible = OpCodes.Ldc_I4_0.ToInstruction();
        var collapsed = OpCodes.Ldc_I4_2.ToInstruction();
        var store = OpCodes.Stloc_0.ToInstruction();

        var replacement = new List<Instruction>
        {
            OpCodes.Ldstr.ToInstruction("UiHiddenPageDownload"),
            OpCodes.Ldnull.ToInstruction(),
            OpCodes.Call.ToInstruction(settingsGet),
            OpCodes.Brfalse.ToInstruction(checkShow),
            OpCodes.Call.ToInstruction(hidden),
            OpCodes.Brtrue.ToInstruction(checkShow),
            OpCodes.Ldc_I4_2.ToInstruction(),
            OpCodes.Br.ToInstruction(store),

            checkShow,
            OpCodes.Brfalse.ToInstruction(visible),
            OpCodes.Ldsfld.ToInstruction(frm),
            OpCodes.Ldfld.ToInstruction(showHidden),
            OpCodes.Brtrue.ToInstruction(collapsed),

            visible,
            OpCodes.Br.ToInstruction(store),
            collapsed,
            store
        };

        for (var i = 0; i < replacement.Count; i++) ins.Insert(i, replacement[i]);
        ins.RemoveAt(replacement.Count);
        ins.RemoveAt(replacement.Count);
        SetMaxStack(body);
        return 1;
    }

    private static int PatchLaunchEmptyState(ModuleDef module)
    {
        var method = FindMethod(FindType(module, "PCL.PageLaunchLeft"), "RefreshButtonsUI");
        var body = method.Body ?? throw new InvalidOperationException("RefreshButtonsUI has no body");
        var ins = body.Instructions;

        var settingsGet = FindSettingsGetBool(module);
        var hidden = FindMethod(FindType(module, "PCL.PageSetupUI"), "get_HiddenForceShow");

        for (var i = 0; i + 7 < ins.Count; i++)
        {
            if (ins[i].OpCode != OpCodes.Call || ins[i].Operand is not IMethod m || m.Name != "get_McInstanceSelected") continue;
            if (ins[i + 1].OpCode.Code is not (Code.Brtrue or Code.Brtrue_S)) continue;
            if (ins[i + 1].Operand is not Instruction trueTarget || trueTarget != ins[i + 5]) continue;
            if (ins[i + 2].OpCode != OpCodes.Ldc_I4_1 || ins[i + 3].OpCode != OpCodes.Stloc_0) continue;
            if (ins[i + 4].OpCode.Code is not (Code.Br or Code.Br_S)) continue;
            if (ins[i + 4].Operand is not Instruction joinTarget || joinTarget != ins[i + 7]) continue;
            if (ins[i + 5].OpCode != OpCodes.Ldc_I4_3 || ins[i + 6].OpCode != OpCodes.Stloc_0) continue;

            var next = ins[i + 7];
            var selected = OpCodes.Ldc_I4_3.ToInstruction();
            var show = OpCodes.Ldc_I4_2.ToInstruction();

            // Keep the original call instruction object in place: other branches
            // (e.g. IL_0030 bne.un) target it directly.
            var replacement = new List<Instruction>
            {
                OpCodes.Brtrue.ToInstruction(selected),
                OpCodes.Ldstr.ToInstruction("UiHiddenPageDownload"),
                OpCodes.Ldnull.ToInstruction(),
                OpCodes.Call.ToInstruction(settingsGet),
                OpCodes.Brfalse.ToInstruction(show),
                OpCodes.Call.ToInstruction(hidden),
                OpCodes.Brtrue.ToInstruction(show),
                OpCodes.Ldc_I4_1.ToInstruction(),
                OpCodes.Stloc_0.ToInstruction(),
                OpCodes.Br.ToInstruction(next),

                selected,
                OpCodes.Stloc_0.ToInstruction(),
                OpCodes.Br.ToInstruction(next),

                show,
                OpCodes.Stloc_0.ToInstruction(),
                OpCodes.Br.ToInstruction(next)
            };

            for (var k = 0; k < replacement.Count; k++) ins.Insert(i + 1 + k, replacement[k]);
            for (var k = 0; k < 6; k++) ins.RemoveAt(i + 1 + replacement.Count);
            SetMaxStack(body);
            return 1;
        }

        return 0;
    }

    private static IMethod FindSettingsGetBool(ModuleDef module)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is MethodSpec spec &&
                spec.Method.Name == "Get" &&
                spec.GenericInstMethodSig?.GenericArguments.Count == 1 &&
                spec.GenericInstMethodSig.GenericArguments[0].FullName == "System.Boolean")
                return spec;
        }

        throw new InvalidOperationException("Settings.Get(Of Boolean) call not found");
    }
}
