#if NETCOREAPP
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge.ModuleLoader.V1;

/// <summary>Physical-path identity for module files, so one module group maps to one load context.</summary>
public static class ModulePath
{
    /// <summary>True when both paths name the same physical file, including through links or junctions.</summary>
    public static bool IsSameFile(string first, string second)
    {
        try { return Comparer.Equals(Resolve(first), Resolve(second)); }
        catch { return Comparer.Equals(Path.GetFullPath(first), Path.GetFullPath(second)); }
    }

    internal static readonly StringComparer Comparer =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string Resolve(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Module assembly was not found.", fullPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new StringBuilder(512);
            while (true)
            {
                // FILE_NAME_OPENED keeps the resolved handle path without SMB component-normalization queries.
                // Normalizing a readable file can otherwise fail when a parent denies directory access.
                var length = GetFinalPathNameByHandle(file.SafeFileHandle.DangerousGetHandle(), buffer, (uint)buffer.Capacity, 0x8);
                if (length == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (length >= buffer.Capacity)
                {
                    buffer.Capacity = checked((int)length + 1);
                    continue;
                }

                var resolved = buffer.ToString();
                if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    return @"\\" + resolved.Substring(8);
                return resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved.Substring(4) : resolved;
            }
        }

        var physical = RealPath(fullPath, IntPtr.Zero);
        if (physical == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try { return Marshal.PtrToStringAnsi(physical) ?? fullPath; }
        finally { Free(physical); }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint length, uint flags);

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPath(string path, IntPtr resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(IntPtr path);
}
#endif
