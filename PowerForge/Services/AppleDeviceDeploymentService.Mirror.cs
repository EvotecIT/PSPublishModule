namespace PowerForge;

public sealed partial class AppleDeviceDeploymentService
{
    private async Task<MirrorResult> MirrorBuildRootAsync(
        string projectPath,
        AppleAppBuildRequest request,
        string rsyncExecutable,
        StringComparison sourcePathComparison,
        IReadOnlyCollection<string> excludedGeneratedRootPaths,
        CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveBuildRoot(projectPath, request.BuildRoot);
        var requestedMirrorPath = ResolveBuildMirrorPath(request);
        var normalizedSourceRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sourceRoot));
        var normalizedMirrorPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(requestedMirrorPath));
        if (normalizedMirrorPath.StartsWith(
                normalizedSourceRoot,
                sourcePathComparison))
            throw new InvalidOperationException("BuildMirrorPath must not be inside the mirrored build root.");

        var physicalSourceRoot = AppleReleaseArtifactService.ResolvePhysicalPath(sourceRoot);
        var physicalMirrorPath = AppleReleaseArtifactService.ResolvePhysicalPath(requestedMirrorPath);
        ValidatePhysicalMirrorBoundary(
            physicalMirrorPath,
            physicalSourceRoot,
            sourcePathComparison);

        Directory.CreateDirectory(requestedMirrorPath);
        physicalMirrorPath = AppleReleaseArtifactService.ResolvePhysicalPath(requestedMirrorPath);
        ValidatePhysicalMirrorBoundary(
            physicalMirrorPath,
            physicalSourceRoot,
            sourcePathComparison);
        var mirrorDirectoryIdentity = ExistingFilePathIdentityResolver
            .ResolveDirectoryStatus(physicalMirrorPath)
            .Identity;
        var mirrorPath = physicalMirrorPath;
        normalizedMirrorPath = EnsureTrailingDirectorySeparator(mirrorPath);
        var mutationMonitor = new AppleReleaseSourceMutationMonitor(
            mirrorPath,
            "local Apple build mirror",
            "xcodebuild",
            "Discard the product and rebuild the mirror.",
            enableImmediately: false,
            ignoredMutation: args => IsExpectedGeneratedMirrorDirectoryMutation(
                args,
                mirrorPath,
                excludedGeneratedRootPaths));

        try
        {
            var args = new List<string>
            {
                "-a",
                "--delete",
                "--delete-excluded",
                "--exclude",
                "/.git",
            };
            foreach (var excludedRootPath in excludedGeneratedRootPaths)
            {
                args.Add("--exclude");
                args.Add("/" + excludedRootPath);
            }
            args.Add(normalizedSourceRoot);
            args.Add(normalizedMirrorPath);

            var processRequest = AppleTrustedExecutionEnvironment.CreateProcessRequest(
                rsyncExecutable,
                "rsync",
                "/usr/bin/rsync",
                "Exact-source local Apple build mirroring",
                sourceRoot,
                args,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout);
            processRequest.SetPreStartBoundary(() =>
            {
                var currentPhysicalMirrorPath =
                    AppleReleaseArtifactService.ResolvePhysicalPath(mirrorPath);
                ValidatePhysicalMirrorBoundary(
                    currentPhysicalMirrorPath,
                    physicalSourceRoot,
                    sourcePathComparison);
                var currentIdentity = ExistingFilePathIdentityResolver
                    .ResolveDirectoryStatus(currentPhysicalMirrorPath)
                    .Identity;
                if (!currentIdentity.Equals(
                        mirrorDirectoryIdentity,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "BuildMirrorPath changed before rsync started. " +
                        "The source repository was preserved; choose a stable mirror path and retry.");
                }
            });
            processRequest.SetCompletionBoundary(completionResult =>
            {
                if (completionResult.Succeeded && Directory.Exists(mirrorPath))
                {
                    _ = mutationMonitor.CaptureExpectedProducerOutput(
                        () => AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
                            mirrorPath,
                            "local Apple build mirror"),
                        "rsync");
                }
            });

            processRequest.ValidatePreStartBoundaryForCompatibility();
            var result = await _processRunner.RunAsync(
                processRequest,
                cancellationToken).ConfigureAwait(false);
            processRequest.InvokeCompletionBoundary(result);
            if (!result.Succeeded)
            {
                mutationMonitor.Dispose();
                return new MirrorResult(sourceRoot, mirrorPath, result, null);
            }
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

    private static bool IsExpectedGeneratedMirrorDirectoryMutation(
        FileSystemEventArgs args,
        string mirrorPath,
        IReadOnlyCollection<string> excludedGeneratedRootPaths)
    {
        if (args.ChangeType != WatcherChangeTypes.Created &&
            args.ChangeType != WatcherChangeTypes.Changed)
            return false;
        if (!excludedGeneratedRootPaths.Any(rootPath =>
                IsSameOrDescendant(
                    args.FullPath,
                    Path.Combine(mirrorPath, rootPath),
                    StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(args.FullPath);
            return (attributes & FileAttributes.Directory) != 0 &&
                   (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            // If the event target disappeared or cannot be inspected, fail closed.
            return false;
        }
    }

    private static void ValidatePhysicalMirrorBoundary(
        string physicalMirrorPath,
        string physicalSourceRoot,
        StringComparison comparison)
    {
        if (IsSameOrDescendant(
                physicalMirrorPath,
                physicalSourceRoot,
                comparison) ||
            IsSameOrDescendant(
                physicalSourceRoot,
                physicalMirrorPath,
                comparison))
        {
            throw new InvalidOperationException(
                "BuildMirrorPath must not physically overlap the mirrored build root through a symbolic-link or path alias.");
        }
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
