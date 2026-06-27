namespace PowerForge;

public sealed partial class ManagedModuleBenchmarkService
{
    private ManagedModuleBenchmarkRunResult RunCompatibilityPublishScenario(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration)
    {
        using var repositoryScope = CreateCompatibilityPublishRepositoryScope(scenario, engine);
        var repositoryName = repositoryScope.RepositoryName;
        var modulePath = NormalizeOptional(scenario.ModulePath) ?? throw new InvalidOperationException("Publish benchmark scenarios require ModulePath.");
        var packageOutputDirectory = NormalizeOptional(scenario.PackageOutputDirectory);
        if (!string.IsNullOrWhiteSpace(packageOutputDirectory))
            Directory.CreateDirectory(packageOutputDirectory!);

        if (engine == ManagedModuleBenchmarkEngine.PSResourceGet)
        {
            new PSResourceGetClient(_compatibilityPowerShellRunner, _logger).Publish(
                new PSResourcePublishOptions(
                    modulePath,
                    isNupkg: false,
                    repository: repositoryName,
                    destinationPath: packageOutputDirectory,
                    skipDependenciesCheck: scenario.SkipDependencyCheck,
                    skipModuleManifestValidate: false,
                    credential: scenario.Credential),
                TimeSpan.FromMinutes(10));
        }
        else
        {
            new PowerShellGetClient(_compatibilityPowerShellRunner, _logger).Publish(
                new PowerShellGetPublishOptions(
                    modulePath,
                    repositoryName,
                    credential: scenario.Credential),
                TimeSpan.FromMinutes(10));
        }

        return new ManagedModuleBenchmarkRunResult
        {
            ScenarioId = ResolveScenarioId(scenario),
            Operation = scenario.Operation,
            Engine = engine.ToString(),
            Iteration = iteration,
            Succeeded = true,
            Status = "Published",
            ModuleName = scenario.Name,
            Version = ResolveCompatibilityPublishVersion(scenario, modulePath),
            ModulePath = modulePath,
            PublishSource = repositoryName,
            Published = true,
            FinalDiskBytes = MeasureDirectoryBytes(ResolveCompatibilityPublishEvidencePath(scenario))
        };
    }

    private CompatibilityPublishRepositoryScope CreateCompatibilityPublishRepositoryScope(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine)
    {
        var repositoryName = ResolveCompatibilityRepositoryName(scenario);
        if (scenario.Repository.Kind != ManagedModuleRepositoryKind.LocalFolder)
            return new CompatibilityPublishRepositoryScope(repositoryName);

        var localSource = ResolveLocalRepositoryPath(scenario.Repository.Source);
        Directory.CreateDirectory(localSource);
        var scopedName = "PFManagedPublish" + Guid.NewGuid().ToString("N").Substring(0, 12);
        if (engine == ManagedModuleBenchmarkEngine.PSResourceGet)
        {
            var client = new PSResourceGetClient(_compatibilityPowerShellRunner, _logger);
            client.EnsureRepositoryRegistered(scopedName, localSource, trusted: true, timeout: TimeSpan.FromMinutes(2));
            return new CompatibilityPublishRepositoryScope(scopedName, () => client.UnregisterRepository(scopedName, TimeSpan.FromMinutes(2)));
        }

        var powerShellGet = new PowerShellGetClient(_compatibilityPowerShellRunner, _logger);
        powerShellGet.EnsureRepositoryRegistered(scopedName, localSource, localSource, trusted: true, credential: scenario.Credential, timeout: TimeSpan.FromMinutes(2));
        return new CompatibilityPublishRepositoryScope(scopedName, () => powerShellGet.UnregisterRepository(scopedName, TimeSpan.FromMinutes(2)));
    }

    private static string? ResolveCompatibilityPublishEvidencePath(ManagedModuleBenchmarkScenario scenario)
    {
        if (scenario.Repository.Kind == ManagedModuleRepositoryKind.LocalFolder)
            return ResolveLocalRepositoryPath(scenario.Repository.Source);

        return NormalizeOptional(scenario.PackageOutputDirectory);
    }

    private static string ResolveLocalRepositoryPath(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return Path.GetFullPath(source.Trim().Trim('"'));
    }

    private static string? ResolveCompatibilityPublishVersion(ManagedModuleBenchmarkScenario scenario, string modulePath)
    {
        if (!string.IsNullOrWhiteSpace(scenario.Version))
            return scenario.Version!.Trim();

        var manifestPath = ResolveCompatibilityPublishManifestPath(scenario, modulePath);
        if (manifestPath is null)
            return null;

        var version = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion");
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Prerelease").FirstOrDefault();
        return string.IsNullOrWhiteSpace(prerelease)
            ? version
            : version + "-" + prerelease;
    }

    private static string? ResolveCompatibilityPublishManifestPath(ManagedModuleBenchmarkScenario scenario, string modulePath)
    {
        var explicitManifest = NormalizeOptional(scenario.ManifestPath);
        if (!string.IsNullOrWhiteSpace(explicitManifest) && File.Exists(explicitManifest))
            return explicitManifest;

        var expected = Path.Combine(modulePath, Path.GetFileName(modulePath) + ".psd1");
        if (File.Exists(expected))
            return expected;

        var manifests = Directory.Exists(modulePath)
            ? Directory.EnumerateFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        return manifests.Length == 1 ? manifests[0] : null;
    }

    private static ManagedModuleBenchmarkScenario CreateIsolatedPublishScenario(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine,
        int iteration)
    {
        var isolatedRepository = Path.Combine(
            ResolveLocalRepositoryPath(scenario.Repository.Source),
            SanitizePathSegment(engine.ToString()),
            iteration.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var packageOutput = NormalizeOptional(scenario.PackageOutputDirectory);
        var isolatedPackageOutput = string.IsNullOrWhiteSpace(packageOutput)
            ? null
            : Path.Combine(
                Path.GetFullPath(packageOutput!),
                SanitizePathSegment(engine.ToString()),
                iteration.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new ManagedModuleBenchmarkScenario
        {
            Id = scenario.Id,
            Operation = scenario.Operation,
            Repository = new ManagedModuleRepository(scenario.Repository.Name, isolatedRepository, ManagedModuleRepositoryKind.LocalFolder, scenario.Repository.Trusted),
            Name = scenario.Name,
            Version = scenario.Version,
            MinimumVersion = scenario.MinimumVersion,
            MaximumVersion = scenario.MaximumVersion,
            VersionPolicy = scenario.VersionPolicy,
            IncludePrerelease = scenario.IncludePrerelease,
            Scope = scenario.Scope,
            ShellEdition = scenario.ShellEdition,
            ModuleRoot = scenario.ModuleRoot,
            ModulePath = scenario.ModulePath,
            ManifestPath = scenario.ManifestPath,
            PackageCacheDirectory = scenario.PackageCacheDirectory,
            PackageOutputDirectory = isolatedPackageOutput,
            Credential = scenario.Credential,
            Force = scenario.Force,
            AllowClobber = scenario.AllowClobber,
            AcceptLicense = scenario.AcceptLicense,
            SkipDependencyCheck = scenario.SkipDependencyCheck,
            Iterations = scenario.Iterations
        };
    }

    private sealed class CompatibilityPublishRepositoryScope : IDisposable
    {
        private readonly Action? _dispose;

        public CompatibilityPublishRepositoryScope(string? repositoryName, Action? dispose = null)
        {
            RepositoryName = repositoryName;
            _dispose = dispose;
        }

        public string? RepositoryName { get; }

        public void Dispose()
        {
            if (_dispose is null)
                return;

            try
            {
                _dispose();
            }
            catch
            {
                // Compatibility cleanup is best-effort; publish result evidence should not be overwritten by cleanup noise.
            }
        }
    }
}
