namespace PclPatcher;

internal static partial class Program
{
    // PCL 把 CurseForge 的 API 地址写死成 https://api.curseforge.com，而
    // 资源搜索（下载页的“整合包/Mod/资源包”列表）走的是 ModNet.SendRequest，
    // 不经过 ModDownload.DlModRequest 的“官方源 → 镜像源”回退，所以官方源
    // 一旦连不上（TLS 被重置），列表就只剩 Modrinth 的结果。
    //
    // PCL 自己已经内置了 MCIMirror 的 CurseForge 反代（ModDownload.DlSourceModGet
    // 里就是 mod.mcimirror.top/curseforge），这里在 SendRequest 入口把 API 主机
    // 统一改写到该反代，让搜索、详情、版本列表、指纹查询都不再依赖官方 API 的
    // 连通性。文件下载地址（edge.forgecdn.net 等）不受影响，仍然直连。
    private const string CurseForgeApiHost = "https://api.curseforge.com";
    private const string CurseForgeApiMirror = "https://mod.mcimirror.top/curseforge";

    private static ProbeResult ProbeCurseForgeMirror(ModuleDef module)
    {
        var method = FindType(module, "PCL.ModNet").Methods
            .FirstOrDefault(m => m.Name == "SendRequest" && m.MethodSig is { Params.Count: 7 });
        if (method?.Body is null) return new(PatchState.Missing, "ModNet.SendRequest/7 not found");

        var body = method.Body;
        if (body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && s == CurseForgeApiMirror))
            return new(PatchState.AlreadyPatched, "CurseForge API already rerouted");

        return FindStringReplaceRef(module) is not null
            ? new(PatchState.Ready, "SendRequest entry + string.Replace anchor found")
            : new(PatchState.Missing, "System.String::Replace(string,string) reference not found");
    }

    private static int PatchCurseForgeMirror(ModuleDef module)
    {
        var method = FindType(module, "PCL.ModNet").Methods
            .First(m => m.Name == "SendRequest" && m.MethodSig is { Params.Count: 7 });
        var body = method.Body ?? throw new InvalidOperationException("SendRequest has no body");
        var replace = FindStringReplaceRef(module)
                      ?? throw new InvalidOperationException("System.String::Replace(string,string) reference not found");

        var prologue = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, CurseForgeApiHost),
            Instruction.Create(OpCodes.Ldstr, CurseForgeApiMirror),
            Instruction.Create(OpCodes.Callvirt, replace),
            Instruction.Create(OpCodes.Starg, method.Parameters[0])
        };

        for (var i = 0; i < prologue.Count; i++) body.Instructions.Insert(i, prologue[i]);
        SetMaxStack(body);
        return 1;
    }

    private static void VerifyCurseForgeMirror(ModuleDef module)
    {
        var method = FindType(module, "PCL.ModNet").Methods
            .First(m => m.Name == "SendRequest" && m.MethodSig is { Params.Count: 7 });
        var ins = method.Body?.Instructions ?? throw new InvalidOperationException("SendRequest has no body");
        if (ins.Count < 5 ||
            ins[0].OpCode != OpCodes.Ldarg_0 ||
            ins[1].OpCode != OpCodes.Ldstr || ins[1].Operand is not string host || host != CurseForgeApiHost ||
            ins[2].OpCode != OpCodes.Ldstr || ins[2].Operand is not string mirror || mirror != CurseForgeApiMirror ||
            ins[3].OpCode != OpCodes.Callvirt || ins[3].Operand is not IMethod replace || replace.Name != "Replace" ||
            ins[4].OpCode.Code is not (Code.Starg or Code.Starg_S))
            throw new InvalidOperationException("CurseForge reroute prologue missing from SendRequest");
    }

    private static IMethod? FindStringReplaceRef(ModuleDef module)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var ins in method.Body.Instructions)
        {
            if (ins.Operand is IMethod target &&
                target.Name == "Replace" &&
                target.DeclaringType?.FullName == "System.String" &&
                target.MethodSig is { Params.Count: 2 } sig &&
                sig.Params.All(p => p.FullName == "System.String"))
                return target;
        }
        return null;
    }
}
