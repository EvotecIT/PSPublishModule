namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    // Both generated loaders are standalone. Keep their physical-path identity implementation
    // together and use APIs available to the development loader on PowerShell 7 / .NET Core 3.1.
    private static string BuildModulePathIdentitySource() => """
    private static readonly StringComparer ModulePathComparer =
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string ResolveModulePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Module assembly was not found.", fullPath);

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            using (var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buffer = new System.Text.StringBuilder(512);
                while (true)
                {
                    // FILE_NAME_OPENED keeps the resolved handle path without SMB component-normalization queries.
                    // Normalizing a readable file can otherwise fail when a parent denies directory access.
                    var length = GetFinalModulePathName(file.SafeFileHandle.DangerousGetHandle(), buffer, (uint)buffer.Capacity, 0x8);
                    if (length == 0)
                        throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
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
        }

        var physical = GetRealModulePath(fullPath, IntPtr.Zero);
        if (physical == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        try { return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(physical) ?? fullPath; }
        finally { FreeRealModulePath(physical); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalModulePathName(IntPtr file, System.Text.StringBuilder path, uint length, uint flags);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr GetRealModulePath(string path, IntPtr resolved);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "free")]
    private static extern void FreeRealModulePath(IntPtr path);
""";
}
