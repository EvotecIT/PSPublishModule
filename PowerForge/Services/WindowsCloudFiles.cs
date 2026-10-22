using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PowerForge;

/// <summary>Identifies cloud-managed inputs without following filesystem redirections.</summary>
internal static class WindowsCloudFiles
{
    /// <summary>
    /// Recognizes only the documented CLOUD and CLOUD_1 through CLOUD_F reparse tags.
    /// Content availability is still enforced by the subsequent file read or copy.
    /// </summary>
    internal static bool IsPlaceholder(string path)
    {
        if (!FrameworkCompatibility.IsWindows())
            return false;

        const uint openReparsePoint = 0x00200000;
        const uint backupSemantics = 0x02000000;
        using var handle = CreateFile(
            path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero,
            FileMode.Open, openReparsePoint | backupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
            throw MetadataError(path);

        const int fileAttributeTagInfo = 9;
        if (!GetFileInformationByHandleEx(handle, fileAttributeTagInfo, out var information,
                (uint)Marshal.SizeOf(typeof(FileAttributeTagInfo))))
            throw MetadataError(path);

        return (information.FileAttributes & FileAttributes.ReparsePoint) != 0 &&
               IsCloudTag(information.ReparseTag);
    }

    // Only bits 12 through 15 vary in the documented CLOUD tag family.
    internal static bool IsCloudTag(uint tag) => (tag & 0xFFFF0FFFu) == 0x9000001Au;

    private static IOException MetadataError(string path) => new(
        $"Cannot inspect the reparse point for artefact input '{path}'.",
        new Win32Exception(Marshal.GetLastWin32Error()));

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal FileAttributes FileAttributes;
        internal uint ReparseTag;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share,
        IntPtr securityAttributes, FileMode disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileAttributeTagInfo information, uint bufferSize);
}
