namespace PowerForge;

/// <summary>
/// Owns the private Swift package materialization consumed by one exact-source Xcode archive.
/// </summary>
internal sealed class AppleSwiftPackageBuildSnapshot : IDisposable
{
    private readonly AppleReleaseSourceTrustService _sourceTrust = new();
    private readonly IReadOnlyDictionary<string, string> _approvedPackageRevisions;
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
    private readonly AppleReleaseSourceMutationMonitor? _monitor;
    private bool _disposed;

    private AppleSwiftPackageBuildSnapshot(
        string rootPath,
        IReadOnlyDictionary<string, string> approvedPackageRevisions,
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        RootPath = rootPath;
        _approvedPackageRevisions = approvedPackageRevisions;
        _environmentVariables = environmentVariables;
        _monitor = Directory.Exists(SourcePackagesPath)
            ? new AppleReleaseSourceMutationMonitor(
                SourcePackagesPath,
                "materialized Swift package root",
                "xcodebuild archive",
                "Discard the archive and resolve the exact package graph again.")
            : null;
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
            var environmentVariables = BuildIsolatedEnvironment();
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
            var result = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        xcodeBuildExecutable,
                        Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                        arguments,
                        timeout,
                        environmentVariables,
                        captureOutput: true,
                        captureError: true,
                        inheritEnvironment: false),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"xcodebuild failed to resolve the exact Swift package graph with exit code {result.ExitCode}: " +
                    (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr));
            }

            new AppleReleaseSourceTrustService().ValidateMaterializedPackageCheckouts(
                sourcePackagesPath,
                approvedPackageRevisions);
            var snapshot = new AppleSwiftPackageBuildSnapshot(root, approvedPackageRevisions, environmentVariables);
            try
            {
                return snapshot;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
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
        _monitor?.ValidateNoChanges();
        _sourceTrust.ValidateMaterializedPackageCheckouts(SourcePackagesPath, _approvedPackageRevisions);
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
        _monitor?.Dispose();
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }

    private static IReadOnlyDictionary<string, string?> BuildIsolatedEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_SYSTEM"] = "/dev/null",
            ["GIT_CONFIG_GLOBAL"] = "/dev/null",
            ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin"
        };
        foreach (var name in new[] { "HOME", "TMPDIR", "USER", "LOGNAME", "LANG", "LC_ALL", "SSH_AUTH_SOCK" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                environment[name] = value;
        }
        return environment;
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
