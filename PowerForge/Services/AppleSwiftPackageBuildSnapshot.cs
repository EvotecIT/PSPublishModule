namespace PowerForge;

/// <summary>
/// Owns the private Swift package materialization consumed by one exact-source Xcode archive.
/// </summary>
internal sealed class AppleSwiftPackageBuildSnapshot : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string?> IsolatedGitEnvironment =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_SYSTEM"] = "/dev/null",
            ["GIT_CONFIG_GLOBAL"] = "/dev/null",
            ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin"
        };

    private readonly AppleReleaseSourceTrustService _sourceTrust = new();
    private readonly AppleReleaseSourceMutationMonitor? _monitor;
    private bool _disposed;

    private AppleSwiftPackageBuildSnapshot(string rootPath)
    {
        RootPath = rootPath;
        _monitor = Directory.Exists(rootPath)
            ? new AppleReleaseSourceMutationMonitor(
                rootPath,
                "materialized Swift package root",
                "xcodebuild archive",
                "Discard the archive and resolve the exact package graph again.")
            : null;
    }

    internal string RootPath { get; }

    internal IReadOnlyDictionary<string, string?> EnvironmentVariables => IsolatedGitEnvironment;

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
            var arguments = new[]
            {
                isWorkspace ? "-workspace" : "-project",
                projectPath,
                "-scheme",
                scheme,
                "-resolvePackageDependencies",
                "-clonedSourcePackagesDirPath",
                root,
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
                        IsolatedGitEnvironment),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"xcodebuild failed to resolve the exact Swift package graph with exit code {result.ExitCode}: " +
                    (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr));
            }

            new AppleReleaseSourceTrustService().ValidateMaterializedPackageCheckouts(root);
            var snapshot = new AppleSwiftPackageBuildSnapshot(root);
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
        arguments.Add(RootPath);
        arguments.Add("-onlyUsePackageVersionsFromResolvedFile");
        arguments.Add("-disableAutomaticPackageResolution");
        arguments.Add("-skipPackageUpdates");
    }

    internal void ValidateUnchanged()
    {
        _monitor?.ValidateNoChanges();
        _sourceTrust.ValidateMaterializedPackageCheckouts(RootPath);
    }

    internal static void RejectConflictingArguments(IEnumerable<string> arguments)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-clonedSourcePackagesDirPath",
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
}
