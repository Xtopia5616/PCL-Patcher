namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // Default step: force the self-updater onto the full-package branch.
    //
    // PCL builds its incremental-patch URL from the *local* exe:
    //   patches/{build_type} {version} {pcl_md5}.patch
    // A patched exe has a different MD5, so that URL 404s and the launcher
    // reports "未找到更新包 / 不是官方版本". The full package in
    // ServerConfig.Update.Release.DownloadUrls has no hash in its path, so
    // routing the update gateway to DownloadUrls keeps auto-update working
    // (it overwrites the patched exe with the official one, as expected).
    //
    // Shape being changed (inside the update task lambda):
    //   ldsfld   ModSecret::UpdateMark
    //   ldstr    "Empty"
    //   [ldc.i4.0]
    //   call     StringExtensions::StartsWithF   ; leaves bool on stack
    //   brfalse  -> DownloadUrls block
    //
    // Replaced with:
    //   pop                                      ; drop the bool
    //   br       -> DownloadUrls block
    //
    // The "pop" is mandatory: brfalse consumes the bool, plain br does not, and
    // a branch reaching the join point with a non-empty stack makes the method
    // fail JIT with InvalidProgramException.
    // ---------------------------------------------------------------------
    private static (MethodDef Method, Instruction Branch) FindUpdateRoute(ModuleDef module, bool allowPatched)
    {
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                var ins = method.Body.Instructions;
                for (var i = 0; i + 4 < ins.Count; i++)
                {
                    if (ins[i].OpCode != OpCodes.Ldsfld) continue;
                    if (ins[i].Operand is not IField fld || fld.Name != "UpdateMark") continue;
                    if (ins[i + 1].OpCode != OpCodes.Ldstr) continue;
                    if (ins[i + 1].Operand is not string mark || mark != "Empty") continue;

                    var j = i + 2;
                    if (ins[j].OpCode == OpCodes.Ldc_I4_0) j++;   // ignoreCase argument
                    if (j + 1 >= ins.Count) continue;
                    if (ins[j].OpCode != OpCodes.Call && ins[j].OpCode != OpCodes.Callvirt) continue;
                    if (ins[j].Operand is not IMethod callee || callee.Name != "StartsWithF") continue;

                    // Patched shape: call; pop; br
                    if (j + 2 < ins.Count && ins[j + 1].OpCode == OpCodes.Pop && IsBranch(ins[j + 2]))
                    {
                        if (allowPatched) return (method, ins[j + 2]);
                        continue;
                    }

                    if (!IsBranch(ins[j + 1])) continue;
                    var branch = ins[j + 1];
                    if (branch.OpCode != OpCodes.Brfalse_S && branch.OpCode != OpCodes.Brfalse) continue;
                    return (method, branch);
                }
            }
        }

        throw new InvalidOperationException("UpdateMark/StartsWithF branch not found");
    }

    private static bool IsBranch(Instruction ins) =>
        ins.OpCode == OpCodes.Br || ins.OpCode == OpCodes.Br_S ||
        ins.OpCode == OpCodes.Brfalse || ins.OpCode == OpCodes.Brfalse_S;

    private static bool TargetsDownloadUrls(Instruction branch, CilBody body)
    {
        if (branch.Operand is not Instruction target) return false;
        var ins = body.Instructions;
        var index = ins.IndexOf(target);
        if (index < 0) return false;

        for (var k = index; k < ins.Count && k < index + 3; k++)
            if (ins[k].Operand is string s && s == "DownloadUrls") return true;
        return false;
    }

    private static int PatchForceFullPackage(ModuleDef module)
    {
        var (method, branch) = FindUpdateRoute(module, allowPatched: false);
        var body = method.Body;
        var index = body.Instructions.IndexOf(branch);
        if (index < 0) throw new InvalidOperationException("branch instruction not in body");

        body.Instructions.Insert(index, OpCodes.Pop.ToInstruction());
        branch.OpCode = OpCodes.Br;
        return 1;
    }

    private static void VerifyForceFullPackage(ModuleDef module)
    {
        var (method, branch) = FindUpdateRoute(module, allowPatched: true);
        if (branch.OpCode != OpCodes.Br && branch.OpCode != OpCodes.Br_S)
            throw new InvalidOperationException("update route is still conditional");

        var body = method.Body;
        var index = body.Instructions.IndexOf(branch);
        if (index <= 0 || body.Instructions[index - 1].OpCode != OpCodes.Pop)
            throw new InvalidOperationException("missing pop before the unconditional branch");

        if (!TargetsDownloadUrls(branch, body))
            throw new InvalidOperationException("update route does not target the DownloadUrls block");
    }
}
