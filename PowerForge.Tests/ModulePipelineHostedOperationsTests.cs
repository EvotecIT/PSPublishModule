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

        var services = ModulePipelineRunnerDefaults.Create(new NullLogger(), powerShellRunner, null, null, null, null, null);

        Assert.Same(powerShellRunner, services.PowerShellRunner);
        Assert.IsType<PowerShellModuleDependencyMetadataProvider>(services.ModuleDependencyMetadataProvider);
        Assert.IsType<PowerShellModulePipelineHostedOperations>(services.HostedOperations);
        Assert.IsType<PowerShellMissingFunctionAnalysisService>(services.MissingFunctionAnalysisService);
        Assert.IsType<PowerShellScriptFunctionExportDetector>(services.ScriptFunctionExportDetector);
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

    [Fact]
    public void RunTestsAfterMerge_IncludesFailedTestNamesAndMessages()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextTestSuiteResult = new ModuleTestSuiteResult(
                    projectPath: root.FullName,
                    testPath: testsPath,
                    moduleName: moduleName,
                    moduleVersion: "1.0.0",
                    manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                    requiredModules: Array.Empty<RequiredModuleReference>(),
                    dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                    moduleImported: true,
                    exportedFunctionCount: null,
                    exportedCmdletCount: null,
                    exportedAliasCount: null,
                    pesterVersion: "5.7.1",
                    totalCount: 3,
                    passedCount: 2,
                    failedCount: 1,
                    skippedCount: 0,
                    duration: null,
                    coveragePercent: null,
                    failureAnalysis: new ModuleTestFailureAnalysis
                    {
                        Source = "PesterResults",
                        Timestamp = DateTime.Now,
                        TotalCount = 3,
                        PassedCount = 2,
                        FailedCount = 1,
                        FailedTests = new[]
                        {
                            new ModuleTestFailureInfo
                            {
                                Name = "Broken.Test",
                                ErrorMessage = "boom"
                            }
                        }
                    },
                    exitCode: 1,
                    stdOut: string.Empty,
                    stdErr: string.Empty,
                    resultsXmlPath: null)
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRunTestsAfterMerge(runner, plan, buildResult, new TestConfiguration { TestsPath = testsPath }));

            var actual = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("TestsAfterMerge failed (1 failed).", actual.Message, StringComparison.Ordinal);
            Assert.Contains("Broken.Test", actual.Message, StringComparison.Ordinal);
            Assert.Contains("boom", actual.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunTestsAfterMerge_FallsBackToCapturedErrorOutput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextTestSuiteResult = new ModuleTestSuiteResult(
                    projectPath: root.FullName,
                    testPath: testsPath,
                    moduleName: moduleName,
                    moduleVersion: "1.0.0",
                    manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                    requiredModules: Array.Empty<RequiredModuleReference>(),
                    dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                    moduleImported: true,
                    exportedFunctionCount: null,
                    exportedCmdletCount: null,
                    exportedAliasCount: null,
                    pesterVersion: "5.7.1",
                    totalCount: 3,
                    passedCount: 2,
                    failedCount: 1,
                    skippedCount: 0,
                    duration: null,
                    coveragePercent: null,
                    failureAnalysis: null,
                    exitCode: 1,
                    stdOut: "ignored output",
                    stdErr: "\r\nfirst error line\r\nsecond error line",
                    resultsXmlPath: null)
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRunTestsAfterMerge(runner, plan, buildResult, new TestConfiguration { TestsPath = testsPath }));

            var actual = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("stderr: first error line", actual.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RunTestsAfterMerge_OmittedCount_IgnoresBlankFailuresFilteredOutOfOutput()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var testsPath = Directory.CreateDirectory(Path.Combine(root.FullName, "Tests")).FullName;

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var hostedOperations = new FakeHostedOperations
            {
                NextTestSuiteResult = new ModuleTestSuiteResult(
                    projectPath: root.FullName,
                    testPath: testsPath,
                    moduleName: moduleName,
                    moduleVersion: "1.0.0",
                    manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                    requiredModules: Array.Empty<RequiredModuleReference>(),
                    dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
                    moduleImported: true,
                    exportedFunctionCount: null,
                    exportedCmdletCount: null,
                    exportedAliasCount: null,
                    pesterVersion: "5.7.1",
                    totalCount: 7,
                    passedCount: 0,
                    failedCount: 7,
                    skippedCount: 0,
                    duration: null,
                    coveragePercent: null,
                    failureAnalysis: new ModuleTestFailureAnalysis
                    {
                        Source = "PesterResults",
                        Timestamp = DateTime.Now,
                        TotalCount = 7,
                        PassedCount = 0,
                        FailedCount = 7,
                        FailedTests = new[]
                        {
                            new ModuleTestFailureInfo { Name = "Broken.Test1", ErrorMessage = "boom1" },
                            new ModuleTestFailureInfo { Name = "Broken.Test2", ErrorMessage = "boom2" },
                            new ModuleTestFailureInfo { Name = "Broken.Test3", ErrorMessage = "boom3" },
                            new ModuleTestFailureInfo { Name = "Broken.Test4", ErrorMessage = "boom4" },
                            new ModuleTestFailureInfo { Name = "Broken.Test5", ErrorMessage = "boom5" },
                            new ModuleTestFailureInfo(),
                            new ModuleTestFailureInfo()
                        }
                    },
                    exitCode: 1,
                    stdOut: string.Empty,
                    stdErr: string.Empty,
                    resultsXmlPath: null)
            };

            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeRunTestsAfterMerge(runner, plan, buildResult, new TestConfiguration { TestsPath = testsPath }));

            var actual = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.DoesNotContain("Additional failed tests omitted", actual.Message, StringComparison.Ordinal);
            Assert.Contains("Broken.Test5", actual.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModuleDependencyInstallResult[] InvokeEnsureBuildDependenciesInstalledIfNeeded(ModulePipelineRunner runner, ModulePipelinePlan plan)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("EnsureBuildDependenciesInstalledIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "EnsureBuildDependenciesInstalledIfNeeded method signature may have changed.");
        return (ModuleDependencyInstallResult[])method!.Invoke(runner, new object?[] { plan })!;
    }

    private static void InvokeRunTestsAfterMerge(ModulePipelineRunner runner, ModulePipelinePlan plan, ModuleBuildResult buildResult, TestConfiguration configuration)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("RunTestsAfterMerge", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "RunTestsAfterMerge method signature may have changed.");
        method!.Invoke(runner, new object?[] { plan, buildResult, configuration });
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
        public ModuleTestSuiteResult? NextTestSuiteResult { get; set; }

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
            => NextTestSuiteResult ?? throw new InvalidOperationException("Not used in this test.");

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
