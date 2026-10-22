using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PowerForge;

/// <summary>Reads per-directory case semantics without creating probe files.</summary>
internal static class WindowsDirectoryCaseSensitivity
{
    internal static bool TryGet(string directory, out bool caseSensitive)
    {
        caseSensitive = false;
        if (!FrameworkCompatibility.IsWindows())
            return false;

        const uint backupSemantics = 0x02000000;
        using var handle = CreateFile(WindowsNativePath.GetExtendedPath(directory), 0, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, FileMode.Open, backupSemantics, IntPtr.Zero);
        const int fileCaseSensitiveInfo = 23;
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, fileCaseSensitiveInfo,
                out uint flags, sizeof(uint)))
            return false;

        caseSensitive = (flags & 1) != 0;
        return true;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share,
        IntPtr securityAttributes, FileMode disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out uint flags, uint bufferSize);
}
