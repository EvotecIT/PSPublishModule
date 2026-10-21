namespace PowerForge;

/// <summary>
/// Owns the private Swift package materialization consumed by one exact-source Xcode build.
/// </summary>
internal sealed class AppleSwiftPackageBuildSnapshot : IDisposable
{
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
    private readonly AppleReleaseSourceMutationMonitor _monitor;
    private readonly AppleReleaseSourceMutationMonitor _rootMonitor;
    private readonly AppleArchiveUploadSnapshot.SnapshotIdentity _materializedPackagesIdentity;
    private readonly AppleStableDirectoryIdentity _rootDirectory;
    private bool _disposed;

    private AppleSwiftPackageBuildSnapshot(
        string rootPath,
        AppleStableDirectoryIdentity rootDirectory,
        IReadOnlyDictionary<string, string> approvedPackageRevisions,
        IReadOnlyDictionary<string, string?> environmentVariables,
        AppleReleaseSourceMutationMonitor monitor,
        AppleReleaseSourceMutationMonitor rootMonitor,
        AppleArchiveUploadSnapshot.SnapshotIdentity materializedPackagesIdentity)
    {
        RootPath = rootPath;
        _rootDirectory = rootDirectory;
        _environmentVariables = environmentVariables;
        _monitor = monitor;
        _rootMonitor = rootMonitor;
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
        AppleReleaseSourceMutationMonitor? rootMonitor = null;
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
            Directory.CreateDirectory(Path.Combine(root, "ArchiveDerivedData"));
            var initializedRootIdentity = ExistingFilePathIdentityResolver.ResolveDirectoryStatus(root).MutationIdentity;
            rootMonitor = new AppleReleaseSourceMutationMonitor(
                parent,
                "private Swift package snapshot directory",
                "xcodebuild exact-source package resolution and build",
                "Discard the Apple product and resolve the exact package graph again.",
                enableImmediately: false,
                exactPath: root,
                includeExactPathDescendants: false,
                // Use the same synchronously drainable native boundary as package inputs.
                // FileSystemWatcher offers no flush for delayed macOS setup notifications.
                useNativeMacOSObserver: true);
            // FSEvents may deliver this directory's own create/chmod notifications after
            // subscription. Drain them during setup, while binding the ORIGINAL directory
            // and its private permissions, then arm before any resolver process can start.
            rootMonitor.CaptureExpectedProducerOutput(() =>
            {
                rootDirectory.ValidateUnchanged();
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(root) !=
                    (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
                    throw new InvalidOperationException("The private Swift package snapshot directory permissions changed during setup.");
#endif
                var currentIdentity = ExistingFilePathIdentityResolver.ResolveDirectoryStatus(root).MutationIdentity;
                if (!currentIdentity.Equals(initializedRootIdentity, StringComparison.Ordinal))
                    throw new InvalidOperationException("The private Swift package snapshot directory changed during initialization.");
                return currentIdentity;
            }, "private snapshot directory initialization");
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
            AppleArchiveUploadSnapshot.SnapshotIdentity? materializedPackagesIdentity = null;
            progress?.Invoke("Resolving the pinned Swift package graph");
            monitor = new AppleReleaseSourceMutationMonitor(
                sourcePackagesPath,
                "materialized Swift package root",
                "xcodebuild exact-source build",
                "Discard the Apple product and resolve the exact package graph again.",
                enableImmediately: false,
                unchangedMacOSCloneSource: (path, flags) => IsUnchangedPackageCloneSource(
                    sourcePackagesPath, path, flags, Volatile.Read(ref materializedPackagesIdentity)));
            var processRequest = new ProcessRunRequest(
                xcodeBuildExecutable,
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                timeout,
                environmentVariables,
                captureOutput: true,
                captureError: true,
                inheritEnvironment: false);
            processRequest.SetPreStartBoundary(rootDirectory.ValidateUnchanged);
            processRequest.SetCompletionBoundary(completionResult =>
            {
                if (!completionResult.Succeeded)
                    return;
                rootDirectory.ValidateUnchanged();
                var boundIdentity = monitor.CaptureExpectedProducerOutput(
                    () => CaptureMaterializedPackageIdentity(sourcePackagesPath),
                    "xcodebuild -resolvePackageDependencies");
                Volatile.Write(ref materializedPackagesIdentity, boundIdentity);
                progress?.Invoke("Validating materialized Swift package source and Git provenance");
                sourceTrust.ValidateMaterializedPackageCheckouts(sourcePackagesPath, approvedPackageRevisions);
                var identityAfterValidation = CaptureMaterializedPackageIdentity(sourcePackagesPath);
                if (!identityAfterValidation.Equals(boundIdentity))
                {
                    throw new InvalidOperationException(
                        "The materialized Swift package root changed while its exact package graph was being validated. " +
                        "Discard the Apple product and resolve the exact package graph again.");
                }
                rootDirectory.ValidateUnchanged();
                progress?.Invoke("Pinned Swift package graph validated");
            });
            processRequest.ValidatePreStartBoundaryForCompatibility();
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
                rootMonitor,
                materializedPackagesIdentity);
            monitor = null;
            rootMonitor = null;
            return snapshot;
        }
        catch
        {
            monitor?.Dispose();
            rootMonitor?.Dispose();
            try { rootDirectory.DeleteOwnedDirectoryIfUnchanged(); } catch { /* best effort private cleanup */ }
            throw;
        }
    }

    internal void AppendArchiveArguments(ICollection<string> arguments)
    {
        _rootDirectory.ValidateUnchanged();
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
        _rootDirectory.ValidateUnchanged();
        arguments.Add("-clonedSourcePackagesDirPath");
        arguments.Add(SourcePackagesPath);
        arguments.Add("-onlyUsePackageVersionsFromResolvedFile");
        arguments.Add("-disableAutomaticPackageResolution");
        arguments.Add("-skipPackageUpdates");
    }

    internal void ValidateUnchanged()
    {
        void ValidateCurrentIdentity()
        {
            _rootDirectory.ValidateUnchanged();
            var actual = CaptureMaterializedPackageIdentity(SourcePackagesPath);
            if (!actual.Equals(_materializedPackagesIdentity))
            {
                throw new InvalidOperationException(
                    "The materialized Swift package root changed before the exact-source xcodebuild completed. " +
                    "A transient write or hard-link alias invalidates the exact package graph.");
            }
            _rootDirectory.ValidateUnchanged();
        }

        // Preserve the most specific identity diagnostic while both observers
        // are still active, then close them around a second identical boundary.
        ValidateCurrentIdentity();
        AppleReleaseSourceMutationMonitor.ValidateNoChangesTogether(
            new[] { _monitor, _rootMonitor },
            ValidateCurrentIdentity);
    }

    internal void ValidateRootDirectoryUnchanged()
        => _rootDirectory.ValidateUnchanged();

    private static AppleArchiveUploadSnapshot.SnapshotIdentity CaptureMaterializedPackageIdentity(
        string sourcePackagesPath)
    {
        var artifactsPath = Path.Combine(sourcePackagesPath, "artifacts");
        if (Directory.Exists(artifactsPath) || File.Exists(artifactsPath)
#if NET8_0_OR_GREATER
            || new DirectoryInfo(artifactsPath).LinkTarget is not null
#endif
           )
            ValidateNoEscapingArtifactLinks(artifactsPath);
        var bindSymbolicLinks = false;
#if NET8_0_OR_GREATER
        bindSymbolicLinks = OperatingSystem.IsMacOS();
#endif
        return AppleArchiveUploadSnapshot.CaptureCompleteIdentity(
            sourcePackagesPath,
            "materialized Swift package snapshot",
            bindSymbolicLinkIdentities: bindSymbolicLinks);
    }

    private static void ValidateNoEscapingArtifactLinks(string artifactsRoot)
    {
        var root = Path.GetFullPath(artifactsRoot);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Materialized Swift binary artifact root must not be a symbolic link or reparse point: {root}");
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
                    else
                        FileSystemPathSafety.RequireRegularFile(entry);
                    continue;
                }
#if NET8_0_OR_GREATER
                var target = isDirectory ? new DirectoryInfo(entry).LinkTarget : new FileInfo(entry).LinkTarget;
                if (string.IsNullOrWhiteSpace(target) || Path.IsPathRooted(target))
                    throw new InvalidOperationException($"Materialized Swift binary artifact contains an unbound symbolic link: {entry}");
                // Validate all intermediate hops, including links which leave this root and return
                // to a contained final target. Every mutable component must remain monitored.
                var resolved = FileSystemPathSafety.ResolveRelativeLinksWithinRoot(root, entry);
                if (string.IsNullOrWhiteSpace(resolved))
                    throw new InvalidOperationException($"Materialized Swift binary artifact contains an unresolved symbolic link: {entry}");
                var relative = FrameworkCompatibility.GetRelativePath(root, resolved);
                if (Path.IsPathRooted(relative) ||
                    relative.Equals("..", StringComparison.Ordinal) ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Materialized Swift binary artifact link escapes its approved root: {entry}");
                }
                if (!isDirectory) FileSystemPathSafety.RequireRegularFile(resolved);
                var selectedIdentity = isDirectory
                    ? ExistingFilePathIdentityResolver.ResolveDirectoryStatus(entry).Identity
                    : ExistingFilePathIdentityResolver.Resolve(entry);
                var resolvedIdentity = isDirectory
                    ? ExistingFilePathIdentityResolver.ResolveDirectoryStatus(resolved).Identity
                    : ExistingFilePathIdentityResolver.Resolve(resolved);
                if (!selectedIdentity.Equals(resolvedIdentity, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Materialized Swift binary artifact link does not resolve to its contained target: {entry}");
#else
                throw new PlatformNotSupportedException("Swift binary-artifact link validation requires .NET 8 or newer.");
#endif
            }
        }
    }

    private static bool IsUnchangedPackageCloneSource(
        string sourcePackagesPath, string path, uint flags, AppleArchiveUploadSnapshot.SnapshotIdentity? approved)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsMacOS() || approved?.FileMutationIdentities is not { } identities)
            return false;
        var relative = Path.GetRelativePath(sourcePackagesPath, path).Replace('\\', '/');
        // Xcode also clones binary frameworks committed inside validated package checkouts.
        // Regular checkout/artifact files and contained artifact links have separately bound
        // physical identities. A clone never authorizes a new pathname or replacement link.
        if (!(relative.StartsWith("artifacts/", StringComparison.Ordinal) ||
              relative.StartsWith("checkouts/", StringComparison.Ordinal)) ||
            !identities.TryGetValue(relative, out var expected))
            return false;
        if (expected.StartsWith("symlink:", StringComparison.Ordinal))
        {
            if (!relative.StartsWith("artifacts/", StringComparison.Ordinal) || (flags & 0x00040000) == 0)
                return false;
            return expected.Equals(
                ExistingFilePathIdentityResolver.CapturePrivateSymbolicLinkMutationIdentity(
                    path, "approved Swift artifact clone source"), StringComparison.Ordinal);
        }
        if ((flags & 0x00010000) == 0)
            return false;
        FileSystemPathSafety.RequireRegularFile(path);
        return expected.Equals(
            ExistingFilePathIdentityResolver.CapturePrivateFileMutationIdentity(path, "approved Swift package clone source"),
            StringComparison.Ordinal);
#else
        return false;
#endif
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
        _rootMonitor.Dispose();
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
