namespace PclPatcher;

internal static partial class Program
{
    // ---------------------------------------------------------------------
    // IL helpers and validation
    // ---------------------------------------------------------------------
    private static int RemoveCallPattern(CilBody body, string getter, string setter, Code valueCode)
    {
        var ins = body.Instructions;
        for (var i = 1; i + 2 < ins.Count; i++)
        {
            if (ins[i - 1].OpCode != OpCodes.Ldarg_0) continue;
            if (ins[i].OpCode != OpCodes.Callvirt ||
                ins[i].Operand is not IMethod get ||
                get.Name != getter) continue;
            if (ins[i + 1].OpCode.Code != valueCode) continue;
            if (ins[i + 2].OpCode != OpCodes.Callvirt ||
                ins[i + 2].Operand is not IMethod set ||
                set.Name != setter) continue;

            if (!CanRemoveRange(body, i - 1, 4)) return 0;
            for (var k = 0; k < 4; k++) ins.RemoveAt(i - 1);
            return 1;
        }
        return 0;
    }

    private static int RemovePageIdPattern(CilBody body)
    {
        var ins = body.Instructions;
        for (var i = 1; i + 1 < ins.Count; i++)
        {
            if (ins[i - 1].OpCode != OpCodes.Ldarg_0) continue;
            if (ins[i].OpCode != OpCodes.Ldc_I4_2) continue;
            if (ins[i + 1].OpCode != OpCodes.Stfld) continue;
            if (ins[i + 1].Operand is not IField f || f.Name != "PageID") continue;

            if (!CanRemoveRange(body, i - 1, 3)) return 0;
            for (var k = 0; k < 3; k++) ins.RemoveAt(i - 1);
            return 1;
        }
        return 0;
    }

    private static void ReplaceTargets(CilBody body, Instruction oldInstruction, Instruction newInstruction)
    {
        foreach (var ins in body.Instructions)
        {
            if (ReferenceEquals(ins.Operand, oldInstruction))
                ins.Operand = newInstruction;
            else if (ins.Operand is Instruction[] targets)
            {
                for (var i = 0; i < targets.Length; i++)
                    if (ReferenceEquals(targets[i], oldInstruction)) targets[i] = newInstruction;
            }
        }

        foreach (var eh in body.ExceptionHandlers)
        {
            if (ReferenceEquals(eh.TryStart, oldInstruction)) eh.TryStart = newInstruction;
            if (ReferenceEquals(eh.TryEnd, oldInstruction)) eh.TryEnd = newInstruction;
            if (ReferenceEquals(eh.HandlerStart, oldInstruction)) eh.HandlerStart = newInstruction;
            if (ReferenceEquals(eh.HandlerEnd, oldInstruction)) eh.HandlerEnd = newInstruction;
            if (ReferenceEquals(eh.FilterStart, oldInstruction)) eh.FilterStart = newInstruction;
        }
    }

    private static bool CanRemoveRange(CilBody body, int start, int count)
    {
        var remove = body.Instructions.Skip(start).Take(count).ToHashSet();
        if (remove.Count != count) return false;

        foreach (var ins in body.Instructions)
        {
            if (remove.Contains(ins)) continue;
            if (ins.Operand is Instruction target && remove.Contains(target)) return false;
            if (ins.Operand is Instruction[] targets && targets.Any(remove.Contains)) return false;
        }

        foreach (var eh in body.ExceptionHandlers)
        {
            if (remove.Contains(eh.TryStart) || remove.Contains(eh.TryEnd) ||
                remove.Contains(eh.HandlerStart) || remove.Contains(eh.HandlerEnd) ||
                (eh.FilterStart is not null && remove.Contains(eh.FilterStart)))
                return false;
        }

        return true;
    }

    private static void SetMaxStack(CilBody body)
    {
        if (MaxStackCalculator.GetMaxStack(body.Instructions, body.ExceptionHandlers, out var max))
            body.MaxStack = (ushort)Math.Max((int)max, 8);
        else
            body.MaxStack = 8;
    }

    private static void ValidateIl(ModuleDef module)
    {
        foreach (var type in module.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var body = method.Body;
            var ins = body.Instructions;
            if (ins.Count == 0)
                throw new InvalidOperationException($"{type.FullName}::{method.Name} has an empty body");

            var set = ins.ToHashSet();
            foreach (var i in ins)
            {
                switch (i.Operand)
                {
                    case Instruction target when !set.Contains(target):
                        Console.Error.WriteLine($"{type.FullName}::{method.Name} dangling branch at {i} -> {target}");
                        foreach (var x in ins) Console.Error.WriteLine("  " + x);
                        throw new InvalidOperationException($"{type.FullName}::{method.Name} has a dangling branch target");
                    case Instruction[] targets when targets.Any(t => !set.Contains(t)):
                        Console.Error.WriteLine($"{type.FullName}::{method.Name} dangling switch at {i}");
                        foreach (var x in ins) Console.Error.WriteLine("  " + x);
                        throw new InvalidOperationException($"{type.FullName}::{method.Name} has a dangling switch target");
                }
            }

            foreach (var eh in body.ExceptionHandlers)
            {
                foreach (var boundary in new[] { eh.TryStart, eh.TryEnd, eh.HandlerStart, eh.HandlerEnd, eh.FilterStart })
                {
                    if (boundary is not null && !set.Contains(boundary))
                        throw new InvalidOperationException($"{type.FullName}::{method.Name} has a dangling exception-handler boundary");
                }
            }
        }
    }

    private static TypeDef FindType(ModuleDef module, string fullName)
        => module.Find(fullName, false) ?? throw new InvalidOperationException("type not found: " + fullName);

    private static MethodDef FindMethod(TypeDef type, string name)
        => type.FindMethod(name) ?? throw new InvalidOperationException($"method not found: {type.FullName}::{name}");

    private static FieldDef FindField(TypeDef type, string name)
        => type.FindField(name) ?? throw new InvalidOperationException($"field not found: {type.FullName}::{name}");

    private static void PrintFingerprints(ModuleDef module)
    {
        Console.WriteLine("method fingerprints (16 hex chars):");
        foreach (var (typeName, methodName, paramCount) in KeyMethods)
        {
            var type = module.Find(typeName, false);
            var method = type?.Methods.FirstOrDefault(m => m.Name == methodName && m.MethodSig.Params.Count == paramCount);
            Console.WriteLine(method is null
                ? $"{typeName}::{methodName}/{paramCount} = <missing>"
                : $"{typeName}::{methodName}/{paramCount} = {Fingerprint(method)}");
        }
    }

    private static readonly (string Type, string Method, int Params)[] KeyMethods =
    {
        ("PCL.PageDownloadLeft", ".ctor", 0),
        ("PCL.PageDownloadInstall", "LoaderInit", 0),
        ("PCL.ModModpack", "ModpackInstall", 3),
        ("PCL.PageSelectLeft", "McFolderListUI", 0),
        ("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1),
        ("PCL.PageInstanceModDisabled", "BtnDownload_Loaded", 0),
        ("PCL.PageLaunchLeft", "RefreshButtonsUI", 0)
    };

    private static string Fingerprint(MethodDef method)
    {
        var body = method.Body ?? throw new InvalidOperationException($"{method.FullName} has no body");
        var sb = new StringBuilder();
        foreach (var ins in body.Instructions)
        {
            sb.Append(ins.OpCode.Code).Append(':');
            sb.Append(ins.Operand switch
            {
                null => "",
                string s => "s:" + s,
                IMethod m => "m:" + m.FullName,
                IField f => "f:" + f.FullName,
                Instruction target => "i:" + body.Instructions.IndexOf(target),
                Instruction[] targets => "t:" + string.Join(",", targets.Select(t => body.Instructions.IndexOf(t))),
                _ => "o:" + ins.Operand
            });
            sb.Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
