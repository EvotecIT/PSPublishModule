using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void RunImportModules_UsesInjectedHostedOperations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ThrowingPowerShellRunner(),
                new FakeMetadataProvider(),
                hostedOperations);

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
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Pester",
                            RequiredVersion = "5.6.1"
                        }
                    },
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            RequiredModules = true,
                            Self = true,
                            Verbose = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(
                stagingPath: root.FullName,
                manifestPath: Path.Combine(root.FullName, $"{moduleName}.psd1"),
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            InvokeRunImportModules(runner, plan, buildResult);

            Assert.Equal(1, hostedOperations.ImportValidationCalls);
            Assert.True(hostedOperations.LastImportSelf);
            Assert.True(hostedOperations.LastImportRequired);
            Assert.True(hostedOperations.LastImportVerbose);
            Assert.Equal("Pester", hostedOperations.LastImportModules.Single().Name);
            Assert.NotEmpty(hostedOperations.LastTargets);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SignBuiltModuleOutput_UsesInjectedHostedOperations()
    {
        var hostedOperations = new FakeHostedOperations
        {
            NextSigningResult = new ModuleSigningResult
            {
                SignedNew = 2,
                Attempted = 2
            }
        };

        var runner = new ModulePipelineRunner(
            new NullLogger(),
            new ThrowingPowerShellRunner(),
            new FakeMetadataProvider(),
            hostedOperations);

        var result = InvokeSignBuiltModuleOutput(
            runner,
            "TestModule",
            Path.GetTempPath(),
            new SigningOptionsConfiguration
            {
                CertificateThumbprint = "ABC123",
                IncludeInternals = false
            });

        Assert.Same(hostedOperations.NextSigningResult, result);
        Assert.Equal(1, hostedOperations.SignCalls);
        Assert.Contains("*.ps1", hostedOperations.LastIncludePatterns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Modules", hostedOperations.LastExcludePatterns, StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot, $"{moduleName}.psd1"),
            $"@{{ ModuleVersion = '{version}'; RootModule = '{moduleName}.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }}");
    }

    private static void InvokeRunImportModules(ModulePipelineRunner runner, ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("RunImportModules", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "RunImportModules method signature may have changed.");
        method!.Invoke(runner, new object?[] { plan, buildResult });
    }

    private static ModuleSigningResult InvokeSignBuiltModuleOutput(
        ModulePipelineRunner runner,
        string moduleName,
        string rootPath,
        SigningOptionsConfiguration signing)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("SignBuiltModuleOutput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "SignBuiltModuleOutput method signature may have changed.");
        return (ModuleSigningResult)method!.Invoke(runner, new object?[] { moduleName, rootPath, signing })!;
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

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeHostedOperations : IModulePipelineHostedOperations
    {
        public int ImportValidationCalls { get; private set; }
        public bool LastImportRequired { get; private set; }
        public bool LastImportSelf { get; private set; }
        public bool LastImportVerbose { get; private set; }
        public ImportModuleEntry[] LastImportModules { get; private set; } = Array.Empty<ImportModuleEntry>();
        public ModuleImportValidationTarget[] LastTargets { get; private set; } = Array.Empty<ModuleImportValidationTarget>();
        public int SignCalls { get; private set; }
        public string[] LastIncludePatterns { get; private set; } = Array.Empty<string>();
        public string[] LastExcludePatterns { get; private set; } = Array.Empty<string>();
        public ModuleSigningResult NextSigningResult { get; set; } = new();

        public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
            ModuleDependency[] dependencies,
            ModuleSkipConfiguration? skipModules,
            bool force,
            string? repository,
            RepositoryCredential? credential,
            bool prerelease)
            => throw new InvalidOperationException("Not used in this test.");

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
        {
            ImportValidationCalls++;
            LastImportModules = modules ?? Array.Empty<ImportModuleEntry>();
            LastImportRequired = importRequired;
            LastImportSelf = importSelf;
            LastImportVerbose = verbose;
            LastTargets = targets ?? Array.Empty<ModuleImportValidationTarget>();
        }

        public ModuleSigningResult SignModuleOutput(
            string moduleName,
            string rootPath,
            string[] includePatterns,
            string[] excludeSubstrings,
            SigningOptionsConfiguration signing)
        {
            SignCalls++;
            LastIncludePatterns = includePatterns ?? Array.Empty<string>();
            LastExcludePatterns = excludeSubstrings ?? Array.Empty<string>();
            return NextSigningResult;
        }
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell runner should not be used in this test.");
    }
}
