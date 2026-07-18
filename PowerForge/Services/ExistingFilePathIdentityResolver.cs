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
        if (!GetFileInformationByHandleEx(
                handle,
                WindowsFileInfoByHandleClass.FileIdInfo,
                out var information,
                checked((uint)Marshal.SizeOf<WindowsFileIdInfo>())))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return FormatWindowsFileIdentity(
            information.VolumeSerialNumber,
            information.FileId.Part0,
            information.FileId.Part1);
    }

    /// <summary>
    /// Formats the volume-qualified 128-bit Windows file identifier without discarding ReFS identity bits.
    /// </summary>
    internal static string FormatWindowsFileIdentity(ulong volumeSerialNumber, ulong identifierPart0, ulong identifierPart1)
        => $"windows:{volumeSerialNumber:X16}:{identifierPart0:X16}{identifierPart1:X16}";

#if NET8_0_OR_GREATER
    private static string ReadUnixFileIdentity(SafeFileHandle handle)
    {
        if (SystemNativeFStat(handle, out var status) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return $"unix:{unchecked((ulong)status.Device):X16}:{unchecked((ulong)status.Inode):X16}";
    }
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileId128
    {
        internal ulong Part0;
        internal ulong Part1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal WindowsFileId128 FileId;
    }

    private enum WindowsFileInfoByHandleClass
    {
        FileIdInfo = 18
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        WindowsFileInfoByHandleClass fileInformationClass,
        out WindowsFileIdInfo information,
        uint bufferSize);

#if NET8_0_OR_GREATER
    [DllImport("System.Native", EntryPoint = "SystemNative_FStat", SetLastError = true)]
    private static extern int SystemNativeFStat(
        SafeFileHandle file,
        out UnixFileStatus status);
#endif
}
