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
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = approvedModule
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
            Assert.True(analysisService.LastOptions.IncludeFunctionsRecursively);
            Assert.Empty(analysisService.LastOptions.IgnoreFunctions);
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
        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<string>();

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
