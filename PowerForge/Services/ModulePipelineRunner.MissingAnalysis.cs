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
    private MissingFunctionsReport? AnalyzeMissingFunctions(string? filePath, string? code, ModulePipelinePlan plan)
    {
        if (string.IsNullOrWhiteSpace(filePath) && string.IsNullOrWhiteSpace(code))
            return null;

        var approved = plan.ApprovedModules ?? Array.Empty<string>();
        var analyzer = new MissingFunctionsAnalyzer();
        var options = new MissingFunctionsOptions(
            approvedModules: approved,
            ignoreFunctions: Array.Empty<string>(),
            includeFunctionsRecursively: true);
        try
        {
            return analyzer.Analyze(filePath, code, options);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Missing function analysis failed. {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return null;
        }
    }

    private static string[] GetRequiredModuleNames(ModulePipelinePlan plan)
    {
        if (plan is null) return Array.Empty<string>();
        return (plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>())
            .Select(m => m.ModuleName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ValidateMissingFunctions(
        MissingFunctionsReport report,
        ModulePipelinePlan plan,
        IReadOnlyCollection<string>? dependentModules)
    {
        if (report is null) return;

        var requiredModules = GetRequiredModuleNames(plan);

        var required = new HashSet<string>(requiredModules, StringComparer.OrdinalIgnoreCase);
        var approved = new HashSet<string>(plan.ApprovedModules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var dependentRequiredModules = dependentModules ?? ResolveDependentRequiredModules(requiredModules, approved);
        var dependent = new HashSet<string>(dependentRequiredModules, StringComparer.OrdinalIgnoreCase);
        var ignoreModules = new HashSet<string>(plan.ModuleSkip?.IgnoreModuleName ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var ignoreFunctions = new HashSet<string>(plan.ModuleSkip?.IgnoreFunctionName ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var commandModuleHints = BuildCommandModuleHintMap(plan.CommandModuleDependencies);
        var force = plan.ModuleSkip?.Force == true;
        var strictMissing = plan.ModuleSkip?.FailOnMissingCommands == true;

        var apps = report.Summary
            .Where(c => string.Equals(c.CommandType, "Application", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (apps.Length > 0)
            _logger.Warn($"Applications used by module: {string.Join(", ", apps)}");

        var failures = new List<string>();

        var moduleCommands = report.Summary
            .Where(c => !string.IsNullOrWhiteSpace(c.CommandType))
            .Where(c => !string.Equals(c.CommandType, "Application", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.Source))
            .GroupBy(c => c.Source, StringComparer.OrdinalIgnoreCase);

        foreach (var group in moduleCommands)
        {
            var moduleName = group.Key ?? string.Empty;
            if (string.IsNullOrWhiteSpace(moduleName))
                continue;
            if (IsBuiltInModule(moduleName))
                continue;
            if (required.Contains(moduleName) || approved.Contains(moduleName) || dependent.Contains(moduleName))
                continue;

            var allIgnored = group.All(c => ignoreFunctions.Contains(c.Name));
            if (force || ignoreModules.Contains(moduleName) || allIgnored)
            {
                _logger.Warn($"Missing module '{moduleName}' ignored by configuration.");
                continue;
            }

            failures.Add(moduleName);
            foreach (var cmd in group)
                _logger.Error($"Missing module '{moduleName}' provides '{cmd.Name}' (CommandType: {cmd.CommandType}).");
        }

        var unresolved = report.Summary
            .Where(c => string.IsNullOrWhiteSpace(c.CommandType))
            .ToArray();

        foreach (var cmd in unresolved)
        {
            var name = cmd.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (name.StartsWith("$", StringComparison.Ordinal))
                continue;
            if (IsBuiltInCommand(name))
                continue;
            if (ignoreFunctions.Contains(name))
            {
                _logger.Warn($"Unresolved command '{name}' ignored by configuration.");
                continue;
            }

            if (force)
            {
                _logger.Warn($"Unresolved command '{name}' (ignored by Force).");
                continue;
            }

            if (TryInferModuleForCommand(name, commandModuleHints, out var inferredModule, out var inferenceSource))
            {
                if (ignoreModules.Contains(inferredModule))
                {
                    _logger.Warn($"Unresolved command '{name}' likely maps to module '{inferredModule}' (ignored by configuration).");
                    continue;
                }

                failures.Add(name);
                _logger.Error($"Unresolved command '{name}' (likely module '{inferredModule}' via {inferenceSource}).");
                continue;
            }

            failures.Add(name);
            _logger.Error($"Unresolved command '{name}' (no module source).");
        }

        if (failures.Count > 0 && !force)
        {
            var unique = failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (strictMissing)
            {
                throw new InvalidOperationException(
                    $"Missing commands detected during merge. Resolve dependencies or configure ModuleSkip. Missing: {string.Join(", ", unique)}.");
            }

            _logger.Warn(
                $"Missing commands detected during merge. Continuing because FailOnMissingCommands is disabled. Missing: {string.Join(", ", unique)}.");
        }
    }

    private static Dictionary<string, string[]> BuildCommandModuleHintMap(IReadOnlyDictionary<string, string[]> commandModuleDependencies)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (commandModuleDependencies is null || commandModuleDependencies.Count == 0)
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in commandModuleDependencies)
        {
            var moduleName = kvp.Key;
            if (string.IsNullOrWhiteSpace(moduleName))
                continue;

            foreach (var cmd in kvp.Value ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(cmd))
                    continue;
                var commandName = cmd.Trim();
                if (!map.TryGetValue(commandName, out var modules))
                {
                    modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[commandName] = modules;
                }

                modules.Add(moduleName.Trim());
            }
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryInferModuleForCommand(
        string commandName,
        IReadOnlyDictionary<string, string[]> commandModuleHints,
        out string moduleName,
        out string source)
    {
        moduleName = string.Empty;
        source = string.Empty;

        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var name = commandName.Trim();

        if (commandModuleHints is not null &&
            commandModuleHints.TryGetValue(name, out var mappedModules) &&
            mappedModules is { Length: > 0 })
        {
            moduleName = mappedModules[0];
            source = "CommandModuleDependencies";
            return true;
        }

        var dash = name.IndexOf("-", StringComparison.Ordinal);
        if (dash > 0 && dash < name.Length - 1)
        {
            var noun = name.Substring(dash + 1);
            if (noun.StartsWith("AD", StringComparison.OrdinalIgnoreCase))
            {
                moduleName = "ActiveDirectory";
                source = "command pattern";
                return true;
            }

            if (noun.StartsWith("DnsServer", StringComparison.OrdinalIgnoreCase))
            {
                moduleName = "DnsServer";
                source = "command pattern";
                return true;
            }

            if (noun.StartsWith("DhcpServer", StringComparison.OrdinalIgnoreCase))
            {
                moduleName = "DhcpServer";
                source = "command pattern";
                return true;
            }
        }

        return false;
    }

    private void LogMergeSummary(
        ModulePipelinePlan plan,
        MergeSourceInfo mergeInfo,
        MissingFunctionsReport? missingReport,
        IReadOnlyCollection<string>? dependentModules)
    {
        if (plan is null) return;

        var requiredModules = plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        var approvedModules = plan.ApprovedModules ?? Array.Empty<string>();
        var dependent = dependentModules ?? Array.Empty<string>();

        _logger.Info($"Merge/dependency summary (required {requiredModules.Length}, approved {approvedModules.Length}, dependent {dependent.Count}).");
        if (requiredModules.Length > 0)
        {
            var formatted = requiredModules
                .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.ModuleName))
                .OrderBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatRequiredModule)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToArray();

            if (formatted.Length > 0)
            {
                _logger.Info($"  Required modules ({formatted.Length}):");
                foreach (var module in formatted)
                    _logger.Info($"    - {module}");
            }
        }

        if (approvedModules.Length > 0)
        {
            var ordered = approvedModules
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ordered.Length > 0)
            {
                _logger.Info($"  Approved modules ({ordered.Length}):");
                foreach (var module in ordered)
                    _logger.Info($"    - {module}");
            }
        }

        if (dependent.Count > 0)
        {
            var ordered = dependent
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ordered.Length > 0)
            {
                _logger.Info($"  Dependent modules ({ordered.Length}):");
                foreach (var module in ordered)
                    _logger.Info($"    - {module}");
            }
        }

        if (plan.MergeModule)
        {
            if (mergeInfo.HasScripts)
                _logger.Info($"MergeModule: {mergeInfo.ScriptFiles.Length} script file(s) found for merge.");
            else if (File.Exists(mergeInfo.Psm1Path))
                _logger.Info("MergeModule: using existing PSM1 (no script sources).");
        }

        if (plan.MergeMissing)
        {
            if (missingReport is null)
            {
                _logger.Warn("MergeMissing: missing function analysis failed; no functions inlined.");
            }
            else
            {
                var topLevel = missingReport.FunctionsTopLevelOnly?.Length ?? 0;
                var total = missingReport.Functions?.Length ?? 0;
                _logger.Info($"MergeMissing: {topLevel} top-level function(s) inlined (total {total} including dependencies).");
            }
        }
    }

    private static string FormatRequiredModule(ManifestEditor.RequiredModule module)
    {
        if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
            return string.Empty;

        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion))
            parts.Add($"required {module.RequiredVersion}");
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
            parts.Add($"minimum {module.ModuleVersion}");
        if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
            parts.Add($"maximum {module.MaximumVersion}");
        if (!string.IsNullOrWhiteSpace(module.Guid))
            parts.Add($"guid {module.Guid}");

        return parts.Count == 0
            ? module.ModuleName
            : $"{module.ModuleName} ({string.Join(", ", parts)})";
    }

    private string[] ResolveDependentRequiredModules(IEnumerable<string> requiredModules, IReadOnlyCollection<string> approvedModules)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in requiredModules ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(module))
                continue;
            if (approvedModules is not null && approvedModules.Contains(module))
                continue;

            CollectModuleDependencies(module, visited, deps);
        }

        return deps.ToArray();
    }

    private void CollectModuleDependencies(string moduleName, HashSet<string> visited, HashSet<string> output)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return;
        if (!visited.Add(moduleName))
            return;

        var required = GetRequiredModulesFromInstalledModule(moduleName);
        foreach (var dep in required)
        {
            if (string.IsNullOrWhiteSpace(dep))
                continue;
            if (output.Add(dep))
                CollectModuleDependencies(dep, visited, output);
        }
    }

    private string[] GetRequiredModulesFromInstalledModule(string moduleName)
    {
        try
        {
            using var ps = CreatePowerShell();
            var script = EmbeddedScripts.Load("Scripts/ModulePipeline/Get-RequiredModules.ps1");
            ps.AddScript(script).AddArgument(moduleName);
            var results = ps.Invoke();
            if (ps.HadErrors || results is null)
                return Array.Empty<string>();

            return results
                .Select(r => r?.BaseObject?.ToString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to resolve required modules for '{moduleName}': {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return Array.Empty<string>();
        }
    }

    private static PowerShell CreatePowerShell()
    {
        if (Runspace.DefaultRunspace is null)
            return PowerShell.Create();
        return PowerShell.Create(RunspaceMode.CurrentRunspace);
    }

    private static bool IsBuiltInModule(string moduleName)
        => moduleName.StartsWith("Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> BuiltInCommandNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Add-Content",
            "Add-Type",
            "Clear-Variable",
            "ConvertFrom-Json",
            "ConvertTo-Json",
            "Copy-Item",
            "Export-ModuleMember",
            "ForEach-Object",
            "Format-List",
            "Format-Table",
            "Get-ChildItem",
            "Get-Command",
            "Get-Content",
            "Get-Date",
            "Get-Item",
            "Get-ItemProperty",
            "Get-Location",
            "Get-Member",
            "Get-Variable",
            "Import-Module",
            "Join-Path",
            "Measure-Object",
            "Move-Item",
            "New-Item",
            "New-Object",
            "Out-File",
            "Pop-Location",
            "Push-Location",
            "Remove-Item",
            "Remove-Variable",
            "Resolve-Path",
            "Select-Object",
            "Set-Content",
            "Set-Item",
            "Set-ItemProperty",
            "Set-Location",
            "Set-Variable",
            "Sort-Object",
            "Split-Path",
            "Start-Process",
            "Start-Sleep",
            "Test-Path",
            "Where-Object",
            "Write-Debug",
            "Write-Error",
            "Write-Host",
            "Write-Information",
            "Write-Output",
            "Write-Progress",
            "Write-Verbose",
            "Write-Warning"
        };

    private static bool IsBuiltInCommand(string name)
        => BuiltInCommandNames.Contains(name);

    private sealed class MergeSourceInfo
    {
        public MergeSourceInfo(string psm1Path, string[] scriptFiles, string mergedScriptContent, bool hasLib)
        {
            Psm1Path = psm1Path;
            ScriptFiles = scriptFiles ?? Array.Empty<string>();
            MergedScriptContent = mergedScriptContent ?? string.Empty;
            HasLib = hasLib;
        }

        public string Psm1Path { get; }
        public string[] ScriptFiles { get; }
        public string MergedScriptContent { get; }
        public bool HasLib { get; }
        public bool HasScripts => ScriptFiles.Length > 0;
    }

}
