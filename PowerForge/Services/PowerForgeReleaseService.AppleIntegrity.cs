namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static void ValidateAppleAutomationOutputPaths(
        string receiptPath,
        string receiptHistoryPath,
        string planReceiptPath,
        string lockPath,
        IEnumerable<(string Name, string Path, bool IsDirectory)> protectedPaths)
    {
        var history = Path.GetFullPath(receiptHistoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = new[]
        {
            (Name: "ReceiptPath", Path: Path.GetFullPath(receiptPath)),
            (Name: "PlanReceiptPath", Path: Path.GetFullPath(planReceiptPath)),
            (Name: "LockPath", Path: Path.GetFullPath(lockPath))
        };

        for (var index = 0; index < files.Length; index++)
        {
            for (var siblingIndex = index + 1; siblingIndex < files.Length; siblingIndex++)
            {
                if (!PathsOverlap(files[index].Path, files[siblingIndex].Path))
                    continue;
                throw new InvalidOperationException(
                    $"Apple automation output files must not equal, contain, or be contained by each other: " +
                    $"{files[index].Name}, {files[siblingIndex].Name}.");
            }
        }

        foreach (var file in files)
        {
            var comparison = GetAppleOutputPathComparison(file.Path, history);
            var candidate = file.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (candidate.Equals(history, comparison) ||
                candidate.StartsWith(history + Path.DirectorySeparatorChar, comparison) ||
                history.StartsWith(candidate + Path.DirectorySeparatorChar, comparison))
            {
                throw new InvalidOperationException(
                    $"AppleApps.Automation.ReceiptHistoryPath must not equal, contain, or be contained by {file.Name}.");
            }
        }

        var outputs = files
            .Select(static file => (file.Name, file.Path, IsDirectory: false))
            .Append((Name: "ReceiptHistoryPath", Path: history, IsDirectory: true))
            .ToArray();
        foreach (var output in outputs)
        {
            foreach (var protectedPath in protectedPaths.Where(static entry => !string.IsNullOrWhiteSpace(entry.Path)))
            {
                if (!PathsOverlap(output.Path, protectedPath.Path))
                    continue;
                throw new InvalidOperationException(
                    $"Apple automation output {output.Name} must not equal, contain, or be contained by " +
                    $"release input/artifact path {protectedPath.Name}: {Path.GetFullPath(protectedPath.Path)}");
            }
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        var comparison = GetAppleOutputPathComparison(first, second);
        var left = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var right = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return left.Equals(right, comparison) ||
               left.StartsWith(right + Path.DirectorySeparatorChar, comparison) ||
               right.StartsWith(left + Path.DirectorySeparatorChar, comparison);
    }

    private static StringComparison GetAppleOutputPathComparison(string first, string second)
        => FrameworkCompatibility.GetPathStringComparisonForPath(first) == StringComparison.OrdinalIgnoreCase ||
           FrameworkCompatibility.GetPathStringComparisonForPath(second) == StringComparison.OrdinalIgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static void VerifyExpectedAppleCheckpointArchives(PowerForgeAppleReleasePlan plan)
    {
        foreach (var app in plan.Apps.Where(static candidate => !string.IsNullOrWhiteSpace(candidate.ExpectedArchiveSha256)))
        {
            if (!File.Exists(app.ArchivePath) && !Directory.Exists(app.ArchivePath))
            {
                throw new FileNotFoundException(
                    $"The checkpointed Apple archive for '{app.Name}' was not found: {app.ArchivePath}",
                    app.ArchivePath);
            }

            var actual = AppleNotarizationService.ComputeArtifactSha256(app.ArchivePath);
            if (!actual.Equals(app.ExpectedArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The checkpointed Apple archive for '{app.Name}' changed before publish. Expected SHA-256 " +
                    $"'{app.ExpectedArchiveSha256}', received '{actual}'. Rebuild and approve a new exact checkpoint.");
            }
        }
    }

    private static string ValidateDirectRecoveryArtifactPath(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        string storedPath)
    {
        var artifactPath = Path.GetFullPath(Path.IsPathRooted(storedPath)
            ? storedPath
            : Path.Combine(plan.ProjectRoot, storedPath));
        var exportRoot = Path.GetFullPath(app.ExportPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!artifactPath.Equals(exportRoot, comparison) &&
            !artifactPath.StartsWith(exportRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException(
                $"Direct Apple recovery artifact for '{app.Name}' is outside its current export root: {artifactPath}");
        }

        EnsurePathHasNoLinkedTraversal(plan.ProjectRoot, artifactPath, $"Direct Apple recovery artifact for '{app.Name}'");
        return artifactPath;
    }

    private static void EnsurePathHasNoLinkedTraversal(string projectRoot, string path, string name)
    {
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!current.Equals(root, comparison) &&
            !current.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException($"{name} is outside AppleApps.ProjectRoot: {current}");

        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{name} traverses a symbolic link or reparse point: {current}");
            if (current.Equals(root, comparison))
                break;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException($"Unable to validate {name}: {path}");
        }
    }
}
