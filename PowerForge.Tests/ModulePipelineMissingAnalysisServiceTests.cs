using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineMissingAnalysisServiceTests
{
    [Fact]
    public void AnalyzeMissingFunctions_UsesInjectedMissingFunctionAnalysisService()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string approvedModule = "Approved.Module";
            const string runtimeModule = "Runtime.Module";

            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var expected = new MissingFunctionAnalysisResult(
                summary: new[] { new MissingCommandReference("Get-Thing", approvedModule, "Function", isAlias: false, isPrivate: false, error: string.Empty) },
                summaryFiltered: Array.Empty<MissingCommandReference>(),
                functions: new[] { "function Get-Thing { 'ok' }" },
                functionsTopLevelOnly: new[] { "function Get-Thing { 'ok' }" });
            var analysisService = new RecordingMissingFunctionAnalysisService(expected);
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeDependencyMetadataProvider(new InstalledModuleMetadata(
                    runtimeModule,
                    "1.0.0",
                    "11111111-1111-1111-1111-111111111111",
                    root.FullName)),
                new FakeHostedOperations(),
                new FakeManifestMutator(),
                analysisService);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = approvedModule
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = runtimeModule,
                            RequiredVersion = "1.0.0"
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var method = typeof(ModulePipelineRunner).GetMethod("AnalyzeMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(method is not null, "AnalyzeMissingFunctions method signature may have changed.");

            var result = (MissingFunctionAnalysisResult?)method!.Invoke(runner, new object?[] { null, "Get-Thing", plan });

            Assert.Equal(1, analysisService.Calls);
            Assert.Same(expected, result);
            Assert.Null(analysisService.LastFilePath);
            Assert.Equal("Get-Thing", analysisService.LastCode);
            Assert.NotNull(analysisService.LastOptions);
            Assert.Equal(new[] { approvedModule }, analysisService.LastOptions!.ApprovedModules);
            Assert.Empty(analysisService.LastOptions.KnownFunctions);
            Assert.True(analysisService.LastOptions.IncludeFunctionsRecursively);
            Assert.Empty(analysisService.LastOptions.IgnoreFunctions);
            Assert.True(analysisService.LastOptions.RequireApprovedModuleSources);
            var runtimeSource = Assert.Single(analysisService.LastOptions.RuntimeModuleSources);
            Assert.Equal(runtimeModule, runtimeSource.Name);
            Assert.Equal(root.FullName, runtimeSource.ModuleBasePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AnalyzeMissingFunctions_PassesModuleLevelConsumerFunctionsButNotPrivateScopedFunctionsAsKnown()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var analysisService = new RecordingMissingFunctionAnalysisService(
                new MissingFunctionAnalysisResult(
                    Array.Empty<MissingCommandReference>(),
                    Array.Empty<MissingCommandReference>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()));
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeDependencyMetadataProvider(),
                new FakeHostedOperations(),
                new FakeManifestMutator(),
                analysisService);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };
            const string code = "function Get-ConsumerHelper { 'ok' }\nfunction private:Get-PrivateHelper { 'private' }";
            var method = typeof(ModulePipelineRunner).GetMethod("AnalyzeMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(runner, new object?[] { null, code, runner.Plan(spec) });

            Assert.NotNull(analysisService.LastOptions);
            Assert.Contains("Get-ConsumerHelper", analysisService.LastOptions!.KnownFunctions, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Get-PrivateHelper", analysisService.LastOptions.KnownFunctions, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateMissingFunctions_IgnoresCommandsResolvedFromTheConsumerModuleItself()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var report = new MissingFunctionAnalysisResult(
                summary: new[]
                {
                    new MissingCommandReference("Get-ConsumerHelper", moduleName, "Function", false, false, string.Empty)
                },
                summaryFiltered: Array.Empty<MissingCommandReference>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());
            var runner = new ModulePipelineRunner(new NullLogger());
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };
            var method = typeof(ModulePipelineRunner).GetMethod("ValidateMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(runner, new object?[] { report, runner.Plan(spec), null });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("Out-Null")]
    [InlineData("Set-Alias")]
    public void ValidateMissingFunctions_IgnoresCommandsProvidedByTheDefaultPowerShellSession(string commandName)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var report = new MissingFunctionAnalysisResult(
                summary: new[] { new MissingCommandReference(commandName, string.Empty, string.Empty, false, false, "not resolved in build host") },
                summaryFiltered: Array.Empty<MissingCommandReference>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());
            var runner = new ModulePipelineRunner(new NullLogger());
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };
            var method = typeof(ModulePipelineRunner).GetMethod("ValidateMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(runner, new object?[] { report, runner.Plan(spec), null });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateMissingFunctions_FailsByDefault_WhenModuleIsNotRequiredOrSkipped()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string missingModule = "Missing.Dependency";

            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var report = new MissingFunctionAnalysisResult(
                summary: new[] { new MissingCommandReference("Get-MissingThing", missingModule, "Function", isAlias: false, isPrivate: false, error: string.Empty) },
                summaryFiltered: Array.Empty<MissingCommandReference>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeDependencyMetadataProvider(),
                new FakeHostedOperations(),
                new FakeManifestMutator(),
                new RecordingMissingFunctionAnalysisService(report));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var plan = runner.Plan(spec);
            var method = typeof(ModulePipelineRunner).GetMethod("ValidateMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(method is not null, "ValidateMissingFunctions method signature may have changed.");

            var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(runner, new object?[] { report, plan, null }));
            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("Missing commands detected during merge", inner.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(missingModule, inner.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    internal static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
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

    internal sealed class RecordingMissingFunctionAnalysisService : IMissingFunctionAnalysisService
    {
        private readonly MissingFunctionAnalysisResult _result;

        public RecordingMissingFunctionAnalysisService(MissingFunctionAnalysisResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }
        public string? LastFilePath { get; private set; }
        public string? LastCode { get; private set; }
        public MissingFunctionsOptions? LastOptions { get; private set; }

        public MissingFunctionAnalysisResult Analyze(string? filePath, string? code, MissingFunctionsOptions options)
        {
            Calls++;
            LastFilePath = filePath;
            LastCode = code;
            LastOptions = options;
            return _result;
        }
    }

    internal sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used in this test.");
    }

    internal sealed class FakeDependencyMetadataProvider : IModuleDependencyMetadataProvider
    {
        private readonly InstalledModuleMetadata? _installed;

        internal FakeDependencyMetadataProvider(InstalledModuleMetadata? installed = null)
        {
            _installed = installed;
        }

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
        {
            var output = new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);
            if (_installed is not null && names.Contains(_installed.Name, StringComparer.OrdinalIgnoreCase))
                output[_installed.Name] = _installed;
            return output;
        }

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class FakeHostedOperations : IModulePipelineHostedOperations
    {
        public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
            ModuleDependency[] dependencies,
            ModuleSkipConfiguration? skipModules,
            bool force,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => Array.Empty<ModuleDependencyInstallResult>();

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
            bool includeScriptFolders,
            Action? remotePublishAttempted,
            Action? remoteSideEffectObserved,
            Action<string, string>? finalizeRepositoryModule,
            IGitHubReleaseProgressReporter? gitHubProgress)
            => throw new InvalidOperationException("Not used in this test.");

        public void ValidateModuleImports(
            string manifestPath,
            ImportModuleEntry[] modules,
            bool importRequired,
            bool importSelf,
            bool verbose,
            ModuleImportValidationTarget[] targets)
            => throw new InvalidOperationException("Not used in this test.");

        public ModulePipelineActionResult RunAction(
            ModulePipelineActionConfiguration action,
            ModulePipelineActionContext context,
            string contextPath,
            string projectRoot)
            => throw new InvalidOperationException("Not used in this test.");

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] packageFilePaths,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
            => throw new InvalidOperationException("Not used in this test.");
    }

    internal sealed class FakeManifestMutator : IModuleManifestMutator
    {
        public List<ManifestExportWrite> ManifestExportWrites { get; } = new();

        public bool TrySetTopLevelModuleVersion(string filePath, string newVersion) => true;
        public bool TrySetTopLevelString(string filePath, string key, string newValue) => true;
        public bool TrySetTopLevelStringArray(string filePath, string key, string[] values) => true;
        public bool TrySetPsDataString(string filePath, string key, string value) => true;
        public bool TrySetPsDataStringArray(string filePath, string key, string[] values) => true;
        public bool TrySetPsDataBool(string filePath, string key, bool value) => true;
        public bool TryRemoveTopLevelKey(string filePath, string key) => true;
        public bool TryRemovePsDataKey(string filePath, string key) => true;
        public bool TrySetRequiredModules(string filePath, RequiredModuleReference[] modules) => true;
        public bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value) => true;
        public bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values) => true;
        public bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value) => true;
        public bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> values) => true;

        public bool TrySetManifestExports(string filePath, string[]? functions, string[]? cmdlets, string[]? aliases)
        {
            ManifestExportWrites.Add(new ManifestExportWrite(
                filePath,
                functions ?? Array.Empty<string>(),
                cmdlets ?? Array.Empty<string>(),
                aliases ?? Array.Empty<string>()));
            return true;
        }

        public bool TrySetRepository(string filePath, string? branch, string[]? paths) => true;
    }

    internal sealed record ManifestExportWrite(string FilePath, string[] Functions, string[] Cmdlets, string[] Aliases);
}
