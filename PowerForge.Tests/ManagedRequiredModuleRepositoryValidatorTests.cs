using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedRequiredModuleRepositoryValidatorTests
{
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

        validator.Validate(publish, targetRepository, targetCredential: null, plan, buildResult);

        Assert.True(File.Exists(Path.Combine(targetPath, "Company.Core.1.0.0.nupkg")));
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
            () => validator.Validate(publish, targetRepository, targetCredential: null, plan, buildResult));

        Assert.Contains("PSGallery", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private repository", exception.Message, StringComparison.OrdinalIgnoreCase);
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
}
