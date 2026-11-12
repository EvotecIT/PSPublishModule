using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>Reads a macOS volume's case semantics without creating probe files (APFS and HFS+ are usually case-insensitive).</summary>
internal static class MacDirectoryCaseSensitivity
{
    // _PC_CASE_SENSITIVE from <sys/unistd.h>.
    private const int CaseSensitiveName = 11;

    internal static bool TryGet(string directory, out bool caseSensitive)
    {
        caseSensitive = false;
#if NET472
        return false;
#else
        if (!OperatingSystem.IsMacOS())
            return false;

        var result = PathConf(directory, CaseSensitiveName);
        if (result < 0)
            return false;

        caseSensitive = result != 0;
        return true;
#endif
    }

    [DllImport("libc", EntryPoint = "pathconf", SetLastError = true)]
    private static extern long PathConf(string path, int name);
}
