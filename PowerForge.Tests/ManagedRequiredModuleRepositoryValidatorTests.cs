using System.Net;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedRequiredModuleRepositoryValidatorTests
{
    [Fact]
    public void Validate_mirrors_dependency_graph_to_write_endpoint_and_verifies_through_read_endpoint()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Core.1.0.0.nupkg"), "Company.Core", "1.0.0",
            dependencies: new[] { new TestDependency("Company.Dependency", "[1.0.0]", targetFramework: null) });
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Dependency.1.0.0.nupkg"), "Company.Dependency", "1.0.0");
        using var handler = new AggregateReadHandler(destination.Path);
        using var client = new HttpClient(handler);
        var validator = new ManagedRequiredModuleRepositoryValidator(new NullLogger(), new ManagedModuleRepositoryClient(new NullLogger(), client));
        var publish = new PublishConfiguration
        {
            PublishRequiredModules = true,
            RequiredModuleSourceRepository = "Source",
            RequiredModuleSourceRepositoryUri = source.Path
        };
        var plan = CreatePlan(new RequiredModuleReference("Company.Core", requiredVersion: "1.0.0"));
        var buildResult = new ModuleBuildResult(source.Path, Path.Combine(source.Path, "missing.psd1"),
            new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

        validator.Validate(publish, new ManagedModuleRepository("ConsumerFeed", "https://feed.test/v3/index.json"),
            targetCredential: null, targetPublishCredential: null, plan, buildResult,
            publishRepository: new ManagedModuleRepository("UploadFeed", destination.Path));

        Assert.True(LocalPackageExists(destination.Path, "Company.Core", "1.0.0"));
        Assert.True(LocalPackageExists(destination.Path, "Company.Dependency", "1.0.0"));
        Assert.Contains("/flat/company.core/index.json", handler.Paths);
        Assert.Contains("/flat/company.dependency/index.json", handler.Paths);
    }

    [Fact]
    public void Validate_rejects_mirroring_when_only_write_endpoint_is_psgallery()
    {
        using var readFeed = new TemporaryDirectory();
        var validator = new ManagedRequiredModuleRepositoryValidator(new NullLogger());
        var plan = CreatePlan(new RequiredModuleReference("Company.Core", requiredVersion: "1.0.0"));
        var buildResult = new ModuleBuildResult(readFeed.Path, Path.Combine(readFeed.Path, "missing.psd1"),
            new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

        var error = Assert.Throws<InvalidOperationException>(() => validator.Validate(
            new PublishConfiguration { PublishRequiredModules = true },
            new ManagedModuleRepository("ConsumerFeed", readFeed.Path),
            targetCredential: null, targetPublishCredential: null, plan, buildResult,
            publishRepository: new ManagedModuleRepository("UploadFeed", "https://www.powershellgallery.com/api/v2")));

        Assert.Contains("Refusing to mirror dependencies to PSGallery", error.Message);
    }

    [Fact]
    public void Validate_treats_missing_local_target_repository_as_empty_when_mirroring()
    {
        using var source = new TemporaryDirectory();
        using var targetContainer = new TemporaryDirectory();
        var targetPath = Path.Combine(targetContainer.Path, "MissingTargetFeed");
        TestPackageFactory.Create(
            Path.Combine(source.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0");
        var validator = new ManagedRequiredModuleRepositoryValidator(new NullLogger());
        var publish = new PublishConfiguration
        {
            PublishRequiredModules = true,
            RequiredModuleSourceRepository = "Source",
            RequiredModuleSourceRepositoryUri = source.Path
        };
        var targetRepository = new ManagedModuleRepository("Private", targetPath);
        var plan = CreatePlan(new RequiredModuleReference("Company.Core", requiredVersion: "1.0.0"));
        var buildResult = new ModuleBuildResult(
            stagingPath: targetContainer.Path,
            manifestPath: Path.Combine(targetContainer.Path, "missing.psd1"),
            exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        var remoteSideEffects = 0;

        validator.Validate(
            publish,
            targetRepository,
            targetCredential: null,
            targetPublishCredential: null,
            plan,
            buildResult,
            remoteSideEffectObserved: () => remoteSideEffects++);

        Assert.True(LocalPackageExists(targetPath, "Company.Core", "1.0.0"));
        Assert.Equal(1, remoteSideEffects);
    }

    [Fact]
    public void Validate_mirrors_merged_package_dependencies_without_raw_manifest_duplicates()
    {
        using var source = new TemporaryDirectory();
        using var target = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(source.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Dependency", "[1.0.0]", targetFramework: null) },
            files: new Dictionary<string, string>
            {
                ["Company.Core.psd1"] = "@{ ModuleVersion = '1.0.0'; RequiredModules = @('Company.Dependency') }"
            });
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Dependency.1.0.0.nupkg"), "Company.Dependency", "1.0.0");
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Dependency.2.0.0.nupkg"), "Company.Dependency", "2.0.0");
        var validator = new ManagedRequiredModuleRepositoryValidator(new NullLogger());
        var publish = new PublishConfiguration
        {
            PublishRequiredModules = true,
            RequiredModuleSourceRepository = "Source",
            RequiredModuleSourceRepositoryUri = source.Path
        };
        var plan = CreatePlan(new RequiredModuleReference("Company.Core", requiredVersion: "1.0.0"));
        var buildResult = new ModuleBuildResult(
            stagingPath: target.Path,
            manifestPath: Path.Combine(target.Path, "missing.psd1"),
            exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        var remoteSideEffects = 0;

        validator.Validate(
            publish,
            new ManagedModuleRepository("Private", target.Path),
            targetCredential: null,
            targetPublishCredential: null,
            plan,
            buildResult,
            remoteSideEffectObserved: () => remoteSideEffects++);

        Assert.True(LocalPackageExists(target.Path, "Company.Core", "1.0.0"));
        Assert.True(LocalPackageExists(target.Path, "Company.Dependency", "1.0.0"));
        Assert.False(LocalPackageExists(target.Path, "Company.Dependency", "2.0.0"));
        Assert.Equal(2, remoteSideEffects);
    }

    [Fact]
    public void IsSelectableDependencyVersion_rejects_unlisted_versions_for_ranges_only()
    {
        var unlisted = new ManagedModuleVersionInfo
        {
            Name = "Company.Dependency",
            Version = "1.2.0",
            Listed = false
        };

        Assert.False(ManagedRequiredModuleRepositoryValidator.IsSelectableDependencyVersion(
            unlisted,
            ManagedModuleVersionRange.Parse(">=1.0.0")));
        Assert.True(ManagedRequiredModuleRepositoryValidator.IsSelectableDependencyVersion(
            unlisted,
            ManagedModuleVersionRange.Parse("[1.2.0]")));
    }

    [Fact]
    public void Validate_rejects_publish_required_modules_when_target_source_is_psgallery_alias()
    {
        using var source = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(source.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0");
        var validator = new ManagedRequiredModuleRepositoryValidator(new NullLogger());
        var publish = new PublishConfiguration
        {
            PublishRequiredModules = true,
            RequiredModuleSourceRepository = "Source",
            RequiredModuleSourceRepositoryUri = source.Path
        };
        var targetRepository = new ManagedModuleRepository("CompanyAlias", "https://www.powershellgallery.com/api/v3/index.json");
        var plan = CreatePlan(new RequiredModuleReference("Company.Core", requiredVersion: "1.0.0"));
        var buildResult = new ModuleBuildResult(
            stagingPath: source.Path,
            manifestPath: Path.Combine(source.Path, "missing.psd1"),
            exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate(publish, targetRepository, targetCredential: null, targetPublishCredential: null, plan, buildResult));

        Assert.Contains("PSGallery", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private repository", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AggregateReadHandler(string destination) : HttpMessageHandler
    {
        internal List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/v3/index.json")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\"https://feed.test/flat/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}")
                });

            var packageId = path.Split('/')[2];
            var exists = LocalPackageExists(destination, packageId, "1.0.0");
            return Task.FromResult(new HttpResponseMessage(exists ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(exists ? "{\"versions\":[\"1.0.0\"]}" : "{}")
            });
        }
    }

    private static ModulePipelinePlan CreatePlan(params RequiredModuleReference[] requiredModules)
        => new(
            moduleName: "TestModule",
            projectRoot: @"C:\repo\TestModule",
            expectedVersion: "1.0.0",
            resolvedVersion: "1.0.0",
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "TestModule",
                SourcePath = @"C:\repo\TestModule",
                Version = "1.0.0"
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: Array.Empty<string>(),
            requiredModules: requiredModules,
            externalModuleDependencies: Array.Empty<string>(),
            requiredModulesForPackaging: Array.Empty<RequiredModuleReference>(),
            information: null,
            documentation: null,
            delivery: null,
            documentationBuild: null,
            compatibilitySettings: null,
            fileConsistencySettings: null,
            validationSettings: null,
            formatting: null,
            importModules: null,
            placeHolders: Array.Empty<PlaceHolderReplacement>(),
            placeHolderOption: null,
            commandModuleDependencies: new Dictionary<string, string[]>(),
            testsAfterMerge: Array.Empty<TestConfiguration>(),
            actions: Array.Empty<ConfigurationActionSegment>(),
            mergeModule: false,
            mergeMissing: false,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: Array.Empty<string>(),
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: Array.Empty<ConfigurationPublishSegment>(),
            artefacts: Array.Empty<ConfigurationArtefactSegment>(),
            installEnabled: false,
            installStrategy: InstallationStrategy.AutoRevision,
            installKeepVersions: 3,
            installRoots: Array.Empty<string>(),
            installLegacyFlatHandling: LegacyFlatModuleHandling.Warn,
            installPreserveVersions: Array.Empty<string>(),
            installMissingModules: false,
            installMissingModulesForce: false,
            installMissingModulesPrerelease: false,
            installMissingModulesRepository: null,
            installMissingModulesCredential: null,
            stagingWasGenerated: true,
            deleteGeneratedStagingAfterRun: true);

    private static bool LocalPackageExists(string repositoryPath, string packageId, string version)
    {
        if (!Directory.Exists(repositoryPath))
            return false;

        var expectedFileName = $"{packageId}.{version}.nupkg";
        return Directory.EnumerateFiles(repositoryPath, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Any(path => string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase));
    }
}
