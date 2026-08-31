namespace PowerForge;

/// <summary>
/// Owns the private Swift package materialization consumed by one exact-source Xcode build.
/// </summary>
internal sealed class AppleSwiftPackageBuildSnapshot : IDisposable
{
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
    private readonly AppleReleaseSourceMutationMonitor _monitor;
    private readonly AppleArchiveUploadSnapshot.SnapshotIdentity _materializedPackagesIdentity;
    private readonly AppleStableDirectoryIdentity _rootDirectory;
    private bool _disposed;

    private AppleSwiftPackageBuildSnapshot(
        string rootPath,
        AppleStableDirectoryIdentity rootDirectory,
        IReadOnlyDictionary<string, string> approvedPackageRevisions,
        IReadOnlyDictionary<string, string?> environmentVariables,
        AppleReleaseSourceMutationMonitor monitor,
        AppleArchiveUploadSnapshot.SnapshotIdentity materializedPackagesIdentity)
    {
        RootPath = rootPath;
        _rootDirectory = rootDirectory;
        _environmentVariables = environmentVariables;
        _monitor = monitor;
        _materializedPackagesIdentity = materializedPackagesIdentity;
    }

    internal string RootPath { get; }

    internal string SourcePackagesPath => Path.Combine(RootPath, "SourcePackages");

    internal string ResolverDerivedDataPath => Path.Combine(RootPath, "ResolverDerivedData");

    internal string ArchiveDerivedDataPath => Path.Combine(RootPath, "ArchiveDerivedData");

    internal IReadOnlyDictionary<string, string?> EnvironmentVariables => _environmentVariables;

    internal static IReadOnlyDictionary<string, string> ReadApprovedRemotePackages(
        string projectPath)
    {
        var repositoryRoot = ResolveRepositoryRoot(projectPath);
        return new AppleReleaseSourceTrustService()
            .ReadApprovedLocalBuildPackageRevisions(
                repositoryRoot,
                projectPath);
    }

    internal static async Task<AppleSwiftPackageBuildSnapshot> CreateAsync(
        IProcessRunner processRunner,
        string xcodeBuildExecutable,
        string projectPath,
        bool isWorkspace,
        string scheme,
        IReadOnlyDictionary<string, string> approvedPackageRevisions,
        string sourceRoot,
        StringComparison sourcePathComparison,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? progress = null)
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "PowerForge",
            "apple-swiftpm-build-snapshots");
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            parent,
            sourceRoot,
            "Swift package snapshot root",
            sourcePathComparison);
        Directory.CreateDirectory(parent);
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            parent,
            sourceRoot,
            "Swift package snapshot root",
            sourcePathComparison);
        parent = AppleReleaseArtifactService.ResolvePhysicalPath(parent);
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rootDirectory = AppleStableDirectoryIdentity.Capture(
            root,
            "private Swift package snapshot directory");
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            rootDirectory.Path,
            sourceRoot,
            "Swift package snapshot root",
            sourcePathComparison);
        root = rootDirectory.Path;
        AppleReleaseSourceMutationMonitor? monitor = null;
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            var sourcePackagesPath = Path.Combine(root, "SourcePackages");
            var derivedDataPath = Path.Combine(root, "ResolverDerivedData");
            Directory.CreateDirectory(sourcePackagesPath);
            Directory.CreateDirectory(derivedDataPath);
            var environmentVariables = AppleTrustedExecutionEnvironment.Create(isolateGitConfiguration: true);
            var arguments = new[]
            {
                isWorkspace ? "-workspace" : "-project",
                projectPath,
                "-scheme",
                scheme,
                "-resolvePackageDependencies",
                "-clonedSourcePackagesDirPath",
                sourcePackagesPath,
                "-derivedDataPath",
                derivedDataPath,
                "-onlyUsePackageVersionsFromResolvedFile",
                "-disableAutomaticPackageResolution",
                "-skipPackageUpdates"
            };
            var sourceTrust = new AppleReleaseSourceTrustService();
            progress?.Invoke("Resolving the pinned Swift package graph");
            monitor = new AppleReleaseSourceMutationMonitor(
                sourcePackagesPath,
                "materialized Swift package root",
                "xcodebuild exact-source build",
                "Discard the Apple product and resolve the exact package graph again.",
                enableImmediately: false);
            AppleArchiveUploadSnapshot.SnapshotIdentity? materializedPackagesIdentity = null;
            var processRequest = new ProcessRunRequest(
                xcodeBuildExecutable,
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                timeout,
                environmentVariables,
                captureOutput: true,
                captureError: true,
                inheritEnvironment: false);
            processRequest.SetCompletionBoundary(completionResult =>
            {
                if (!completionResult.Succeeded)
                    return;
                materializedPackagesIdentity = monitor.CaptureExpectedProducerOutput(
                    () => CaptureMaterializedPackageIdentity(sourcePackagesPath),
                    "xcodebuild -resolvePackageDependencies");
                progress?.Invoke("Validating materialized Swift package source and Git provenance");
                sourceTrust.ValidateMaterializedPackageCheckouts(sourcePackagesPath, approvedPackageRevisions);
                var identityAfterValidation = CaptureMaterializedPackageIdentity(sourcePackagesPath);
                if (!identityAfterValidation.Equals(materializedPackagesIdentity))
                {
                    throw new InvalidOperationException(
                        "The materialized Swift package root changed while its exact package graph was being validated. " +
                        "Discard the Apple product and resolve the exact package graph again.");
                }
                monitor.ValidateNoChanges();
                progress?.Invoke("Pinned Swift package graph validated");
            });
            var result = await processRunner.RunAsync(processRequest, cancellationToken).ConfigureAwait(false);
            processRequest.InvokeCompletionBoundary(result);
            if (!result.Succeeded)
            {
                monitor.Dispose();
                throw new InvalidOperationException(
                    $"xcodebuild failed to resolve the exact Swift package graph with exit code {result.ExitCode}: " +
                    (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr));
            }
            if (materializedPackagesIdentity is null)
            {
                monitor.Dispose();
                throw new InvalidOperationException(
                    "xcodebuild completed without binding the exact materialized Swift package graph at its process completion boundary.");
            }

            var snapshot = new AppleSwiftPackageBuildSnapshot(
                root,
                rootDirectory,
                approvedPackageRevisions,
                environmentVariables,
                monitor,
                materializedPackagesIdentity);
            monitor = null;
            return snapshot;
        }
        catch
        {
            monitor?.Dispose();
            try { rootDirectory.DeleteOwnedDirectoryIfUnchanged(); } catch { /* best effort private cleanup */ }
            throw;
        }
    }

    internal void AppendArchiveArguments(ICollection<string> arguments)
    {
        arguments.Add("-clonedSourcePackagesDirPath");
        arguments.Add(SourcePackagesPath);
        arguments.Add("-derivedDataPath");
        arguments.Add(ArchiveDerivedDataPath);
        arguments.Add("-onlyUsePackageVersionsFromResolvedFile");
        arguments.Add("-disableAutomaticPackageResolution");
        arguments.Add("-skipPackageUpdates");
    }

    internal void AppendLocalBuildArguments(ICollection<string> arguments)
    {
        arguments.Add("-clonedSourcePackagesDirPath");
        arguments.Add(SourcePackagesPath);
        arguments.Add("-onlyUsePackageVersionsFromResolvedFile");
        arguments.Add("-disableAutomaticPackageResolution");
        arguments.Add("-skipPackageUpdates");
    }

    internal void ValidateUnchanged()
    {
        var actual = CaptureMaterializedPackageIdentity(SourcePackagesPath);
        if (!actual.Equals(_materializedPackagesIdentity))
        {
            throw new InvalidOperationException(
                "The materialized Swift package root changed before the exact-source xcodebuild completed. " +
                "A transient write or hard-link alias invalidates the exact package graph.");
        }
        _monitor.ValidateNoChanges();
    }

    private static AppleArchiveUploadSnapshot.SnapshotIdentity CaptureMaterializedPackageIdentity(
        string sourcePackagesPath)
    {
        var artifactsPath = Path.Combine(sourcePackagesPath, "artifacts");
        if (Directory.Exists(artifactsPath))
            ValidateNoEscapingArtifactLinks(artifactsPath);
        return AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
            sourcePackagesPath,
            "materialized Swift package snapshot");
    }

    private static void ValidateNoEscapingArtifactLinks(string artifactsRoot)
    {
        var root = Path.GetFullPath(artifactsRoot);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    if (isDirectory)
                        pending.Push(entry);
                    continue;
                }
#if NET8_0_OR_GREATER
                var target = isDirectory ? new DirectoryInfo(entry).LinkTarget : new FileInfo(entry).LinkTarget;
                if (string.IsNullOrWhiteSpace(target) || Path.IsPathRooted(target))
                    throw new InvalidOperationException($"Materialized Swift binary artifact contains an unbound symbolic link: {entry}");
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry)!, target));
                var relative = FrameworkCompatibility.GetRelativePath(root, resolved);
                if (Path.IsPathRooted(relative) ||
                    relative.Equals("..", StringComparison.Ordinal) ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Materialized Swift binary artifact link escapes its approved root: {entry}");
                }
#else
                throw new PlatformNotSupportedException("Swift binary-artifact link validation requires .NET 8 or newer.");
#endif
            }
        }
    }

    internal static void RejectConflictingArguments(IEnumerable<string> arguments)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-clonedSourcePackagesDirPath",
            "-derivedDataPath",
            "-packageCachePath",
            "-resolvePackageDependencies",
            "-disableAutomaticPackageResolution",
            "-onlyUsePackageVersionsFromResolvedFile",
            "-skipPackageUpdates",
            "-skipPackagePluginValidation",
            "-skipPackageSignatureValidation"
        };
        var conflict = arguments.FirstOrDefault(argument => forbidden.Contains(argument));
        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"Exact-source Apple archives own Swift package materialization; additional xcodebuild argument '{conflict}' is not allowed.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _monitor.Dispose();
        try { _rootDirectory.DeleteOwnedDirectoryIfUnchanged(); } catch { /* best effort after archive */ }
    }

    internal static string ResolveRepositoryRoot(string startPath)
    {
        var fullStartPath = Path.GetFullPath(startPath);
        var current = new DirectoryInfo(Directory.Exists(fullStartPath) ? fullStartPath : Path.GetDirectoryName(fullStartPath)!);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, ".git");
            if (File.Exists(marker) || Directory.Exists(marker))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException($"Exact-source Xcode project is not inside a Git worktree: {startPath}");
    }
}
