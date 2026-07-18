using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Resolves an existing file to a stable physical identity so aliases are projected and written once.
/// </summary>
internal static class ExistingFilePathIdentityResolver
{
    /// <summary>
    /// Returns an operating-system file identity that is shared by every symbolic-link or hard-link alias.
    /// </summary>
    /// <param name="path">Existing file whose identity should be resolved.</param>
    /// <returns>A volume/device-qualified file identifier suitable for in-process equality checks.</returns>
    internal static string Resolve(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (System.IO.Path.DirectorySeparatorChar == '\\')
            return ReadWindowsFileIdentity(stream.SafeFileHandle);

#if NET8_0_OR_GREATER
        return ReadUnixFileIdentity(stream.SafeFileHandle);
#else
        throw new PlatformNotSupportedException("Physical file identity is not available for this runtime and operating system.");
#endif
    }

    private static string ReadWindowsFileIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return $"windows:{information.VolumeSerialNumber:X8}:{information.FileIndexHigh:X8}{information.FileIndexLow:X8}";
    }

#if NET8_0_OR_GREATER
    private static string ReadUnixFileIdentity(SafeFileHandle handle)
    {
        if (SystemNativeFStat(handle, out var status) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return $"unix:{unchecked((ulong)status.Device):X16}:{unchecked((ulong)status.Inode):X16}";
    }
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        internal uint FileAttributes;
        internal WindowsFileTime CreationTime;
        internal WindowsFileTime LastAccessTime;
        internal WindowsFileTime LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

#if NET8_0_OR_GREATER
    // System.Native exposes one normalized FileStatus ABI across .NET's supported Unix platforms.
    [StructLayout(LayoutKind.Sequential)]
    private struct UnixFileStatus
    {
        internal int Flags;
        internal int Mode;
        internal uint UserId;
        internal uint GroupId;
        internal long Size;
        internal long AccessTime;
        internal long AccessTimeNanoseconds;
        internal long ModificationTime;
        internal long ModificationTimeNanoseconds;
        internal long ChangeTime;
        internal long ChangeTimeNanoseconds;
        internal long BirthTime;
        internal long BirthTimeNanoseconds;
        internal long Device;
        internal long RawDevice;
        internal long Inode;
        internal uint UserFlags;
        internal int HardLinkCount;
    }
#endif

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out WindowsFileInformation information);

#if NET8_0_OR_GREATER
    [DllImport("System.Native", EntryPoint = "SystemNative_FStat", SetLastError = true)]
    private static extern int SystemNativeFStat(
        SafeFileHandle file,
        out UnixFileStatus status);
#endif
}
