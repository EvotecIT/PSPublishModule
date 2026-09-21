using System.IO;

namespace PowerForge;

/// <summary>Creates the canonical ordered module pipeline plan for a JSON-backed host request without executing it.</summary>
public sealed class ModuleBuildPlanService
{
    private readonly ILogger _logger;

    /// <summary>Creates a planner using the supplied logger.</summary>
    public ModuleBuildPlanService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Applies the same host overrides used by <see cref="ModuleBuildHostService.ExecuteBuildAsync"/> and creates the
    /// ordered pipeline plan. Legacy PowerShell scripts must first export their JSON configuration.
    /// </summary>
    public ModulePipelinePlan Plan(ModuleBuildHostBuildRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.RepositoryRoot))
            throw new ArgumentException("RepositoryRoot is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new ArgumentException("ConfigPath is required for module plan review.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.ScriptPath))
            throw new ArgumentException("ConfigPath and ScriptPath are mutually exclusive.", nameof(request));

        var repositoryRoot = Path.GetFullPath(request.RepositoryRoot);
        var configPath = ResolvePath(repositoryRoot, request.ConfigPath!);
        var preparation = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
        {
            ParameterSetName = "Config",
            ConfigPath = configPath,
            CurrentPath = repositoryRoot,
            ResolvePath = value => ResolvePath(repositoryRoot, value),
            RunMode = request.RunMode,
            ModuleVersion = request.ModuleVersion,
            PreReleaseTag = request.PreReleaseTag,
            BuildConfiguration = request.Configuration,
            BuildFramework = request.Framework,
            NoDotnetBuild = request.NoDotnetBuild,
            NoDotnetBuildWasBound = request.NoDotnetBuildWasSpecified,
            NoSign = request.NoSign,
            NoSignWasBound = request.NoSign,
            SignModule = request.SignModule,
            SignModuleWasBound = request.SignModuleWasSpecified,
            IncludeProjectPackages = request.IncludeProjectPackages,
            IncludeModulePublishing = request.IncludeModulePublishing,
            UnifiedGitHubRelease = request.UnifiedGitHubRelease,
            SkipInstall = request.SkipInstall,
            StagingPath = request.StagingPath,
            CertificateThumbprint = request.CertificateThumbprint,
            SignIncludeBinaries = request.SignIncludeBinaries,
            SignIncludeInternals = request.SignIncludeInternals,
            SignIncludeExe = request.SignIncludeExe,
            DiagnosticsBaselinePath = request.DiagnosticsBaselinePath,
            DiagnosticsBaselinePathWasBound = request.DiagnosticsBaselinePath is not null,
            GenerateDiagnosticsBaseline = request.GenerateDiagnosticsBaseline ?? false,
            GenerateDiagnosticsBaselineWasBound = request.GenerateDiagnosticsBaseline.HasValue,
            UpdateDiagnosticsBaseline = request.UpdateDiagnosticsBaseline ?? false,
            UpdateDiagnosticsBaselineWasBound = request.UpdateDiagnosticsBaseline.HasValue,
            FailOnNewDiagnostics = request.FailOnNewDiagnostics ?? false,
            FailOnNewDiagnosticsWasBound = request.FailOnNewDiagnostics.HasValue,
            FailOnDiagnosticsSeverity = ParseSeverity(request.FailOnDiagnosticsSeverity),
            FailOnDiagnosticsSeverityWasBound = request.FailOnDiagnosticsSeverity is not null
        });

        return new ModulePipelineRunner(_logger).Plan(preparation.PipelineSpec);
    }

    private static BuildDiagnosticSeverity? ParseSeverity(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.TryParse<BuildDiagnosticSeverity>(value, ignoreCase: true, out var severity)
                ? severity
                : throw new ArgumentException($"Unknown diagnostics severity '{value}'.", nameof(value));

    private static string ResolvePath(string repositoryRoot, string value)
        => Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(repositoryRoot, value));
}
