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
        // The five sites are independent: 2.99.0.0 still has four of them in the
        // pre-restriction shape and only lost the resource-detail install button,
        // while 2.99.0.1 lost all five. Patch whatever is missing, then verify
        // the whole set.
        var changes = 0;
        if (!IsUpdatePromptPatched(module)) changes += PatchUpdatePrompt(module);
        if (!IsModDisabledPatched(module)) changes += PatchModDisabledButton(module);
        if (!IsLaunchEmptyStatePatched(module)) changes += PatchLaunchEmptyState(module);
        if (!IsResourceDetailPatched(module)) changes += PatchResourceDetailInstall(module);
        if (!IsSelectLeftImportPatched(module)) changes += PatchSelectLeftImport(module);

        if (changes == 0)
            throw new InvalidOperationException("extra UI entry probe reported work but no site was patched");

        VerifyExtraUiEntries(module);
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

        var detailState = InspectResourceDetailInstall(module, out var detailReason);
        if (detailState != ResourceDetailInstallState.Patched)
            throw new InvalidOperationException("resource detail install: " + detailReason);

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

    internal enum ResourceDetailInstallState
    {
        Missing,
        WrongHandlerType,
        Patched
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

        // ResourceVersion.ToListItem takes two handlers that are *different* delegate
        // types with the same simple name:
        //   parameter 0: PCL.MyListItem/ClickEventHandler   (row click)
        //   parameter 1: PCL.MyIconButton/ClickEventHandler (the row's save-as icon)
        // Wrapping Install_Click in the list-item delegate and passing it as the icon
        // handler builds without complaint, but the row materialises through
        // MyIconButton.add_Click, which casts to its own nested type and throws
        // InvalidCastException ("[A]ClickEventHandler cannot be cast to
        // [B]ClickEventHandler") — the integrated-pack detail page crash.
        // Derive both constructors from the callee signature so neither slot can
        // drift to the other delegate again.
        var toListItem = FindMethod(FindType(module, "PCL.ResourceVersion"), "ToListItem");
        if (toListItem.MethodSig.Params.Count != 3 ||
            toListItem.MethodSig.Params[2].FullName != "System.Boolean")
            throw new InvalidOperationException("ResourceVersion.ToListItem signature changed");
        var rowCtor = DelegateCtor(toListItem, 0);
        var iconCtor = DelegateCtor(toListItem, 1);

        // Files patched by an older build of this tool carry the wrong delegate in
        // the icon slot; repair that in place instead of forcing a restore from .bak.
        var repaired = RepairResourceDetailHandlerTypes(ins, rowCtor, iconCtor);
        if (repaired > 0) return repaired;

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

            var addIns = ins[start + 9];
            var boolLocal = ins[start + 7].Operand as Local;
            var boolLoad = ins[start + 7].OpCode == OpCodes.Ldloc_1
                ? (Func<Instruction>)(() => OpCodes.Ldloc_1.ToInstruction())
                : () => OpCodes.Ldloc_S.ToInstruction(boolLocal!);

            var elseLabel = OpCodes.Ldarg_0.ToInstruction();
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
                OpCodes.Newobj.ToInstruction(rowCtor),
                OpCodes.Ldarg_0.ToInstruction(),
                OpCodes.Ldftn.ToInstruction(save),
                OpCodes.Newobj.ToInstruction(iconCtor),
                boolLoad(),
                OpCodes.Callvirt.ToInstruction((IMethod)ins[start + 8].Operand!),
                OpCodes.Br.ToInstruction(addIns),

                elseLabel,
                OpCodes.Ldftn.ToInstruction(save),
                OpCodes.Newobj.ToInstruction(rowCtor),
                OpCodes.Ldnull.ToInstruction(),
                boolLoad(),
                OpCodes.Callvirt.ToInstruction((IMethod)ins[start + 8].Operand!)
            };

            for (var k = 0; k < replacement.Count; k++) ins.Insert(start + k, replacement[k]);
            for (var k = 0; k < 9; k++) ins.RemoveAt(start + replacement.Count);
            SetMaxStack(body);
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Re-points an already patched icon slot that was built with the list-item
    /// delegate (the 2.99 crash) to PCL.MyIconButton/ClickEventHandler.
    /// </summary>
    private static int RepairResourceDetailHandlerTypes(IList<Instruction> ins, MethodDef rowCtor, MethodDef iconCtor)
    {
        for (var i = 0; i + 4 < ins.Count; i++)
        {
            if (ins[i].OpCode != OpCodes.Ldftn ||
                ins[i].Operand is not IMethod install || install.Name != "Install_Click") continue;
            if (ins[i + 1].OpCode != OpCodes.Newobj ||
                ins[i + 1].Operand is not IMethod row || !SameType(row.DeclaringType, rowCtor.DeclaringType)) continue;
            if (ins[i + 4].OpCode != OpCodes.Newobj ||
                ins[i + 4].Operand is not IMethod icon) continue;
            if (SameType(icon.DeclaringType, iconCtor.DeclaringType)) return 0;

            ins[i + 4] = OpCodes.Newobj.ToInstruction(iconCtor);
            return 1;
        }

        return 0;
    }

    private static ResourceDetailInstallState InspectResourceDetailInstall(ModuleDef module, out string reason)
    {
        var method = FindMethod(FindType(module, "PCL.PageDownloadResourceDetail"), "InitializeStackResourceVersions");
        var ins = method.Body?.Instructions
                  ?? throw new InvalidOperationException("InitializeStackResourceVersions has no body");
        var toListItem = FindMethod(FindType(module, "PCL.ResourceVersion"), "ToListItem");
        var rowCtor = DelegateCtor(toListItem, 0);
        var iconCtor = DelegateCtor(toListItem, 1);

        for (var i = 0; i + 4 < ins.Count; i++)
        {
            if (ins[i].OpCode != OpCodes.Ldftn ||
                ins[i].Operand is not IMethod install || install.Name != "Install_Click") continue;
            if (ins[i + 1].OpCode != OpCodes.Newobj ||
                ins[i + 1].Operand is not IMethod row || !SameType(row.DeclaringType, rowCtor.DeclaringType))
            {
                reason = "install handler is not wrapped in " + rowCtor.DeclaringType!.Name;
                return ResourceDetailInstallState.Missing;
            }
            if (ins[i + 4].OpCode != OpCodes.Newobj || ins[i + 4].Operand is not IMethod icon)
            {
                reason = "icon-button handler is not a delegate creation";
                return ResourceDetailInstallState.Missing;
            }
            if (!SameType(icon.DeclaringType, iconCtor.DeclaringType))
            {
                reason = $"icon button bound to {icon.DeclaringType?.FullName} instead of {iconCtor.DeclaringType!.FullName}";
                return ResourceDetailInstallState.WrongHandlerType;
            }

            reason = "install handler + save-as icon button";
            return ResourceDetailInstallState.Patched;
        }

        reason = "no install handler on the resource version rows";
        return ResourceDetailInstallState.Missing;
    }

    private static TypeDef DelegateParamType(MethodDef method, int index)
    {
        var param = method.MethodSig.Params[index];
        var type = param.ToTypeDefOrRef().ResolveTypeDef();
        if (type is null)
            throw new InvalidOperationException(
                $"{method.FullName} parameter #{index} ({param.FullName}) does not resolve inside this module");
        if (type.BaseType?.FullName != "System.MulticastDelegate")
            throw new InvalidOperationException(
                $"{method.FullName} parameter #{index} is {type.FullName}, not a delegate");
        return type;
    }

    private static MethodDef DelegateCtor(MethodDef method, int index)
        => DelegateParamType(method, index).FindMethod(".ctor")
           ?? throw new InvalidOperationException($"{method.FullName} parameter #{index} has no .ctor");

    private static bool SameType(ITypeDefOrRef? left, ITypeDefOrRef? right)
        => left is not null && right is not null &&
           string.Equals(left.FullName, right.FullName, StringComparison.Ordinal);

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

                // Only touch the label right after the "确定" button: the dialog
                // also carries an unrelated empty caption later on.
                for (var j = i + 1; j < Math.Min(i + 12, ins.Count); j++)
                {
                    if (ins[j].OpCode != OpCodes.Ldstr || ins[j].Operand is not string ok || ok != "确定") continue;
                    for (var k = j + 1; k < Math.Min(j + 4, ins.Count); k++)
                    {
                        if (ins[k].OpCode != OpCodes.Ldstr || ins[k].Operand is not string label) continue;
                        if (label.Length != 0) return 0;
                        ins[k] = OpCodes.Ldstr.ToInstruction("下载");
                        return 1;
                    }
                    return 0;
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
