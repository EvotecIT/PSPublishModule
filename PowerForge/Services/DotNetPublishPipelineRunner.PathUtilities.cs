namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static bool IsWindows()
    {
#if NET472
        return true;
#else
        return OperatingSystem.IsWindows();
#endif
    }

    private static string? ResolveOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    internal static void DirectoryCopy(string sourceDir, string destDir)
    {
        var source = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Bundle source directory cannot be a reparse point: {source}");
        if (Directory.Exists(dest) && (File.GetAttributes(dest) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Bundle destination directory cannot be a reparse point: {dest}");

        Directory.CreateDirectory(dest);

        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((source, dest));
        while (pending.Count > 0)
        {
            (string currentSource, string currentDestination) = pending.Pop();
            if ((File.GetAttributes(currentSource) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Bundle source contains a reparse point: {currentSource}");
            if ((File.GetAttributes(currentDestination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Bundle destination contains a reparse point: {currentDestination}");

            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         currentSource,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Bundle source contains a reparse point: {entry}");

                string target = Path.Combine(currentDestination, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (Directory.Exists(target) &&
                        (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Bundle destination contains a reparse point: {target}");
                    }
                    Directory.CreateDirectory(target);
                    pending.Push((entry, target));
                    continue;
                }

                if (PathEntryExists(target) &&
                    (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Bundle destination contains a reparse point: {target}");
                }
                File.Copy(entry, target, overwrite: true);
            }
        }
    }
}
