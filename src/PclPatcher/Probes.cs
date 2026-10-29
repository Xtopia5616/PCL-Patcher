namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // Capability probes: never mutate; decide whether a step is applicable.
    // ---------------------------------------------------------------------
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
        var promptEmpty = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .Any(m => m.Body.Instructions.Any(i => i.Operand is string s && s == "Minecraft 更新提示") &&
                      m.Body.Instructions.Any(i => i.Operand is string s2 && s2.Length == 0));
        var promptDownload = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .Any(m => m.Body.Instructions.Any(i => i.Operand is string s && s == "Minecraft 更新提示") &&
                      m.Body.Instructions.Any(i => i.Operand is string s2 && s2 == "下载"));
        var selectLeft = FindMethod(FindType(module, "PCL.PageSelectLeft"), "McFolderListUI")
            .Body.Instructions.Any(i => i.Operand is string s && s == "导入整合包");

        if (promptDownload) return new(PatchState.AlreadyPatched, "update prompt already patched");
        if (promptEmpty && selectLeft) return new(PatchState.Ready, "pre-patch UI anchors");
        return new(PatchState.Missing, "UI entry anchors missing");
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
