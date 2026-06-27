using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Measures managed module lifecycle scenarios using the managed C# module engine.
/// </summary>
public sealed partial class ManagedModuleBenchmarkService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleInstallService _installService;
    private readonly ManagedModuleUpdateService _updateService;
    private readonly ManagedModulePublishService _publishService;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly IPowerShellRunner _compatibilityPowerShellRunner;
    private readonly Func<ManagedModuleBenchmarkScenario, ManagedModuleBenchmarkEngine, ModuleDependencyInstallResult>? _compatibilityRunner;

    /// <summary>
    /// Creates a managed module benchmark service.
    /// </summary>
    /// <param name="logger">Logger used by managed module services.</param>
    /// <param name="installService">Optional install service override.</param>
    /// <param name="updateService">Optional update service override.</param>
    /// <param name="publishService">Optional publish service override.</param>
    /// <param name="compatibilityRunner">Optional compatibility benchmark runner override.</param>
    /// <param name="compatibilityPowerShellRunner">Optional PowerShell runner used by compatibility benchmark engines.</param>
    /// <param name="repositoryClient">Optional managed repository client used by metadata lookup benchmarks.</param>
    public ManagedModuleBenchmarkService(
        ILogger logger,
        ManagedModuleInstallService? installService = null,
        ManagedModuleUpdateService? updateService = null,
        ManagedModulePublishService? publishService = null,
        Func<ManagedModuleBenchmarkScenario, ManagedModuleBenchmarkEngine, ModuleDependencyInstallResult>? compatibilityRunner = null,
        IPowerShellRunner? compatibilityPowerShellRunner = null,
        ManagedModuleRepositoryClient? repositoryClient = null)
    {
        var safeLogger = logger ?? new NullLogger();
        _logger = safeLogger;
        _installService = installService ?? new ManagedModuleInstallService(safeLogger);
        _updateService = updateService ?? new ManagedModuleUpdateService(safeLogger);
        _publishService = publishService ?? new ManagedModulePublishService(safeLogger);
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(safeLogger);
        _compatibilityPowerShellRunner = compatibilityPowerShellRunner ?? new PowerShellRunner();
        _compatibilityRunner = compatibilityRunner;
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
        var engines = ResolveEngines(request);
        var isolateModuleRoots = engines.Count > 1;

        foreach (var scenario in request.Scenarios)
        {
            foreach (var engine in engines)
            {
                for (var iteration = 1; iteration <= Math.Max(1, scenario.Iterations); iteration++)
                {
                    var runScenario = CreateRunScenario(scenario, engine, iteration, isolateModuleRoots);
                    var run = await RunScenarioAsync(runScenario, engine, iteration, request.ContinueOnError, cancellationToken)
                        .ConfigureAwait(false);
                    runs.Add(run);
                }
            }
        }

        return new ManagedModuleBenchmarkResult
        {
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Runs = runs,
            TransitionGates = ManagedModuleBenchmarkTransitionGateEvaluator.Evaluate(runs)
        };
    }

    private async Task<ManagedModuleBenchmarkRunResult> RunScenarioAsync(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var run = engine == ManagedModuleBenchmarkEngine.Managed
                ? await RunManagedScenarioAsync(scenario, iteration, cancellationToken).ConfigureAwait(false)
                : RunCompatibilityScenario(scenario, engine, iteration);

            stopwatch.Stop();
            run.Elapsed = stopwatch.Elapsed;
            return run;
        }
        catch (Exception ex) when (continueOnError)
        {
            stopwatch.Stop();
            return CreateFailedRun(scenario, engine, iteration, stopwatch.Elapsed, ex);
        }
    }

    private async Task<ManagedModuleBenchmarkRunResult> RunManagedScenarioAsync(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        CancellationToken cancellationToken)
        => scenario.Operation switch
        {
            ManagedModuleBenchmarkOperation.Find => await RunManagedFindScenarioAsync(scenario, iteration, cancellationToken)
                .ConfigureAwait(false),
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
            ManagedModuleBenchmarkOperation.Publish => MapPublish(
                scenario,
                iteration,
                await _publishService.PublishAsync(CreatePublishRequest(scenario), cancellationToken)
                    .ConfigureAwait(false)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario.Operation, "Unsupported benchmark operation.")
        };

    private async Task<ManagedModuleBenchmarkRunResult> RunManagedFindScenarioAsync(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        CancellationToken cancellationToken)
    {
        var startedRequests = _repositoryClient.RequestCount;
        var stopwatch = Stopwatch.StartNew();
        var versions = ManagedModuleSearchMatcher.HasWildcard(scenario.Name)
            ? await _repositoryClient.SearchPackagesAsync(
                    scenario.Repository,
                    scenario.Name,
                    scenario.IncludePrerelease,
                    scenario.Credential,
                    take: 100,
                    cancellationToken)
                .ConfigureAwait(false)
            : await _repositoryClient.GetVersionsAsync(
                    scenario.Repository,
                    scenario.Name,
                    scenario.IncludePrerelease,
                    scenario.Credential,
                    cancellationToken)
                .ConfigureAwait(false);
        stopwatch.Stop();

        var selected = SelectFindVersion(versions);
        return new ManagedModuleBenchmarkRunResult
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = ManagedModuleBenchmarkEngine.Managed.ToString(),
            Iteration = iteration,
            Succeeded = versions.Count > 0,
            Status = versions.Count > 0 ? "Found" : "NotFound",
            ModuleName = selected?.Name ?? scenario.Name,
            Version = selected?.Version,
            ServiceElapsed = stopwatch.Elapsed,
            PackageCount = versions.Count,
            RepositoryRequestCount = _repositoryClient.RequestCount - startedRequests
        };
    }

    private ManagedModuleBenchmarkRunResult RunCompatibilityScenario(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration)
    {
        if (!string.IsNullOrWhiteSpace(scenario.VersionPolicy))
            throw new InvalidOperationException("Compatibility benchmark engines support Version, MinimumVersion, and MaximumVersion. Use the managed engine for VersionPolicy measurements.");

        return scenario.Operation switch
        {
            ManagedModuleBenchmarkOperation.Install or ManagedModuleBenchmarkOperation.Update => _compatibilityRunner is null
                ? throw new InvalidOperationException(
                    "Default compatibility install/update benchmarks are disabled because Install-Module and Install-PSResource do not provide a reliable custom module-root isolation contract. Use managed install/update benchmarks, save/publish compatibility benchmarks, or run native install/update comparisons in a disposable host with an explicit compatibility runner.")
                : MapCompatibilityResult(
                    scenario,
                    engine,
                    iteration,
                    _compatibilityRunner(scenario, engine)),
            ManagedModuleBenchmarkOperation.Save => RunCompatibilitySaveScenario(scenario, engine, iteration),
            ManagedModuleBenchmarkOperation.Publish => RunCompatibilityPublishScenario(scenario, engine, iteration),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario.Operation, "Unsupported benchmark operation.")
        };
    }

    private ManagedModuleBenchmarkRunResult RunCompatibilitySaveScenario(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration)
    {
        if (!string.IsNullOrWhiteSpace(scenario.MaximumVersion) &&
            engine == ManagedModuleBenchmarkEngine.PowerShellGet)
        {
            throw new InvalidOperationException("PowerShellGet Save-Module compatibility benchmarks do not support MaximumVersion. Use PSResourceGet or the managed engine for range measurements.");
        }

        var repositoryName = ResolveCompatibilityRepositoryName(scenario);
        var destinationPath = NormalizeOptional(scenario.ModuleRoot) ?? throw new InvalidOperationException("Save benchmark scenarios require ModuleRoot.");
        var items = engine == ManagedModuleBenchmarkEngine.PSResourceGet
            ? new PSResourceGetClient(_compatibilityPowerShellRunner, _logger).Save(
                new PSResourceSaveOptions(
                    scenario.Name,
                    destinationPath,
                    BuildPSResourceVersionArgument(scenario),
                    repositoryName,
                    scenario.IncludePrerelease,
                    trustRepository: true,
                    skipDependencyCheck: scenario.SkipDependencyCheck,
                    acceptLicense: scenario.AcceptLicense,
                    quiet: true,
                    credential: scenario.Credential),
                TimeSpan.FromMinutes(10))
            : new PowerShellGetClient(_compatibilityPowerShellRunner, _logger).Save(
                new PowerShellGetSaveOptions(
                    scenario.Name,
                    destinationPath,
                    minimumVersion: NormalizeOptional(scenario.MinimumVersion),
                    requiredVersion: NormalizeOptional(scenario.Version),
                    repository: repositoryName,
                    prerelease: scenario.IncludePrerelease,
                    acceptLicense: scenario.AcceptLicense,
                    credential: scenario.Credential),
                TimeSpan.FromMinutes(10));
        var selected = SelectCompatibilityItem(items, scenario.Name);

        return new ManagedModuleBenchmarkRunResult
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = engine.ToString(),
            Iteration = iteration,
            Succeeded = true,
            Status = "Saved",
            ModuleName = scenario.Name,
            Version = selected?.Version ?? scenario.Version,
            ModuleRoot = destinationPath,
            PackageCount = Math.Max(1, items.Count),
            FinalDiskBytes = MeasureDirectoryBytes(destinationPath)
        };
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

    private static ManagedModulePublishRequest CreatePublishRequest(ManagedModuleBenchmarkScenario scenario)
        => new()
        {
            ModulePath = NormalizeOptional(scenario.ModulePath) ?? throw new InvalidOperationException("Publish benchmark scenarios require ModulePath."),
            ManifestPath = NormalizeOptional(scenario.ManifestPath),
            Name = NormalizeOptional(scenario.Name),
            Version = NormalizeOptional(scenario.Version),
            Repository = scenario.Repository,
            OutputDirectory = NormalizeOptional(scenario.PackageOutputDirectory),
            Credential = scenario.Credential,
            SkipDependenciesCheck = scenario.SkipDependencyCheck,
            Force = scenario.Force
        };

    private static ManagedModuleBenchmarkRunResult MapInstall(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        ManagedModuleInstallResult result)
    {
        var evidence = ManagedModuleBenchmarkEvidence.FromInstall(result);
        return new ManagedModuleBenchmarkRunResult
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = ManagedModuleBenchmarkEngine.Managed.ToString(),
            Iteration = iteration,
            Succeeded = true,
            Status = result.Status.ToString(),
            ModuleName = scenario.Name,
            Version = result.Version,
            ModuleRoot = result.ModuleRoot,
            ModulePath = result.ModulePath,
            ServiceElapsed = result.Elapsed,
            PackageBytes = result.Download?.BytesWritten ?? 0,
            ExtractedBytes = result.ExtractedBytes,
            ExtractionElapsed = result.ExtractionElapsed,
            FileCount = result.FileCount,
            DependencyCount = evidence.DependencyCount,
            PackageCount = evidence.PackageCount,
            TotalPackageBytes = evidence.TotalPackageBytes,
            TotalExtractedBytes = evidence.TotalExtractedBytes,
            TotalFileCount = evidence.TotalFileCount,
            TotalExtractionElapsed = evidence.TotalExtractionElapsed,
            RepositoryRequestCount = result.RepositoryRequestCount,
            FinalDiskBytes = evidence.FinalDiskBytes,
            FromCache = result.Download?.FromCache ?? false,
            ValidatedVersion = evidence.ValidatedVersion,
            VersionValidationSucceeded = evidence.VersionValidationSucceeded,
            VersionValidationMessage = evidence.VersionValidationMessage
        };
    }

    private static ManagedModuleBenchmarkRunResult MapPublish(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        ManagedModulePublishResult result)
        => new()
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = ManagedModuleBenchmarkEngine.Managed.ToString(),
            Iteration = iteration,
            Succeeded = result.Published || result.Duplicate,
            Status = result.Duplicate ? "Duplicate" : result.Published ? "Published" : "Skipped",
            ModuleName = result.Name,
            Version = result.Version,
            ModulePath = scenario.ModulePath,
            PackagePath = result.PackagePath,
            PublishSource = result.PublishSource,
            StatusCode = result.StatusCode,
            Published = result.Published,
            Duplicate = result.Duplicate,
            ServiceElapsed = result.Elapsed,
            PackageBytes = result.PackageBytes,
            FileCount = result.FileCount,
            PackageCount = string.IsNullOrWhiteSpace(result.PackagePath) ? 0 : 1,
            TotalPackageBytes = result.PackageBytes,
            TotalFileCount = result.FileCount,
            FinalDiskBytes = MeasureFileBytes(result.PublishSource),
            ErrorMessage = result.Message
        };

    private static long MeasureFileBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long MeasureDirectoryBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return 0;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(static file => new FileInfo(file).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static PSResourceInfo? SelectCompatibilityItem(IReadOnlyList<PSResourceInfo> items, string moduleName)
        => (items ?? Array.Empty<PSResourceInfo>())
            .FirstOrDefault(item => string.Equals(item.Name, moduleName, StringComparison.OrdinalIgnoreCase))
           ?? (items ?? Array.Empty<PSResourceInfo>()).FirstOrDefault();

    private static ManagedModuleVersionInfo? SelectFindVersion(IReadOnlyList<ManagedModuleVersionInfo> versions)
        => (versions ?? Array.Empty<ManagedModuleVersionInfo>())
            .OrderBy(static version => version.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault();

    private static string? ResolveCompatibilityRepositoryName(ManagedModuleBenchmarkScenario scenario)
    {
        var name = NormalizeOptional(scenario.Repository.Name);
        return string.Equals(name, "PSGallery", StringComparison.OrdinalIgnoreCase)
            ? null
            : name;
    }

    private static string? BuildPSResourceVersionArgument(ManagedModuleBenchmarkScenario scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.Version))
            return scenario.Version!.Trim();
        if (!string.IsNullOrWhiteSpace(scenario.MinimumVersion) || !string.IsNullOrWhiteSpace(scenario.MaximumVersion))
            return BuildNuGetRange(scenario.MinimumVersion, scenario.MaximumVersion);

        return null;
    }

    private static string BuildNuGetRange(string? minInclusive, string? maxInclusive)
    {
        var minimum = NormalizeOptional(minInclusive);
        var maximum = NormalizeOptional(maxInclusive);
        if (minimum is null && maximum is null)
            return string.Empty;
        if (minimum is not null && maximum is not null)
            return "[" + minimum + ", " + maximum + "]";
        if (minimum is not null)
            return "[" + minimum + ", ]";

        return "[, " + maximum + "]";
    }

    private static ManagedModuleBenchmarkRunResult MapUpdate(
        ManagedModuleBenchmarkScenario scenario,
        int iteration,
        ManagedModuleUpdateResult result)
    {
        var evidence = ManagedModuleBenchmarkEvidence.FromUpdate(result);
        return new ManagedModuleBenchmarkRunResult
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = ManagedModuleBenchmarkEngine.Managed.ToString(),
            Iteration = iteration,
            Succeeded = true,
            Status = result.Status.ToString(),
            ModuleName = scenario.Name,
            Version = result.TargetVersion,
            PreviousVersion = result.PreviousVersion,
            ModuleRoot = result.ModuleRoot,
            ModulePath = result.ModulePath,
            ServiceElapsed = result.Elapsed,
            PackageBytes = result.InstallResult?.Download?.BytesWritten ?? 0,
            ExtractedBytes = result.InstallResult?.ExtractedBytes ?? 0,
            ExtractionElapsed = result.InstallResult?.ExtractionElapsed,
            FileCount = result.InstallResult?.FileCount ?? 0,
            DependencyCount = evidence.DependencyCount,
            PackageCount = evidence.PackageCount,
            TotalPackageBytes = evidence.TotalPackageBytes,
            TotalExtractedBytes = evidence.TotalExtractedBytes,
            TotalFileCount = evidence.TotalFileCount,
            TotalExtractionElapsed = evidence.TotalExtractionElapsed,
            RepositoryRequestCount = result.RepositoryRequestCount,
            FinalDiskBytes = evidence.FinalDiskBytes,
            FromCache = result.InstallResult?.Download?.FromCache ?? false,
            ValidatedVersion = evidence.ValidatedVersion,
            VersionValidationSucceeded = evidence.VersionValidationSucceeded,
            VersionValidationMessage = evidence.VersionValidationMessage
        };
    }

    private static ManagedModuleBenchmarkRunResult MapCompatibilityResult(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration,
        ModuleDependencyInstallResult result)
        => new()
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = engine.ToString(),
            Iteration = iteration,
            Succeeded = result.Status != ModuleDependencyInstallStatus.Failed,
            Status = result.Status.ToString(),
            ModuleName = scenario.Name,
            Version = result.ResolvedVersion,
            PreviousVersion = result.InstalledVersion,
            ModuleRoot = scenario.ModuleRoot,
            PackageBytes = 0,
            ExtractedBytes = 0,
            FileCount = 0,
            DependencyCount = 0,
            FinalDiskBytes = MeasureDirectoryBytes(scenario.ModuleRoot),
            FromCache = false,
            ErrorMessage = result.Status == ModuleDependencyInstallStatus.Failed ? result.Message : null
        };

    private static ManagedModuleBenchmarkScenario CreateRunScenario(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration,
        bool isolateModuleRoot)
    {
        var moduleRoot = NormalizeOptional(scenario.ModuleRoot);
        if (isolateModuleRoot &&
            scenario.Operation == ManagedModuleBenchmarkOperation.Publish &&
            scenario.Repository.Kind == ManagedModuleRepositoryKind.LocalFolder)
        {
            return CreateIsolatedPublishScenario(scenario, engine, iteration);
        }

        if (!isolateModuleRoot ||
            string.IsNullOrWhiteSpace(moduleRoot) ||
            scenario.Operation is ManagedModuleBenchmarkOperation.Find or ManagedModuleBenchmarkOperation.Publish)
        {
            return scenario;
        }

        var isolatedRoot = Path.Combine(
            Path.GetFullPath(moduleRoot!),
            SanitizePathSegment(engine.ToString()),
            iteration.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (scenario.Operation == ManagedModuleBenchmarkOperation.Update && Directory.Exists(moduleRoot))
            CopyDirectory(moduleRoot!, isolatedRoot);

        return new ManagedModuleBenchmarkScenario
        {
            Id = scenario.Id,
            Operation = scenario.Operation,
            Repository = scenario.Repository,
            Name = scenario.Name,
            Version = scenario.Version,
            MinimumVersion = scenario.MinimumVersion,
            MaximumVersion = scenario.MaximumVersion,
            VersionPolicy = scenario.VersionPolicy,
            IncludePrerelease = scenario.IncludePrerelease,
            Scope = scenario.Scope,
            ShellEdition = scenario.ShellEdition,
            ModuleRoot = isolatedRoot,
            ModulePath = scenario.ModulePath,
            ManifestPath = scenario.ManifestPath,
            PackageCacheDirectory = scenario.PackageCacheDirectory,
            PackageOutputDirectory = scenario.PackageOutputDirectory,
            Credential = scenario.Credential,
            Force = scenario.Force,
            AllowClobber = scenario.AllowClobber,
            AcceptLicense = scenario.AcceptLicense,
            SkipDependencyCheck = scenario.SkipDependencyCheck,
            Iterations = scenario.Iterations
        };
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        if (!Directory.Exists(sourceRoot))
            return;

        Directory.CreateDirectory(destinationRoot);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = FrameworkCompatibility.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = FrameworkCompatibility.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static ManagedModuleBenchmarkRunResult CreateFailedRun(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration,
        TimeSpan elapsed,
        Exception exception)
        => new()
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = engine.ToString(),
            Iteration = iteration,
            Succeeded = false,
            Status = "Failed",
            ModuleName = scenario.Name,
            Elapsed = elapsed,
            ErrorMessage = exception.GetBaseException().Message
        };

    private static void Validate(ManagedModuleBenchmarkRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Scenarios is null || request.Scenarios.Count == 0)
            throw new ArgumentException("At least one benchmark scenario is required.", nameof(request));
        if (request.Engines is null || request.Engines.Count == 0)
            throw new ArgumentException("At least one benchmark engine is required.", nameof(request));

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
        if (scenario.Operation == ManagedModuleBenchmarkOperation.Publish &&
            string.IsNullOrWhiteSpace(scenario.ModulePath))
            throw new ArgumentException("Publish benchmark scenarios require ModulePath.");
        if (scenario.Iterations < 1)
            throw new ArgumentException("Benchmark scenario iterations must be greater than zero.");
    }

    private static string ResolveScenarioId(ManagedModuleBenchmarkScenario scenario)
        => string.IsNullOrWhiteSpace(scenario.Id)
            ? scenario.Operation + ":" + scenario.Name.Trim()
            : scenario.Id.Trim();

    private static IReadOnlyList<ManagedModuleBenchmarkEngine> ResolveEngines(ManagedModuleBenchmarkRequest request)
        => request.Engines
            .Distinct()
            .ToArray();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

}
