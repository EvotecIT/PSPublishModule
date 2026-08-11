namespace PowerForge;

/// <summary>
/// Owns the private Swift package materialization consumed by one exact-source Xcode archive.
/// </summary>
internal sealed class AppleSwiftPackageBuildSnapshot : IDisposable
{
    private readonly AppleReleaseSourceTrustService _sourceTrust = new();
    private readonly IReadOnlyDictionary<string, string> _approvedPackageRevisions;
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
    private readonly AppleReleaseSourceMutationMonitor _monitor;
    private readonly string _materializedPackagesSha256;
    private bool _disposed;

    private AppleSwiftPackageBuildSnapshot(
        string rootPath,
        IReadOnlyDictionary<string, string> approvedPackageRevisions,
        IReadOnlyDictionary<string, string?> environmentVariables,
        AppleReleaseSourceMutationMonitor monitor,
        string materializedPackagesSha256)
    {
        RootPath = rootPath;
        _approvedPackageRevisions = approvedPackageRevisions;
        _environmentVariables = environmentVariables;
        _monitor = monitor;
        _materializedPackagesSha256 = materializedPackagesSha256;
    }

    internal string RootPath { get; }

    internal string SourcePackagesPath => Path.Combine(RootPath, "SourcePackages");

    internal string DerivedDataPath => Path.Combine(RootPath, "DerivedData");

    internal IReadOnlyDictionary<string, string?> EnvironmentVariables => _environmentVariables;

    internal static async Task<AppleSwiftPackageBuildSnapshot> CreateAsync(
        IProcessRunner processRunner,
        string xcodeBuildExecutable,
        string projectPath,
        bool isWorkspace,
        string scheme,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var parent = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-swiftpm-build-snapshots");
        Directory.CreateDirectory(parent);
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppleReleaseSourceMutationMonitor? monitor = null;
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            var sourcePackagesPath = Path.Combine(root, "SourcePackages");
            var derivedDataPath = Path.Combine(root, "DerivedData");
            Directory.CreateDirectory(sourcePackagesPath);
            Directory.CreateDirectory(derivedDataPath);
            var repositoryRoot = FindRepositoryRoot(projectPath);
            var approvedPackageRevisions = new AppleReleaseSourceTrustService().ReadApprovedTrackedPackageRevisions(
                repositoryRoot,
                DiscoverApprovedPackageLocks(repositoryRoot, projectPath));
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
            monitor = new AppleReleaseSourceMutationMonitor(
                sourcePackagesPath,
                "materialized Swift package root",
                "xcodebuild archive",
                "Discard the archive and resolve the exact package graph again.",
                enableImmediately: false);
            string? materializedPackagesSha256 = null;
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
                materializedPackagesSha256 = monitor.CaptureExpectedProducerOutput(
                    () => CaptureMaterializedPackageIdentity(
                        sourceTrust,
                        sourcePackagesPath,
                        approvedPackageRevisions),
                    "xcodebuild -resolvePackageDependencies");
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
            if (string.IsNullOrWhiteSpace(materializedPackagesSha256))
            {
                monitor.Dispose();
                throw new InvalidOperationException(
                    "xcodebuild completed without binding the exact materialized Swift package graph at its process completion boundary.");
            }

            var snapshot = new AppleSwiftPackageBuildSnapshot(
                root,
                approvedPackageRevisions,
                environmentVariables,
                monitor,
                materializedPackagesSha256!);
            monitor = null;
            return snapshot;
        }
        catch
        {
            monitor?.Dispose();
            try { AppleArtifactCopy.DeleteOwnedDirectory(root); } catch { /* best effort private cleanup */ }
            throw;
        }
    }

    internal void AppendArchiveArguments(ICollection<string> arguments)
    {
        arguments.Add("-clonedSourcePackagesDirPath");
        arguments.Add(SourcePackagesPath);
        arguments.Add("-derivedDataPath");
        arguments.Add(DerivedDataPath);
        arguments.Add("-onlyUsePackageVersionsFromResolvedFile");
        arguments.Add("-disableAutomaticPackageResolution");
        arguments.Add("-skipPackageUpdates");
    }

    internal void ValidateUnchanged()
    {
        var actual = CaptureMaterializedPackageIdentity(
            _sourceTrust,
            SourcePackagesPath,
            _approvedPackageRevisions);
        if (!actual.Equals(_materializedPackagesSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The materialized Swift package root changed before xcodebuild archive.");
        _monitor.ValidateNoChanges();
    }

    private static string CaptureMaterializedPackageIdentity(
        AppleReleaseSourceTrustService sourceTrust,
        string sourcePackagesPath,
        IReadOnlyDictionary<string, string> approvedPackageRevisions)
    {
        sourceTrust.ValidateMaterializedPackageCheckouts(sourcePackagesPath, approvedPackageRevisions);
        var artifactsPath = Path.Combine(sourcePackagesPath, "artifacts");
        if (Directory.Exists(artifactsPath))
            ValidateNoEscapingArtifactLinks(artifactsPath);
        return AppleNotarizationService.ComputeArtifactSha256(sourcePackagesPath);
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
        try { AppleArtifactCopy.DeleteOwnedDirectory(RootPath); } catch { /* best effort after archive */ }
    }

    private static IEnumerable<string> DiscoverApprovedPackageLocks(string repositoryRoot, string projectPath)
    {
        var project = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(project)
            ?? throw new InvalidOperationException($"Xcode project path has no parent: {project}");
        return new[]
            {
                Path.Combine(repositoryRoot, "Package.resolved"),
                Path.Combine(projectDirectory, "Package.resolved"),
                Path.Combine(project, "xcshareddata", "swiftpm", "Package.resolved"),
                Path.Combine(project, "project.xcworkspace", "xcshareddata", "swiftpm", "Package.resolved")
            }
            .Distinct(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(File.Exists);
    }

    private static string FindRepositoryRoot(string startPath)
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
