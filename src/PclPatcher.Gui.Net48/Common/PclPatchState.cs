#nullable enable

using System.IO;
using System.Security.Cryptography;

namespace PclPatcher.Gui.Net48;

/// <summary>
/// Tells "this exe is still patched" apart from "a .bak happens to sit next to
/// it". Restore deliberately keeps the backup (see FileOperations.RestoreSource),
/// so the presence of <c>&lt;exe&gt;.bak</c> alone is stale information: the source
/// only counts as patched while its bytes differ from that backup.
/// </summary>
internal static class PclPatchState
{
    /// <summary>Shown next to a candidate whose source still differs from its backup.</summary>
    public const string PatchedLabel = "已打过补丁";

    /// <summary>Shown when a backup exists but the source already matches it (restored).</summary>
    public const string RestoredLabel = "有原版备份";

    /// <summary>False when either side is missing; a lone .bak proves nothing.</summary>
    public static bool HashesDiffer(string? sourceSha256, string? backupSha256)
        => !string.IsNullOrEmpty(sourceSha256) &&
           !string.IsNullOrEmpty(backupSha256) &&
           !string.Equals(sourceSha256, backupSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hashes <paramref name="sourcePath"/> and its <c>.bak</c> and reports whether
    /// they still differ. Any read failure is treated as "not patched": a wrong
    /// "已打过补丁" is what this helper exists to avoid.
    /// </summary>
    public static bool IsPatched(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return false;

        var backupPath = sourcePath + ".bak";
        try
        {
            if (!File.Exists(sourcePath) || !File.Exists(backupPath)) return false;
            return HashesDiffer(Sha256(sourcePath), Sha256(backupPath));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string Sha256(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
