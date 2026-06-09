using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private MergeExecutionResult ApplyMerge(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        if (plan is null || buildResult is null) return MergeExecutionResult.None;
        if (!plan.MergeModule && !plan.MergeMissing) return MergeExecutionResult.None;

        var scriptFiles = ModuleMergeComposer.ResolveScriptFiles(buildResult.StagingPath, plan.Information);
        var conditionalExportDependencies = ResolveConditionalExportDependencies(
            plan,
            scriptFiles,
            buildResult.Exports);
        var mergeInfo = ModuleMergeComposer.BuildSources(
            buildResult.StagingPath,
            plan.ModuleName,
            plan.Information,
            buildResult.Exports,
            fixRelativePaths: !plan.DoNotAttemptToFixRelativePaths,
            conditionalFunctionDependencies: conditionalExportDependencies,
            scriptFiles: scriptFiles);

        if (!mergeInfo.HasScripts && !File.Exists(mergeInfo.Psm1Path))
        {
            return new MergeExecutionResult(
                mergedModule: false,
                usedExistingPsm1: false,
                retainedBootstrapperBecauseBinaryOutputsDetected: false,
                requiredModules: plan.RequiredModules ?? Array.Empty<RequiredModuleReference>(),
                approvedModules: plan.ApprovedModules ?? Array.Empty<string>(),
                dependentModules: Array.Empty<string>(),
                topLevelInlinedFunctions: 0,
                totalInlinedFunctions: 0,
                scriptFilesDetected: 0,
                hasBinaryOutputs: mergeInfo.HasLib,
                hasScriptSources: false);
        }
        string? analysisCode = mergeInfo.HasScripts ? mergeInfo.MergedScriptContent : null;
        string? analysisPath = mergeInfo.HasScripts ? null : mergeInfo.Psm1Path;

        MissingFunctionAnalysisResult? missingReport = null;
        string[] dependentRequiredModules = Array.Empty<string>();
        if (plan.MergeModule || plan.MergeMissing)
        {
            var requiredModules = GetRequiredModuleNames(plan);
            var approvedModules = plan.ApprovedModules ?? Array.Empty<string>();
            dependentRequiredModules = ResolveDependentRequiredModules(requiredModules, approvedModules);

            missingReport = AnalyzeMissingFunctions(analysisPath, analysisCode, plan);
            LogMergeSummary(plan, mergeInfo, missingReport, dependentRequiredModules);
            if (missingReport is not null)
                ValidateMissingFunctions(missingReport, plan, dependentRequiredModules);
        }

        var mergeOutcome = ModuleMergeApplier.Apply(_logger, plan, mergeInfo, missingReport);

        return new MergeExecutionResult(
            mergedModule: mergeOutcome.MergedModule,
            usedExistingPsm1: mergeOutcome.UsedExistingPsm1,
            retainedBootstrapperBecauseBinaryOutputsDetected: mergeOutcome.RetainedBootstrapperBecauseBinaryOutputsDetected,
            requiredModules: plan.RequiredModules ?? Array.Empty<RequiredModuleReference>(),
            approvedModules: plan.ApprovedModules ?? Array.Empty<string>(),
            dependentModules: dependentRequiredModules,
            topLevelInlinedFunctions: mergeOutcome.TopLevelInlinedFunctions,
            totalInlinedFunctions: mergeOutcome.TotalInlinedFunctions,
            scriptFilesDetected: mergeOutcome.ScriptFilesDetected,
            hasBinaryOutputs: mergeOutcome.HasBinaryOutputs,
            hasScriptSources: mergeOutcome.HasScriptSources);
    }

    private void ApplyPlaceholders(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        if (plan is null || buildResult is null) return;

        var psm1Path = Path.Combine(buildResult.StagingPath, $"{plan.ModuleName}.psm1");
        ModulePsm1PlaceholderApplier.Apply(
            _logger,
            psm1Path,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease,
            plan.PlaceHolders,
            plan.PlaceHolderOption);
    }

    private void RunImportModules(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var cfg = plan.ImportModules;
        if (cfg is null) return;

        var importSelf = cfg.Self == true;
        var importRequired = cfg.RequiredModules == true;
        if (!importSelf && !importRequired) return;

        var modules = importRequired
            ? plan.RequiredModules
                .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName))
                .Select(m => new ImportModuleEntry
                {
                    Name = m.ModuleName.Trim(),
                    MinimumVersion = string.IsNullOrWhiteSpace(m.ModuleVersion) ? null : m.ModuleVersion!.Trim(),
                    RequiredVersion = string.IsNullOrWhiteSpace(m.RequiredVersion) ? null : m.RequiredVersion!.Trim()
                })
                .ToArray()
            : Array.Empty<ImportModuleEntry>();

        var targets = GetImportValidationTargets(
            plan.CompatiblePSEditions,
            buildResult.StagingPath,
            plan.Manifest?.PowerShellVersion);

        _hostedOperations.ValidateModuleImports(
            buildResult.ManifestPath,
            modules,
            importRequired,
            importSelf,
            cfg.Verbose == true,
            targets);
    }

    private void RunBinaryDependencyPreflight(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var cfg = plan.ImportModules;
        if (cfg is null || cfg.Self != true || cfg.SkipBinaryDependencyCheck == true) return;

        foreach (var target in GetImportValidationTargets(
            plan.CompatiblePSEditions,
            buildResult.StagingPath,
            plan.Manifest?.PowerShellVersion))
        {
            _hostedOperations.EnsureBinaryDependenciesValid(
                buildResult.StagingPath,
                target.PowerShellEdition,
                buildResult.ManifestPath,
                target.Label);
        }
    }

    internal static ModuleImportValidationTarget[] GetImportValidationTargets(
        IReadOnlyList<string>? compatiblePSEditions,
        string? stagingPath = null,
        string? minimumPowerShellVersion = null)
    {
        var compatible = compatiblePSEditions ?? Array.Empty<string>();
        var supportsDesktopByVersion = SupportsDesktopImportValidation(minimumPowerShellVersion);
        var requiresCoreByVersion = RequiresCoreImportValidation(minimumPowerShellVersion);
        var hasDesktop = supportsDesktopByVersion &&
                         compatible.Any(static s => string.Equals(s, "Desktop", StringComparison.OrdinalIgnoreCase));
        var hasCore = requiresCoreByVersion ||
                      compatible.Any(static s => string.Equals(s, "Core", StringComparison.OrdinalIgnoreCase));
        var hasDefaultPayload = HasBinaryPayload(stagingPath, "Default");
        var hasCorePayload = HasBinaryPayload(stagingPath, "Core") || HasBinaryPayload(stagingPath, "Standard");
        var hasAnyBinaryPayload = hasDefaultPayload || hasCorePayload;

        if (Path.DirectorySeparatorChar != '\\')
            return new[] { new ModuleImportValidationTarget("PowerShell/Core", "Core", preferPwsh: true) };

        var targets = new List<ModuleImportValidationTarget>(2);

        if (compatible.Count == 0 && hasAnyBinaryPayload)
        {
            if (supportsDesktopByVersion && hasDefaultPayload)
                targets.Add(new ModuleImportValidationTarget("Windows PowerShell/Desktop", "Desktop", preferPwsh: false));
            if (hasCorePayload || requiresCoreByVersion)
                targets.Add(new ModuleImportValidationTarget("PowerShell/Core", "Core", preferPwsh: true));
        }
        else
        {
            if (hasDesktop && (!hasAnyBinaryPayload || hasDefaultPayload))
                targets.Add(new ModuleImportValidationTarget("Windows PowerShell/Desktop", "Desktop", preferPwsh: false));
            if (hasCore && (!hasAnyBinaryPayload || hasCorePayload || requiresCoreByVersion))
                targets.Add(new ModuleImportValidationTarget("PowerShell/Core", "Core", preferPwsh: true));
        }

        if (targets.Count == 0)
        {
            if (hasDesktop)
                targets.Add(new ModuleImportValidationTarget("Windows PowerShell/Desktop", "Desktop", preferPwsh: false));
            if (hasCore)
                targets.Add(new ModuleImportValidationTarget("PowerShell/Core", "Core", preferPwsh: true));
            if (targets.Count == 0)
                targets.Add(new ModuleImportValidationTarget("PowerShell/Core", "Core", preferPwsh: true));
        }

        return targets.ToArray();
    }

    private static bool SupportsDesktopImportValidation(string? minimumPowerShellVersion)
    {
        var version = TryParseMinimumPowerShellVersion(minimumPowerShellVersion);
        return version is null || version <= new Version(5, 1);
    }

    private static bool RequiresCoreImportValidation(string? minimumPowerShellVersion)
    {
        var version = TryParseMinimumPowerShellVersion(minimumPowerShellVersion);
        return version is not null && version > new Version(5, 1);
    }

    private static Version? TryParseMinimumPowerShellVersion(string? minimumPowerShellVersion)
    {
        var normalized = minimumPowerShellVersion?.Trim();
        if (normalized is null || normalized.Length == 0)
            return null;

        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex > 0)
            normalized = normalized.Substring(0, prereleaseIndex);

        return Version.TryParse(normalized, out var parsed) ? parsed : null;
    }

    private static bool HasBinaryPayload(string? stagingPath, string folderName)
    {
        if (string.IsNullOrWhiteSpace(stagingPath) || string.IsNullOrWhiteSpace(folderName))
            return false;

        var payloadPath = Path.Combine(stagingPath, "Lib", folderName);
        return Directory.Exists(payloadPath) &&
               Directory.EnumerateFiles(payloadPath, "*.dll", SearchOption.AllDirectories).Any();
    }

    private void RunTestsAfterMerge(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        TestConfiguration testConfiguration)
    {
        if (plan is null || buildResult is null || testConfiguration is null) return;

        var testsPath = testConfiguration.TestsPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(testsPath))
            throw new InvalidOperationException("TestsAfterMerge is enabled but TestsPath is empty.");

        if (!Path.IsPathRooted(testsPath))
            testsPath = Path.GetFullPath(Path.Combine(plan.ProjectRoot, testsPath));

        var importCfg = plan.ImportModules;
        var importSelf = importCfg?.Self == true;
        var importRequired = importCfg?.RequiredModules == true;
        var importVerbose = importCfg?.Verbose == true;

        var importModules = importRequired
            ? plan.RequiredModules
                .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName))
                .Select(m => new ModuleDependency(
                    name: m.ModuleName.Trim(),
                    requiredVersion: m.RequiredVersion,
                    minimumVersion: m.ModuleVersion,
                    maximumVersion: m.MaximumVersion))
                .ToArray()
            : Array.Empty<ModuleDependency>();

        var spec = new ModuleTestSuiteSpec
        {
            ProjectPath = buildResult.StagingPath,
            TestPath = testsPath,
            Force = testConfiguration.Force,
            SkipDependencies = true,
            SkipImport = !importSelf,
            ImportModules = importModules,
            ImportModulesVerbose = importVerbose
        };

        var result = _hostedOperations.RunModuleTestSuite(spec);
        if (result.FailedCount > 0)
        {
            var failureMessage = BuildTestsAfterMergeFailureMessage(result);
            if (testConfiguration.Force)
            {
                _logger.Warn($"{failureMessage}{Environment.NewLine}Force was set; continuing.");
            }
            else
            {
                throw new InvalidOperationException(failureMessage);
            }
        }
    }

    private static string BuildTestsAfterMergeFailureMessage(ModuleTestSuiteResult result)
    {
        var message = $"TestsAfterMerge failed ({result.FailedCount} failed).";
        if (result.FailureAnalysis is { FailedTests.Length: > 0 } analysis)
        {
            var filteredFailures = analysis.FailedTests
                .Where(static failure => !string.IsNullOrWhiteSpace(failure.Name) || !string.IsNullOrWhiteSpace(failure.ErrorMessage))
                .ToArray();

            var lines = filteredFailures
                .Take(5)
                .Select(FormatTestsAfterMergeFailureLine)
                .ToArray();

            if (lines.Length > 0)
            {
                var omittedCount = filteredFailures.Length - lines.Length;
                var details = string.Join(Environment.NewLine, lines);
                if (omittedCount > 0)
                    details = $"{details}{Environment.NewLine}Additional failed tests omitted: {omittedCount}.";

                return $"{message}{Environment.NewLine}Failed tests:{Environment.NewLine}{details}";
            }
        }

        var stdErrLine = GetFirstMeaningfulLine(result.StdErr);
        if (!string.IsNullOrWhiteSpace(stdErrLine))
            return $"{message}{Environment.NewLine}stderr: {stdErrLine}";

        var stdOutLine = GetFirstMeaningfulLine(result.StdOut);
        if (!string.IsNullOrWhiteSpace(stdOutLine))
            return $"{message}{Environment.NewLine}stdout: {stdOutLine}";

        return message;
    }

    private static string FormatTestsAfterMergeFailureLine(ModuleTestFailureInfo failure)
    {
        var testName = string.IsNullOrWhiteSpace(failure.Name) ? "<unnamed test>" : failure.Name.Trim();
        var errorLine = GetFirstMeaningfulLine(failure.ErrorMessage);
        return string.IsNullOrWhiteSpace(errorLine)
            ? $"- {testName}"
            : $"- {testName}: {errorLine}";
    }

    private static string? GetFirstMeaningfulLine(string? text)
    {
        if (text is null || text.Length == 0 || string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return null;
    }

    private void TryRegenerateBootstrapperFromManifest(
        ModuleBuildResult buildResult,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies,
        bool handleRuntimes,
        ModulePipelinePlan? plan = null)
    {
        try
        {
            var exports = ModuleManifestExportReader.ReadExports(buildResult.ManifestPath);
            var conditionalExportDependencies = plan is null
                ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                : ResolveConditionalExportDependencies(
                    plan,
                    ModuleMergeComposer.ResolveScriptFiles(buildResult.StagingPath, plan.Information),
                    exports);

            ModuleBootstrapperGenerator.Generate(
                buildResult.StagingPath,
                moduleName,
                exports,
                exportAssemblies,
                handleRuntimes,
                useAssemblyLoadContext: plan?.BuildSpec.UseAssemblyLoadContext ?? false,
                assemblyTypeAcceleratorMode: plan?.BuildSpec.AssemblyTypeAcceleratorMode ?? AssemblyTypeAcceleratorExportMode.None,
                assemblyTypeAccelerators: plan?.BuildSpec.AssemblyTypeAccelerators,
                assemblyTypeAcceleratorAssemblies: plan?.BuildSpec.AssemblyTypeAcceleratorAssemblies,
                conditionalFunctionDependencies: conditionalExportDependencies,
                targetFrameworks: plan?.BuildSpec.Frameworks,
                log: message => _logger.Info(message));
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to regenerate module bootstrapper exports for '{moduleName}'. Error: {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
        }
    }

    private void SyncMergedPsm1WithGeneratedScripts(string manifestPath, string stagingPath, string moduleName, IEnumerable<string> scriptPaths)
        => ModuleMergeComposer.SyncMergedPsm1WithGeneratedScripts(manifestPath, stagingPath, moduleName, scriptPaths);

    private IReadOnlyDictionary<string, string[]> ResolveConditionalExportDependencies(
        ModulePipelinePlan plan,
        IEnumerable<string> scriptFiles,
        ExportSet exports)
    {
        if (plan.CommandModuleDependencies.Count == 0)
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var dependencies = CommandModuleExportDependencyAnalyzer.Analyze(
                scriptFiles,
                plan.CommandModuleDependencies,
                exports.Functions,
                _logger);

            if (dependencies.Count > 0)
            {
                foreach (var dependency in dependencies.OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                    _logger.Info($"Conditional exports: module '{dependency.Key}' gates {dependency.Value.Length} command(s).");
            }

            if (_logger.IsVerbose)
            {
                foreach (var moduleName in plan.CommandModuleDependencies.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!dependencies.ContainsKey(moduleName))
                        _logger.Verbose($"Conditional exports: module '{moduleName}' gates 0 command(s).");
                }
            }

            return dependencies;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Conditional export dependency analysis failed. Error: {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

}
