namespace PowerForge;

public sealed partial class AppleDeviceDeploymentService
{
    private async Task<MirrorResult> MirrorBuildRootAsync(
        string projectPath,
        AppleAppBuildRequest request,
        StringComparison sourcePathComparison,
        CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveBuildRoot(projectPath, request.BuildRoot);
        var mirrorPath = ResolveBuildMirrorPath(request);
        var normalizedSourceRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sourceRoot));
        var normalizedMirrorPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(mirrorPath));
        if (normalizedMirrorPath.StartsWith(
                normalizedSourceRoot,
                sourcePathComparison))
            throw new InvalidOperationException("BuildMirrorPath must not be inside the mirrored build root.");

        var physicalSourceRoot = AppleReleaseArtifactService.ResolvePhysicalPath(sourceRoot);
        var physicalMirrorPath = AppleReleaseArtifactService.ResolvePhysicalPath(mirrorPath);
        if (IsSameOrDescendant(
                physicalMirrorPath,
                physicalSourceRoot,
                sourcePathComparison) ||
            IsSameOrDescendant(
                physicalSourceRoot,
                physicalMirrorPath,
                sourcePathComparison))
        {
            throw new InvalidOperationException(
                "BuildMirrorPath must not physically overlap the mirrored build root through a symbolic-link or path alias.");
        }

        Directory.CreateDirectory(mirrorPath);
        var mutationMonitor = new AppleReleaseSourceMutationMonitor(
            mirrorPath,
            "local Apple build mirror",
            "xcodebuild",
            "Discard the product and rebuild the mirror.",
            enableImmediately: false);

        try
        {
            var args = new List<string>
            {
                "-a",
                "--delete",
                "--delete-excluded",
                "--exclude",
                "/.git",
                "--exclude",
                "/.build",
                "--exclude",
                "/.swiftpm",
                "--exclude",
                "/build",
                "--exclude",
                "/DerivedData",
                normalizedSourceRoot,
                normalizedMirrorPath
            };

            var result = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    NormalizeExecutable(request.RsyncExecutable, "rsync"),
                    sourceRoot,
                    args,
                    request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout),
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                mutationMonitor.Dispose();
                return new MirrorResult(sourceRoot, mirrorPath, result, null);
            }

            _ = mutationMonitor.CaptureExpectedProducerOutput(
                () => AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
                    mirrorPath,
                    "local Apple build mirror"),
                "rsync");
            return new MirrorResult(sourceRoot, mirrorPath, result, mutationMonitor);
        }
        catch
        {
            mutationMonitor.Dispose();
            throw;
        }
    }

    private static string ResolveBuildMirrorPath(AppleAppBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.BuildMirrorPath))
            return Path.GetFullPath(request.BuildMirrorPath!);

        var safeScheme = SanitizePathPart(request.Scheme);
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        return Path.Combine(Path.GetTempPath(), "powerforge-apple-build-mirror", $"{safeScheme}-{uniqueSuffix}");
    }

    private static bool IsSameOrDescendant(
        string candidate,
        string root,
        StringComparison comparison)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullCandidate.Equals(fullRoot, comparison) ||
            fullCandidate.StartsWith(
                EnsureTrailingDirectorySeparator(fullRoot),
                comparison);
    }

    private static string RewritePath(
        string path,
        string sourceRoot,
        string mirrorPath,
        StringComparison sourcePathComparison)
    {
        var fullPath = Path.GetFullPath(path);
        var fullSourceRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sourceRoot));
        if (!fullPath.StartsWith(fullSourceRoot, sourcePathComparison))
            return fullPath;

        var relative = fullPath.Substring(fullSourceRoot.Length);
        return Path.Combine(mirrorPath, relative);
    }

    private sealed class MirrorResult
    {
        public MirrorResult(
            string sourceRoot,
            string mirrorPath,
            ProcessRunResult processResult,
            AppleReleaseSourceMutationMonitor? mutationMonitor)
        {
            SourceRoot = sourceRoot;
            MirrorPath = mirrorPath;
            ProcessResult = processResult;
            MutationMonitor = mutationMonitor;
        }

        public string SourceRoot { get; }

        public string MirrorPath { get; }

        public ProcessRunResult ProcessResult { get; }

        public AppleReleaseSourceMutationMonitor? MutationMonitor { get; }
    }
}
