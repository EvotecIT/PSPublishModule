namespace PowerForge;

/// <summary>Explicit operating-system native ABI imports trusted for reviewed .NET runtime-pack callers.</summary>
internal static class PowerShellTargetNativeAbiCatalog
{
    private static readonly HashSet<string> RuntimeInternalLibraries = new(StringComparer.Ordinal)
    {
        "System.Globalization.Native", "System.IO.Ports.Native", "System.Native", "System.Net.Security.Native",
        "System.Security.Cryptography.Native", "System.Security.Cryptography.Native.Apple",
        "System.Security.Cryptography.Native.OpenSsl"
    };

    private static readonly HashSet<string> WindowsLibraries = new(StringComparer.OrdinalIgnoreCase)
    {
        "__Internal", "QCall", "advapi32.dll", "bcrypt.dll", "crypt32.dll", "dbghelp.dll",
        "iphlpapi.dll", "kernel32.dll", "mi.dll", "mimofcodec.dll", "msvcrt.dll", "ncrypt.dll", "netapi32.dll", "normaliz.dll", "ntdll.dll", "ole32.dll",
        "oleaut32.dll", "powrprof.dll", "psapi.dll", "secur32.dll", "shell32.dll", "shlwapi.dll",
        "ucrtbase.dll", "user32.dll", "userenv.dll", "version.dll", "winhttp.dll", "winmm.dll", "ws2_32.dll"
    };

    private static readonly HashSet<string> LinuxLibraries = new(StringComparer.Ordinal)
    {
        "__Internal", "ld-linux-x86-64.so.2", "libc", "libc.so", "libc.so.6", "libdl", "libdl.so.2", "libgcc_s.so.1",
        "libm", "libm.so.6", "libpthread", "libpthread.so.0", "librt", "librt.so.1", "libstdc++.so.6"
    };

    private static readonly HashSet<string> MacLibraries = new(StringComparer.Ordinal)
    {
        "__Internal", "/usr/lib/libSystem.B.dylib", "libSystem.B.dylib", "libc", "libobjc.A.dylib"
    };

    internal static bool Contains(string runtimeIdentifier, string import)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier) || string.IsNullOrWhiteSpace(import)) return false;
        if (RuntimeInternalLibraries.Contains(import)) return true;
        if (runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
            return WindowsLibraries.Contains(import) ||
                   import.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
                   import.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase);
        if (runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase))
            return LinuxLibraries.Contains(import);
        if (runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase))
            return MacLibraries.Contains(import) || import.StartsWith("/System/Library/Frameworks/", StringComparison.Ordinal);
        return false;
    }
}
