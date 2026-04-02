using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge.Tests;

public sealed class ModuleMergeApplierTests
{
    [Fact]
    public void Apply_WarnsAndKeepsBootstrapper_WhenBinaryOutputsAreDetected()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var logger = new CollectingLogger();
            var plan = CreatePlan(root.FullName, mergeModule: true, mergeMissing: false);
            var mergeSources = new ModuleMergeSources(
                psm1Path: Path.Combine(root.FullName, "TestModule.psm1"),
                scriptFiles: new[] { Path.Combine(root.FullName, "Public", "Get-Test.ps1") },
                mergedScriptContent: "function Get-Test { 'ok' }",
                hasLib: true);

            var outcome = ModuleMergeApplier.Apply(logger, plan, mergeSources, missingReport: null);

            Assert.False(outcome.MergedModule);
            Assert.True(outcome.RetainedBootstrapperBecauseBinaryOutputsDetected);
            Assert.Contains(logger.Warnings, static warning => warning.Contains("binary outputs were detected", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Apply_WritesMergedPsm1AndPrependsMissingFunctions_WhenMergeMissingIsEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string psm1Name = moduleName + ".psm1";
            var logger = new CollectingLogger();
            var psm1Path = Path.Combine(root.FullName, psm1Name);

            File.WriteAllText(psm1Path, "function Old-Bootstrap { 'old' }");

            var plan = CreatePlan(root.FullName, mergeModule: true, mergeMissing: true);
            var mergeSources = new ModuleMergeSources(
                psm1Path: psm1Path,
                scriptFiles: new[] { Path.Combine(root.FullName, "Public", "Get-Test.ps1") },
                mergedScriptContent: "function Get-Test { 'ok' }",
                hasLib: false);
            var missingReport = new MissingFunctionAnalysisResult(
                summary: Array.Empty<MissingCommandReference>(),
                summaryFiltered: Array.Empty<MissingCommandReference>(),
                functions: new[] { "function Get-Helper { 'helper' }" },
                functionsTopLevelOnly: new[] { "function Get-Helper { 'helper' }" });

            var outcome = ModuleMergeApplier.Apply(logger, plan, mergeSources, missingReport);
            var merged = File.ReadAllText(psm1Path);

            Assert.True(outcome.MergedModule);
            Assert.Equal(1, outcome.TopLevelInlinedFunctions);
            Assert.Equal(1, outcome.TotalInlinedFunctions);
            Assert.StartsWith("function Get-Helper", merged, StringComparison.Ordinal);
            Assert.Contains("function Get-Test", merged, StringComparison.Ordinal);
            Assert.Contains(logger.Warnings, static warning => warning.Contains("overwrite existing PSM1 content", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelinePlan CreatePlan(string projectRoot, bool mergeModule, bool mergeMissing)
    {
        return new ModulePipelinePlan(
            moduleName: "TestModule",
            projectRoot: projectRoot,
            expectedVersion: "1.0.0",
            resolvedVersion: "1.0.0",
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "TestModule",
                SourcePath = projectRoot,
                Version = "1.0.0"
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: Array.Empty<string>(),
            requiredModules: Array.Empty<RequiredModuleReference>(),
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
            mergeModule: mergeModule,
            mergeMissing: mergeMissing,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: Array.Empty<string>(),
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: Array.Empty<ConfigurationPublishSegment>(),
            artefacts: Array.Empty<ConfigurationArtefactSegment>(),
            installEnabled: false,
            installStrategy: InstallationStrategy.AutoRevision,
            installKeepVersions: 1,
            installRoots: Array.Empty<string>(),
            installLegacyFlatHandling: LegacyFlatModuleHandling.Warn,
            installPreserveVersions: Array.Empty<string>(),
            installMissingModules: false,
            installMissingModulesForce: false,
            installMissingModulesPrerelease: false,
            installMissingModulesRepository: null,
            installMissingModulesCredential: null,
            stagingWasGenerated: false,
            deleteGeneratedStagingAfterRun: false);
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message)
        {
        }

        public void Success(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message ?? string.Empty);

        public void Error(string message)
        {
        }

        public void Verbose(string message)
        {
        }
    }
}
