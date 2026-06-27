using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Measures managed module lifecycle scenarios using the managed C# module engine.
/// </summary>
public sealed class ManagedModuleBenchmarkService
{
    private readonly ManagedModuleInstallService _installService;
    private readonly ManagedModuleUpdateService _updateService;

    /// <summary>
    /// Creates a managed module benchmark service.
    /// </summary>
    /// <param name="logger">Logger used by managed module services.</param>
    /// <param name="installService">Optional install service override.</param>
    /// <param name="updateService">Optional update service override.</param>
    public ManagedModuleBenchmarkService(
        ILogger logger,
        ManagedModuleInstallService? installService = null,
        ManagedModuleUpdateService? updateService = null)
    {
        var safeLogger = logger ?? new NullLogger();
        _installService = installService ?? new ManagedModuleInstallService(safeLogger);
        _updateService = updateService ?? new ManagedModuleUpdateService(safeLogger);
    }

    /// <summary>
    /// Runs benchmark scenarios and returns typed measurement results.
    /// </summary>
    /// <param name="request">Benchmark request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Benchmark result.</returns>
    public async Task<ManagedModuleBenchmarkResult> RunAsync(
        ManagedModuleBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var started = DateTimeOffset.UtcNow;
        var runs = new List<ManagedModuleBenchmarkRunResult>();

        foreach (var scenario in request.Scenarios)
        {
            for (var iteration = 1; iteration <= Math.Max(1, scenario.Iterations); iteration++)
            {
                var run = await RunScenarioAsync(scenario, iteration, request.ContinueOnError, cancellationToken)
                    .ConfigureAwait(false);
                runs.Add(run);
            }
        }

        return new ManagedModuleBenchmarkResult
        {
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Runs = runs
        };
    }

    private async Task<ManagedModuleBenchmarkRunResult> RunScenarioAsync(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var run = scenario.Operation switch
            {
                ManagedModuleBenchmarkOperation.Install => MapInstall(
                    scenario,
                    iteration,
                    await _installService.InstallAsync(CreateInstallRequest(scenario, false), cancellationToken)
                        .ConfigureAwait(false)),
                ManagedModuleBenchmarkOperation.Save => MapInstall(
                    scenario,
                    iteration,
                    await _installService.InstallAsync(CreateInstallRequest(scenario, true), cancellationToken)
                        .ConfigureAwait(false)),
                ManagedModuleBenchmarkOperation.Update => MapUpdate(
                    scenario,
                    iteration,
                    await _updateService.UpdateAsync(CreateUpdateRequest(scenario), cancellationToken)
                        .ConfigureAwait(false)),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario.Operation, "Unsupported benchmark operation.")
            };

            stopwatch.Stop();
            run.Elapsed = stopwatch.Elapsed;
            return run;
        }
        catch (Exception ex) when (continueOnError)
        {
            stopwatch.Stop();
            return CreateFailedRun(scenario, iteration, stopwatch.Elapsed, ex);
        }
    }

    private static ManagedModuleInstallRequest CreateInstallRequest(ManagedModuleBenchmarkScenario scenario, bool forceCustomRoot)
    {
        var moduleRoot = NormalizeOptional(scenario.ModuleRoot);
        return new ManagedModuleInstallRequest
        {
            Repository = scenario.Repository,
            Name = scenario.Name,
            Version = scenario.Version,
            MinimumVersion = scenario.MinimumVersion,
            MaximumVersion = scenario.MaximumVersion,
            VersionPolicy = scenario.VersionPolicy,
            IncludePrerelease = scenario.IncludePrerelease,
            Scope = forceCustomRoot || !string.IsNullOrWhiteSpace(moduleRoot)
                ? ManagedModuleInstallScope.Custom
                : scenario.Scope,
            ShellEdition = scenario.ShellEdition,
            ModuleRoot = moduleRoot,
            PackageCacheDirectory = NormalizeOptional(scenario.PackageCacheDirectory),
            Credential = scenario.Credential,
            Force = scenario.Force,
            AllowClobber = scenario.AllowClobber,
            AcceptLicense = scenario.AcceptLicense,
            SkipDependencyCheck = scenario.SkipDependencyCheck
        };
    }

    private static ManagedModuleUpdateRequest CreateUpdateRequest(ManagedModuleBenchmarkScenario scenario)
    {
        var moduleRoot = NormalizeOptional(scenario.ModuleRoot);
        return new ManagedModuleUpdateRequest
        {
            Repository = scenario.Repository,
            Name = scenario.Name,
            Version = scenario.Version,
            MinimumVersion = scenario.MinimumVersion,
            MaximumVersion = scenario.MaximumVersion,
            VersionPolicy = scenario.VersionPolicy,
            IncludePrerelease = scenario.IncludePrerelease,
            Scope = !string.IsNullOrWhiteSpace(moduleRoot)
                ? ManagedModuleInstallScope.Custom
                : scenario.Scope,
            ShellEdition = scenario.ShellEdition,
            ModuleRoot = moduleRoot,
            PackageCacheDirectory = NormalizeOptional(scenario.PackageCacheDirectory),
            Credential = scenario.Credential,
            Force = scenario.Force,
            AllowClobber = scenario.AllowClobber,
            AcceptLicense = scenario.AcceptLicense,
            SkipDependencyCheck = scenario.SkipDependencyCheck
        };
    }

    private static ManagedModuleBenchmarkRunResult MapInstall(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        ManagedModuleInstallResult result)
        => new()
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Iteration = iteration,
            Succeeded = true,
            Status = result.Status.ToString(),
            Version = result.Version,
            ModuleRoot = result.ModuleRoot,
            ModulePath = result.ModulePath,
            ServiceElapsed = result.Elapsed,
            PackageBytes = result.Download?.BytesWritten ?? 0,
            ExtractedBytes = result.ExtractedBytes,
            FileCount = result.FileCount,
            DependencyCount = result.DependencyResults.Count,
            FromCache = result.Download?.FromCache ?? false
        };

    private static ManagedModuleBenchmarkRunResult MapUpdate(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        ManagedModuleUpdateResult result)
        => new()
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Iteration = iteration,
            Succeeded = true,
            Status = result.Status.ToString(),
            Version = result.TargetVersion,
            PreviousVersion = result.PreviousVersion,
            ModuleRoot = result.ModuleRoot,
            ModulePath = result.ModulePath,
            ServiceElapsed = result.Elapsed,
            PackageBytes = result.InstallResult?.Download?.BytesWritten ?? 0,
            ExtractedBytes = result.InstallResult?.ExtractedBytes ?? 0,
            FileCount = result.InstallResult?.FileCount ?? 0,
            DependencyCount = result.InstallResult?.DependencyResults.Count ?? 0,
            FromCache = result.InstallResult?.Download?.FromCache ?? false
        };

    private static ManagedModuleBenchmarkRunResult CreateFailedRun(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        TimeSpan elapsed,
        Exception exception)
        => new()
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Iteration = iteration,
            Succeeded = false,
            Status = "Failed",
            Elapsed = elapsed,
            ErrorMessage = exception.GetBaseException().Message
        };

    private static void Validate(ManagedModuleBenchmarkRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Scenarios is null || request.Scenarios.Count == 0)
            throw new ArgumentException("At least one benchmark scenario is required.", nameof(request));

        foreach (var scenario in request.Scenarios)
            ValidateScenario(scenario);
    }

    private static void ValidateScenario(ManagedModuleBenchmarkScenario scenario)
    {
        if (scenario is null)
            throw new ArgumentException("Benchmark scenario cannot be null.");
        if (scenario.Repository is null)
            throw new ArgumentException("Benchmark scenario repository is required.");
        if (string.IsNullOrWhiteSpace(scenario.Name))
            throw new ArgumentException("Benchmark scenario module name is required.");
        if (scenario.Operation == ManagedModuleBenchmarkOperation.Save &&
            string.IsNullOrWhiteSpace(scenario.ModuleRoot))
            throw new ArgumentException("Save benchmark scenarios require ModuleRoot.");
        if (scenario.Iterations < 1)
            throw new ArgumentException("Benchmark scenario iterations must be greater than zero.");
    }

    private static string ResolveScenarioId(ManagedModuleBenchmarkScenario scenario)
        => string.IsNullOrWhiteSpace(scenario.Id)
            ? scenario.Operation + ":" + scenario.Name.Trim()
            : scenario.Id.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
