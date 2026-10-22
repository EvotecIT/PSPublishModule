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
}
