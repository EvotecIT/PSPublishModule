using System.IO.Compression;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    private const int PortableUnixPermissionMask = 0x1FF;
    private const int PrivilegedUnixPermissionMask = 0xE00;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixRegularFile = 0x8000;
    private const int UnixSymbolicLink = 0xA000;

    /// <summary>Authenticates portable read/write/execute bits without carrying privileged Unix modes.</summary>
    private static int? GetPortableUnixPermissions(string path)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            return (int)File.GetUnixFileMode(path) & PortableUnixPermissionMask;
#endif
        return null;
    }

    private static void WriteArchiveUnixPermissions(ZipArchiveEntry entry, int? permissions)
    {
        if (permissions is not null)
            entry.ExternalAttributes = (entry.ExternalAttributes & 0xFFFF) |
                                       ((UnixRegularFile | permissions.Value) << 16);
    }

    /// <summary>Checks archive metadata against the current tested payload before any permissions are restored.</summary>
    private static void ValidateArchiveUnixPermissions(ZipArchiveEntry entry, int? expected)
    {
        var unixMode = entry.ExternalAttributes >> 16 & 0xFFFF;
        var fileType = unixMode & UnixFileTypeMask;
        if (fileType == UnixSymbolicLink)
            throw new InvalidDataException($"Qualified package entry '{entry.FullName}' is a symbolic link.");
        if (fileType != 0 && fileType != UnixRegularFile)
            throw new InvalidDataException($"Qualified package entry '{entry.FullName}' is not a regular file.");
        if ((unixMode & PrivilegedUnixPermissionMask) != 0)
            throw new InvalidDataException($"Qualified package entry '{entry.FullName}' has privileged Unix permission bits.");
        if (expected is not null && (unixMode & PortableUnixPermissionMask) != expected)
            throw new InvalidDataException($"Qualified package entry '{entry.FullName}' has Unix permission bits that differ from its tested artifact.");
    }

    private static void RestoreExtractedUnixPermissions(string path, int? permissions)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows() && permissions is not null)
            File.SetUnixFileMode(path, (UnixFileMode)permissions.Value);
#endif
    }

    private static void ValidateInstalledUnixPermissions(string path, string entryName, int? expected)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows() && expected is not null)
        {
            var actual = (int)File.GetUnixFileMode(path);
            if ((actual & PrivilegedUnixPermissionMask) != 0 || (actual & PortableUnixPermissionMask) != expected)
                throw new InvalidDataException($"Installed package entry '{entryName}' has Unix permission bits that differ from its archive.");
        }
#endif
    }
}
