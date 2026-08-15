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
        bool willRunAppleApps)
        => moduleRequest.RunMode == ConfigurationGateMode.Publish &&
           moduleRequest.IncludeModulePublishing &&
           (!releaseRequest.ModuleOnly || HasPostBuildSourceStateGuard(releaseRequest)) &&
           (runPackages || willRunTools || willRunAppleApps || HasPostBuildSourceStateGuard(releaseRequest));

    private ModuleBuildHostExecutionResult ExecuteModuleRequest(
        ModuleBuildHostBuildRequest request,
        ConfigurationGateMode runMode,
        bool includeModulePublishing,
        bool? noDotnetBuild = null,
        bool? skipInstall = null,
        bool? includeProjectPackages = null,
        bool? reuseStaging = null,
        CancellationToken cancellationToken = default)
    {
        var originalRunMode = request.RunMode;
        var originalIncludeModulePublishing = request.IncludeModulePublishing;
        var originalNoDotnetBuild = request.NoDotnetBuild;
        var originalNoDotnetBuildWasSpecified = request.NoDotnetBuildWasSpecified;
        var originalSkipInstall = request.SkipInstall;
        var originalIncludeProjectPackages = request.IncludeProjectPackages;
        var originalReuseStaging = request.ReuseStaging;
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
        }
    }

    private sealed class DeferredModuleStagingDirectory : IDisposable
    {
        private readonly ILogger _logger;
        private string? _path;

        public DeferredModuleStagingDirectory(ILogger logger)
        {
            _logger = logger;
        }

        public string GetOrCreatePath()
        {
            if (_path is not null)
                return _path;

            _path = Path.Combine(
                Path.GetTempPath(),
                "PowerForge",
                "unified-release",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_path);
            return _path;
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
                    $"Unable to remove deferred module staging directory '{_path}': {exception.Message}");
            }
        }
    }
}
