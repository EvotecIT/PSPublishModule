using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

/// <summary>Preserves long local and UNC paths when calling native Windows file APIs.</summary>
internal static class WindowsNativePath
{
    internal static string GetExtendedPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath.Substring(2)
            : @"\\?\" + fullPath;
    }

    /// <summary>Expands Windows short names in an existing path.</summary>
    internal static string GetLongPath(string path)
    {
        var extendedPath = GetExtendedPath(path);
        var capacity = GetLongPathName(extendedPath, null, 0);
        if (capacity != 0)
        {
            var buffer = new StringBuilder(checked((int)capacity));
            var length = GetLongPathName(extendedPath, buffer, capacity);
            if (length > 0 && length < capacity)
                return buffer.ToString();
        }

        throw new IOException($"Could not resolve the long Windows path for '{path}'.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetLongPathNameW", ExactSpelling = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder? longPath, uint capacity);
}
