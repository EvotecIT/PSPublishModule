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
        => !releaseRequest.ModuleOnly &&
           moduleRequest.RunMode == ConfigurationGateMode.Publish &&
           moduleRequest.IncludeModulePublishing &&
           (runPackages || willRunTools || willRunAppleApps);

    private ModuleBuildHostExecutionResult ExecuteModuleRequest(
        ModuleBuildHostBuildRequest request,
        ConfigurationGateMode runMode,
        bool includeModulePublishing,
        bool? noDotnetBuild = null,
        bool? skipInstall = null,
        bool? includeProjectPackages = null,
        CancellationToken cancellationToken = default)
    {
        var originalRunMode = request.RunMode;
        var originalIncludeModulePublishing = request.IncludeModulePublishing;
        var originalNoDotnetBuild = request.NoDotnetBuild;
        var originalNoDotnetBuildWasSpecified = request.NoDotnetBuildWasSpecified;
        var originalSkipInstall = request.SkipInstall;
        var originalIncludeProjectPackages = request.IncludeProjectPackages;
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
        }
    }
}
