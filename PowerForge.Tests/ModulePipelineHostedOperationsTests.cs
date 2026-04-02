using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineHostedOperationsTests
{
    [Fact]
    public void DefaultRunnerServices_ReuseProvidedPowerShellRunner()
    {
        var powerShellRunner = new RecordingPowerShellRunner(_ => new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh"));

        var services = ModulePipelineRunnerDefaults.Create(new NullLogger(), powerShellRunner, null, null);

        Assert.Same(powerShellRunner, services.PowerShellRunner);
        Assert.IsType<PowerShellModuleDependencyMetadataProvider>(services.ModuleDependencyMetadataProvider);
        Assert.IsType<PowerShellModulePipelineHostedOperations>(services.HostedOperations);
    }

    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_UsesInjectedHostedOperations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            InstallMissingModules = true
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Pester",
                            RequiredVersion = "5.6.1"
                        }
                    }
                }
            };

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var result = InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, plan);

            Assert.Single(result);
            Assert.Equal(1, hostedOperations.DependencyInstallCalls);
            Assert.Equal("Pester", hostedOperations.LastDependencies.Single().Name);
            Assert.Null(hostedOperations.LastRepository);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateModuleImports_UsesInjectedPowerShellRunner()
    {
        var requests = new List<PowerShellRunRequest>();
        var runner = new RecordingPowerShellRunner(request =>
        {
            requests.Add(request);
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
        });

        var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
        operations.ValidateModuleImports(
            manifestPath: @"C:\Temp\TestModule\TestModule.psd1",
            modules: Array.Empty<ImportModuleEntry>(),
            importRequired: true,
            importSelf: false,
            verbose: true,
            targets: new[]
            {
                new ModuleImportValidationTarget("pwsh", "Core", preferPwsh: true)
            });

        var request = Assert.Single(requests);
        Assert.Equal(PowerShellInvocationMode.File, request.InvocationMode);
        Assert.True(request.PreferPwsh);
        Assert.EndsWith(".ps1", request.ScriptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignModuleOutput_UsesInjectedPowerShellRunner()
    {
        var requests = new List<PowerShellRunRequest>();
        var summary = new ModuleSigningResult
        {
            TotalMatched = 1,
            TotalAfterExclude = 1,
            Attempted = 1,
            SignedNew = 1,
            Resigned = 0,
            Failed = 0
        };
        var stdout = "PFSIGN::SUMMARY::" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(summary)));
        var runner = new RecordingPowerShellRunner(request =>
        {
            requests.Add(request);
            return new PowerShellRunResult(0, stdout, string.Empty, "pwsh");
        });

        var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
        var result = operations.SignModuleOutput(
            moduleName: "TestModule",
            rootPath: @"C:\Temp\TestModule",
            includePatterns: new[] { "*.psm1" },
            excludeSubstrings: Array.Empty<string>(),
            signing: new SigningOptionsConfiguration());

        var request = Assert.Single(requests);
        Assert.Equal(PowerShellInvocationMode.File, request.InvocationMode);
        Assert.True(request.PreferPwsh);
        Assert.Equal(1, result.SignedNew);
    }

    private static ModuleDependencyInstallResult[] InvokeEnsureBuildDependenciesInstalledIfNeeded(ModulePipelineRunner runner, ModulePipelinePlan plan)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("EnsureBuildDependenciesInstalledIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "EnsureBuildDependenciesInstalledIfNeeded method signature may have changed.");
        return (ModuleDependencyInstallResult[])method!.Invoke(runner, new object?[] { plan })!;
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }

    private sealed class FakeMetadataProvider : IModuleDependencyMetadataProvider
    {
        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => (names ?? Array.Empty<string>())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToDictionary(
                    static name => name,
                    static name => new InstalledModuleMetadata(name, null, null, null),
                    StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<string>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeHostedOperations : IModulePipelineHostedOperations
    {
        public int DependencyInstallCalls { get; private set; }
        public IReadOnlyList<ModuleDependency> LastDependencies { get; private set; } = Array.Empty<ModuleDependency>();
        public string? LastRepository { get; private set; }

        public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
            ModuleDependency[] dependencies,
            ModuleSkipConfiguration? skipModules,
            bool force,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
        {
            DependencyInstallCalls++;
            LastDependencies = dependencies ?? Array.Empty<ModuleDependency>();
            LastRepository = repository;
            var first = LastDependencies.FirstOrDefault() ?? new ModuleDependency("Unknown", null, null, null);

            return new[]
            {
                new ModuleDependencyInstallResult(
                    name: first.Name,
                    installedVersion: first.RequiredVersion,
                    resolvedVersion: first.RequiredVersion,
                    requestedVersion: first.RequiredVersion,
                    status: ModuleDependencyInstallStatus.Satisfied,
                    installer: "fake",
                    message: "ok")
            };
        }

        public DocumentationBuildResult BuildDocumentation(
            string moduleName,
            string stagingPath,
            string moduleManifestPath,
            DocumentationConfiguration documentation,
            BuildDocumentationConfiguration buildDocumentation,
            IModulePipelineProgressReporter progress,
            ModulePipelineStep? extractStep,
            ModulePipelineStep? writeStep,
            ModulePipelineStep? externalHelpStep)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleValidationReport ValidateModule(ModuleValidationSpec spec)
            => throw new InvalidOperationException("Not used in this test.");

        public void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec)
            => throw new InvalidOperationException("Not used in this test.");

        public ModulePublishResult PublishModule(
            PublishConfiguration publish,
            ModulePipelinePlan plan,
            ModuleBuildResult buildResult,
            IReadOnlyList<ArtefactBuildResult> artefactResults,
            bool includeScriptFolders)
            => throw new InvalidOperationException("Not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
            => throw new InvalidOperationException("Not used in this test.");
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used in this test.");
    }

    private sealed class RecordingPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public RecordingPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _run(request);
    }
}
