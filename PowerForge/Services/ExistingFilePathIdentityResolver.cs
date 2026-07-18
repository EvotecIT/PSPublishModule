#if NETFRAMEWORK
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace PowerForge;

/// <summary>
/// Resolves an existing file to a stable physical identity so aliases are projected and written once.
/// </summary>
internal static class ExistingFilePathIdentityResolver
{
    internal static string Resolve(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
#if NET8_0_OR_GREATER
        return ResolveModern(fullPath);
#else
        return ResolveWindowsFinalPath(fullPath);
#endif
    }

#if NET8_0_OR_GREATER
    private static string ResolveModern(string fullPath)
    {
        var root = System.IO.Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new IOException($"Existing file path has no filesystem root: {fullPath}");

        var current = root!;
        var relative = fullPath.Substring(root!.Length);
        var separators = System.IO.Path.DirectorySeparatorChar == System.IO.Path.AltDirectorySeparatorChar
            ? new[] { System.IO.Path.DirectorySeparatorChar }
            : new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };

        foreach (var segment in relative.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = FindExistingEntry(current, segment);
            FileSystemInfo info = Directory.Exists(entry)
                ? new DirectoryInfo(entry)
                : new FileInfo(entry);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            current = target is null ? entry : System.IO.Path.GetFullPath(target.FullName);
        }

        return System.IO.Path.GetFullPath(current);
    }

    private static string FindExistingEntry(string parent, string segment)
    {
        string? exact = null;
        var aliases = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
        {
            var name = System.IO.Path.GetFileName(entry);
            if (string.Equals(name, segment, StringComparison.Ordinal))
            {
                exact = entry;
                break;
            }
            if (string.Equals(name, segment, StringComparison.OrdinalIgnoreCase))
                aliases.Add(entry);
        }

        if (exact is not null)
            return exact;
        if (aliases.Count == 1)
            return aliases[0];
        if (aliases.Count > 1)
            throw new IOException($"Existing path has ambiguous case aliases: {System.IO.Path.Combine(parent, segment)}");
        throw new FileNotFoundException("Existing path entry disappeared while resolving its identity.", System.IO.Path.Combine(parent, segment));
    }
#else
    private static string ResolveWindowsFinalPath(string fullPath)
    {
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return ReadFinalPath(stream.SafeFileHandle);
    }

    private static string ReadFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var builder = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length < builder.Capacity)
                return NormalizeWindowsDevicePath(builder.ToString());
            capacity = checked((int)length + 1);
        }
    }

    private static string NormalizeWindowsDevicePath(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            return @"\\" + path.Substring(uncPrefix.Length);
        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(devicePrefix.Length)
            : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);
#endif
}
