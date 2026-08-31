namespace PowerForge;

/// <summary>
/// Separates durable Apple deployment results from the fresh DerivedData tree
/// consumed by one provenance-bound xcodebuild invocation.
/// </summary>
internal sealed class AppleDerivedDataBuildRoot : IDisposable
{
    private bool _ownsBuildDirectory = true;

    private AppleDerivedDataBuildRoot(
        AppleStableDirectoryIdentity resultDirectory,
        AppleStableDirectoryIdentity buildDirectory)
    {
        ResultDirectory = resultDirectory;
        BuildDirectory = buildDirectory;
    }

    internal AppleStableDirectoryIdentity ResultDirectory { get; }

    internal AppleStableDirectoryIdentity BuildDirectory { get; }

    internal string ResultPath => ResultDirectory.Path;

    internal string BuildPath => BuildDirectory.Path;

    internal static AppleDerivedDataBuildRoot Create(
        string requestedResultPath,
        string sourceRoot,
        StringComparison sourcePathComparison)
    {
        var resultPath = Path.GetFullPath(requestedResultPath);
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            resultPath,
            sourceRoot,
            nameof(AppleAppBuildRequest.DerivedDataPath),
            sourcePathComparison);
        Directory.CreateDirectory(resultPath);
        var resultDirectory = AppleStableDirectoryIdentity.Capture(
            resultPath,
            "DerivedData result root");
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            resultDirectory.Path,
            sourceRoot,
            nameof(AppleAppBuildRequest.DerivedDataPath),
            sourcePathComparison);

        var buildPath = Path.Combine(
            resultDirectory.Path,
            "PowerForge",
            "ExactSourceBuilds",
            Guid.NewGuid().ToString("N"));
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            buildPath,
            sourceRoot,
            "fresh DerivedData build root",
            sourcePathComparison);
        EnsureFreshOrdinaryPath(buildPath);
        Directory.CreateDirectory(buildPath);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                buildPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
#endif
        var buildDirectory = AppleStableDirectoryIdentity.Capture(
            buildPath,
            "fresh DerivedData build root");
        if (!buildDirectory.Path.Equals(
                Path.GetFullPath(buildPath),
                FrameworkCompatibility.GetPathStringComparisonForPath(buildPath)))
        {
            throw new InvalidOperationException(
                $"The fresh DerivedData build root must not traverse a symbolic link or reparse point: {buildPath}");
        }
        resultDirectory.ValidateUnchanged();
        EnsureEmpty(buildDirectory);
        return new AppleDerivedDataBuildRoot(
            resultDirectory,
            buildDirectory);
    }

    internal void ValidateFreshBeforeBuild()
    {
        ResultDirectory.ValidateUnchanged();
        EnsureEmpty(BuildDirectory);
    }

    internal AppleStableDirectoryIdentity ReleaseBuildDirectory()
    {
        _ownsBuildDirectory = false;
        return BuildDirectory;
    }

    internal void RetainBuildDirectory()
        => _ownsBuildDirectory = false;

    private static void EnsureFreshOrdinaryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath) || File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"The fresh DerivedData build root already exists: {fullPath}");
        }
        var physicalPath = AppleReleaseArtifactService.ResolvePhysicalPath(fullPath);
        if (!fullPath.Equals(
                physicalPath,
                FrameworkCompatibility.GetPathStringComparisonForPath(fullPath)))
        {
            throw new InvalidOperationException(
                $"The fresh DerivedData build root must not traverse a symbolic link or reparse point: {fullPath}");
        }
    }

    private static void EnsureEmpty(AppleStableDirectoryIdentity directory)
    {
        directory.ValidateUnchanged();
        if (Directory.EnumerateFileSystemEntries(directory.Path).Any())
        {
            throw new InvalidOperationException(
                $"The fresh DerivedData build root was populated before xcodebuild started: {directory.Path}");
        }
    }

    public void Dispose()
    {
        if (!_ownsBuildDirectory)
            return;
        _ownsBuildDirectory = false;
        try { BuildDirectory.DeleteOwnedDirectoryIfUnchanged(); } catch { /* best effort private cleanup */ }
    }
}
