namespace PowerForge;

/// <summary>
/// Performs bounded cleanup and disk-space checks for configured Apple release artifacts.
/// </summary>
internal sealed class AppleReleaseArtifactService
{
    private static readonly StringComparison PathComparison =
        Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer =
        Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly Func<string, long> _getAvailableBytes;
    private readonly Func<DateTimeOffset> _utcNow;

    internal AppleReleaseArtifactService(
        Func<string, long>? getAvailableBytes = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _getAvailableBytes = getAvailableBytes ?? GetAvailableBytes;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal PowerForgeAppleReleaseCleanupReceipt Preflight(PowerForgeAppleReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        var cleanup = plan.Automation.CleanupBeforeArchive
            ? RemoveStaleArtifacts(plan)
            : new PowerForgeAppleReleaseCleanupReceipt();

        var availableBytes = _getAvailableBytes(plan.ProjectRoot);
        cleanup.FreeSpaceGB = BytesToGigabytes(availableBytes);
        var minimumBytes = GigabytesToBytes(plan.Automation.MinimumFreeSpaceGB);
        if (minimumBytes > 0 && availableBytes < minimumBytes)
        {
            throw new InvalidOperationException(
                $"Apple archive preflight requires {plan.Automation.MinimumFreeSpaceGB:0.##} GB free, " +
                $"but only {cleanup.FreeSpaceGB:0.##} GB is available under '{plan.ProjectRoot}'.");
        }

        return cleanup;
    }

    internal PowerForgeAppleReleaseCleanupReceipt RemoveStaleArtifacts(PowerForgeAppleReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        var roots = GetConfiguredRoots(plan);
        var cutoff = _utcNow().UtcDateTime.AddDays(-Math.Max(0, plan.Automation.ArtifactRetentionDays));
        var candidates = roots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFileSystemEntries(root))
            .Where(path => GetLastWriteTimeUtc(path) <= cutoff)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return RemovePaths(plan, candidates);
    }

    internal PowerForgeAppleReleaseCleanupReceipt RemoveCurrentArtifacts(
        PowerForgeAppleReleasePlan plan,
        IEnumerable<PowerForgeAppleAppReleaseTargetPlan>? apps = null)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        var selected = apps?.ToArray() ?? plan.Apps;
        var paths = selected
            .SelectMany(static app => new[] { app.ArchivePath, app.ExportPath })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(PathComparer)
            .ToArray();
        return RemovePaths(plan, paths);
    }

    private PowerForgeAppleReleaseCleanupReceipt RemovePaths(
        PowerForgeAppleReleasePlan plan,
        IEnumerable<string> paths)
    {
        var roots = GetConfiguredRoots(plan);
        var removed = new List<string>();
        long reclaimedBytes = 0;
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!roots.Any(root => IsWithinRoot(fullPath, root)))
                throw new InvalidOperationException($"Refusing to remove Apple release artifact outside configured roots: {fullPath}");
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                continue;
            EnsureNoReparsePoints(plan.ProjectRoot, fullPath);
            EnsureTreeContainsNoReparsePoints(fullPath);

            reclaimedBytes += GetSize(fullPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            else
                Directory.Delete(fullPath, recursive: true);
            removed.Add(FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, fullPath).Replace('\\', '/'));
        }

        return new PowerForgeAppleReleaseCleanupReceipt
        {
            RemovedPaths = removed.ToArray(),
            ReclaimedBytes = reclaimedBytes,
            FreeSpaceGB = BytesToGigabytes(_getAvailableBytes(plan.ProjectRoot))
        };
    }

    private static string[] GetConfiguredRoots(PowerForgeAppleReleasePlan plan)
    {
        var roots = plan.Apps
            .SelectMany(static app => new[]
            {
                Path.GetDirectoryName(app.ArchivePath),
                Path.GetDirectoryName(app.ExportPath)
            })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .ToArray();
        if (roots.Length == 0)
            throw new InvalidOperationException("Apple release artifact roots could not be resolved.");
        if (roots.Any(root => !IsWithinRoot(root, plan.ProjectRoot)))
            throw new InvalidOperationException("Apple release artifact roots must remain inside AppleApps.ProjectRoot.");
        foreach (var root in roots.Where(Directory.Exists))
            EnsureNoReparsePoints(plan.ProjectRoot, root);
        return roots;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, PathComparison) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static void EnsureNoReparsePoints(string projectRoot, string path)
    {
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsWithinRoot(current, root))
            throw new InvalidOperationException($"Refusing to inspect Apple release artifact outside project root: {current}");

        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to remove Apple release artifact through a symbolic link or reparse point: {current}");
            }

            if (current.Equals(root, PathComparison))
                break;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException($"Unable to inspect Apple release artifact path: {path}");
        }
    }

    private static void EnsureTreeContainsNoReparsePoints(string path)
    {
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to recursively remove an Apple release artifact tree containing a symbolic link or reparse point: {current}");
            }

            if ((attributes & FileAttributes.Directory) == 0)
                continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                pending.Push(entry);
        }
    }

    private static DateTime GetLastWriteTimeUtc(string path)
        => File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : Directory.GetLastWriteTimeUtc(path);

    private static long GetSize(string path)
    {
        if (File.Exists(path))
            return new FileInfo(path).Length;
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(static file => new FileInfo(file).Length);
    }

    private static long GetAvailableBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException($"Unable to resolve disk root for '{path}'.");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    private static long GigabytesToBytes(double value)
        => value <= 0 ? 0 : checked((long)(value * 1024d * 1024d * 1024d));

    private static double BytesToGigabytes(long value)
        => Math.Round(value / (1024d * 1024d * 1024d), 2);
}
