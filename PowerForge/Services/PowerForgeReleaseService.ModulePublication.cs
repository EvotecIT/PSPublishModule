namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private readonly Func<
        ModuleBuildHostBuildRequest,
        CancellationToken,
        ModuleBuildHostExecutionResult> _executeModuleBuild;

    private static bool ShouldDeferModulePublishing(
        ModuleBuildHostBuildRequest moduleRequest,
        PowerForgeReleaseRequest releaseRequest,
        bool runPackages,
        bool willRunTools,
        bool willRunAppleApps,
        bool hasAfterStagingValidation)
        => moduleRequest.RunMode == ConfigurationGateMode.Publish &&
           moduleRequest.IncludeModulePublishing &&
           (hasAfterStagingValidation ||
            ((!releaseRequest.ModuleOnly || HasPostBuildSourceStateGuard(releaseRequest)) &&
             (runPackages || willRunTools || willRunAppleApps || HasPostBuildSourceStateGuard(releaseRequest))));

    private ModuleBuildHostExecutionResult ExecuteModuleRequest(
        ModuleBuildHostBuildRequest request,
        ConfigurationGateMode runMode,
        bool includeModulePublishing,
        bool? noDotnetBuild = null,
        bool? skipInstall = null,
        bool? includeProjectPackages = null,
        bool? reuseStaging = null,
        bool? releaseCheckpoint = null,
        CancellationToken cancellationToken = default)
    {
        var originalRunMode = request.RunMode;
        var originalIncludeModulePublishing = request.IncludeModulePublishing;
        var originalNoDotnetBuild = request.NoDotnetBuild;
        var originalNoDotnetBuildWasSpecified = request.NoDotnetBuildWasSpecified;
        var originalSkipInstall = request.SkipInstall;
        var originalIncludeProjectPackages = request.IncludeProjectPackages;
        var originalReuseStaging = request.ReuseStaging;
        var originalReleaseCheckpoint = request.ReleaseCheckpoint;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.RunMode = runMode;
            request.IncludeModulePublishing = includeModulePublishing;
            if (noDotnetBuild.HasValue)
            {
                request.NoDotnetBuild = noDotnetBuild.Value;
                request.NoDotnetBuildWasSpecified = true;
            }
            if (skipInstall.HasValue)
                request.SkipInstall = skipInstall.Value;
            if (includeProjectPackages.HasValue)
                request.IncludeProjectPackages = includeProjectPackages.Value;
            if (reuseStaging.HasValue)
                request.ReuseStaging = reuseStaging.Value;
            if (releaseCheckpoint.HasValue)
                request.ReleaseCheckpoint = releaseCheckpoint.Value;

            return _executeModuleBuild(request, cancellationToken);
        }
        finally
        {
            request.RunMode = originalRunMode;
            request.IncludeModulePublishing = originalIncludeModulePublishing;
            request.NoDotnetBuild = originalNoDotnetBuild;
            request.NoDotnetBuildWasSpecified = originalNoDotnetBuildWasSpecified;
            request.SkipInstall = originalSkipInstall;
            request.IncludeProjectPackages = originalIncludeProjectPackages;
            request.ReuseStaging = originalReuseStaging;
            request.ReleaseCheckpoint = originalReleaseCheckpoint;
        }
    }

    private sealed class TemporaryReleaseDirectory : IDisposable
    {
        private readonly ILogger _logger;
        private string? _path;

        public TemporaryReleaseDirectory(ILogger logger)
        {
            _logger = logger;
        }

        public string GetOrCreateSubdirectory(string name)
        {
            if (_path is null)
            {
                _path = Path.Combine(
                    Path.GetTempPath(),
                    "PowerForge",
                    "unified-release",
                    Guid.NewGuid().ToString("N"));
            }

            string path = Path.Combine(_path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (string.IsNullOrWhiteSpace(_path) || !Directory.Exists(_path))
                return;

            try
            {
                Directory.Delete(_path!, recursive: true);
            }
            catch (Exception exception)
            {
                _logger.Verbose(
                    $"Unable to remove temporary release directory '{_path}': {exception.Message}");
            }
        }
    }
}
