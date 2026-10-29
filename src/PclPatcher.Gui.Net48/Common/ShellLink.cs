using System.Reflection;

namespace PclPatcher.Gui.Net48;

/// <summary>
/// Resolves the target of a Windows shortcut. PCL is usually started from a
/// desktop or Start Menu .lnk, and that shortcut is often the only on-disk
/// pointer to where the exe actually lives.
/// </summary>
internal static class ShellLink
{
    public static string ResolveTarget(string shortcutPath)
    {
        object shell = null;
        object shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;

            shell = Activator.CreateInstance(type);
            shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { shortcutPath });
            if (shortcut is null) return null;

            var target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null,
                shortcut, null) as string;
            if (string.IsNullOrWhiteSpace(target)) return null;

            target = target.Trim().Trim('"');
            return File.Exists(target) ? IOPath.GetFullPath(target) : null;
        }
        catch
        {
            // Broken shortcut, blocked WScript host, or a shell link that has no
            // file target (MSI-advertised shortcuts). Not finding one is fine.
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                try { Marshal.ReleaseComObject(shortcut); } catch { }
            }
            if (shell is not null && Marshal.IsComObject(shell))
            {
                try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }
    }
}
