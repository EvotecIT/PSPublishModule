using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private bool ShouldAnalyzeBinaryConflicts(ImportModulesConfiguration cfg, bool importRequired)
    {
        if (!importRequired)
            return false;

        if (cfg.AnalyzeBinaryConflicts.HasValue)
            return cfg.AnalyzeBinaryConflicts.Value;

        return true;
    }

    private BuildDiagnostic[] CreateAutomaticBinaryConflictDiagnostics(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var cfg = plan.ImportModules;
        var importRequired = cfg?.RequiredModules == true;
        if (cfg is null || !ShouldAnalyzeBinaryConflicts(cfg, importRequired))
            return Array.Empty<BuildDiagnostic>();

        var requiredModules = plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        var installedModules = ResolveInstalledRequiredModuleReferences(requiredModules);
        if (installedModules.Length == 0)
            return Array.Empty<BuildDiagnostic>();

        var diagnostics = new List<BuildDiagnostic>();
        var editions = GetBinaryConflictEditions(plan.CompatiblePSEditions);
        diagnostics.AddRange(CreateRequiredModuleOrderDiagnostics(
            requiredModules,
            installedModules,
            editions,
            preferConflictOrder: cfg.PreferBinaryConflictOrder == true));
        diagnostics.AddRange(CreateRequiredModuleBinaryConflictDiagnostics(installedModules, editions));

        if (cfg.Self == true)
            diagnostics.AddRange(CreateBuiltModuleBinaryConflictDiagnostics(plan, buildResult, installedModules, editions));

        return diagnostics.ToArray();
    }

    private ManifestEditor.RequiredModule[] ReorderRequiredModulesForBinaryConflicts(
        ManifestEditor.RequiredModule[] requiredModules,
        IReadOnlyList<string>? compatiblePSEditions)
    {
        var modules = requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        if (modules.Length < 2)
            return modules;

        var installedModules = ResolveInstalledRequiredModuleReferences(modules);
        if (installedModules.Length < 2)
            return modules;

        var scores = ScoreRequiredModuleConflictOrder(installedModules, GetBinaryConflictEditions(compatiblePSEditions));
        if (scores.Count == 0 || scores.Values.All(static value => value == 0))
            return modules;

        var originalIndex = modules
            .Select((module, index) => new { module.ModuleName, index })
            .Where(static item => !string.IsNullOrWhiteSpace(item.ModuleName))
            .ToDictionary(static item => item.ModuleName!, static item => item.index, StringComparer.OrdinalIgnoreCase);

        var reordered = modules
            .OrderByDescending(module => scores.TryGetValue(module.ModuleName ?? string.Empty, out var score) ? score : 0)
            .ThenBy(module => originalIndex.TryGetValue(module.ModuleName ?? string.Empty, out var index) ? index : int.MaxValue)
            .ToArray();

        if (!modules.Select(static m => m.ModuleName).SequenceEqual(reordered.Select(static m => m.ModuleName), StringComparer.OrdinalIgnoreCase))
        {
            var declared = string.Join(", ", modules.Select(static m => m.ModuleName).Where(static name => !string.IsNullOrWhiteSpace(name)));
            var preferred = string.Join(", ", reordered.Select(static m => m.ModuleName).Where(static name => !string.IsNullOrWhiteSpace(name)));
            _logger.Info($"PreferBinaryConflictOrder reordered RequiredModules from '{declared}' to '{preferred}'.");
        }

        return reordered;
    }

    private void WarnOnImportModuleBinaryConflicts(ModulePipelinePlan plan, ModuleBuildResult buildResult)
    {
        var requiredModules = plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        var installedModules = ResolveInstalledRequiredModuleReferences(requiredModules);
        if (installedModules.Length == 0)
            return;

        var editions = GetBinaryConflictEditions(plan.CompatiblePSEditions);
        WarnOnRequiredModuleBinaryConflicts(
            requiredModules,
            installedModules,
            editions,
            preferConflictOrder: plan.ImportModules?.PreferBinaryConflictOrder == true);

        if (plan.ImportModules?.Self == true)
            WarnOnBuiltModuleBinaryConflicts(plan, buildResult, installedModules, editions);
    }

    private void WarnOnRequiredModuleBinaryConflicts(
        ManifestEditor.RequiredModule[] requiredModules,
        InstalledModuleReference[] installedModules,
        string[] editions,
        bool preferConflictOrder)
    {
        if (installedModules.Length < 2)
            return;

        var declaredOrder = requiredModules
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(static module => module.ModuleName!.Trim())
            .ToArray();
        var preferredOrder = BuildPreferredRequiredModuleOrder(declaredOrder, installedModules, editions);

        if (preferredOrder.Length > 1 &&
            !declaredOrder.SequenceEqual(preferredOrder, StringComparer.OrdinalIgnoreCase) &&
            !preferConflictOrder)
        {
            _logger.Warn($"Binary conflict advisory: current RequiredModules order '{string.Join(", ", declaredOrder)}' may be suboptimal. Suggested order: '{string.Join(", ", preferredOrder)}'.");
        }

        var detector = new BinaryConflictDetectionService(_logger);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in installedModules)
        {
            var otherModulePaths = installedModules
                .Where(other => !string.Equals(other.Name, module.Name, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.ModuleBasePath!)
                .ToArray();
            if (otherModulePaths.Length == 0)
                continue;

            foreach (var edition in editions)
            {
                var result = detector.Analyze(
                    module.ModuleBasePath!,
                    edition,
                    currentModuleName: module.Name,
                    searchModulePaths: otherModulePaths);

                foreach (var issue in result.Issues)
                {
                    var preferredFirst = issue.VersionComparison >= 0 ? module.Name : issue.InstalledModuleName;
                    var preferredSecond = issue.VersionComparison >= 0 ? issue.InstalledModuleName : module.Name;
                    var preferredVersion = issue.VersionComparison >= 0 ? issue.PayloadAssemblyVersion : issue.InstalledAssemblyVersion;
                    var otherVersion = issue.VersionComparison >= 0 ? issue.InstalledAssemblyVersion : issue.PayloadAssemblyVersion;
                    var key = string.Join("|", issue.PowerShellEdition, issue.AssemblyName, preferredFirst, preferredVersion, preferredSecond, otherVersion);
                    if (!emitted.Add(key))
                        continue;

                    _logger.Warn(
                        $"{issue.PowerShellEdition} binary conflict advisory: RequiredModules '{preferredFirst}' ({preferredVersion}) and '{preferredSecond}' ({otherVersion}) both ship '{issue.AssemblyName}'. Prefer importing '{preferredFirst}' before '{preferredSecond}' if the newer dependency is backward compatible.");
                }
            }
        }

    }

    private BuildDiagnostic[] CreateRequiredModuleOrderDiagnostics(
        ManifestEditor.RequiredModule[] requiredModules,
        InstalledModuleReference[] installedModules,
        string[] editions,
        bool preferConflictOrder)
    {
        if (installedModules.Length < 2)
            return Array.Empty<BuildDiagnostic>();

        var declaredOrder = requiredModules
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(static module => module.ModuleName!.Trim())
            .ToArray();
        var preferredOrder = BuildPreferredRequiredModuleOrder(declaredOrder, installedModules, editions);
        if (preferredOrder.Length < 2 ||
            declaredOrder.SequenceEqual(preferredOrder, StringComparer.OrdinalIgnoreCase) ||
            preferConflictOrder)
        {
            return Array.Empty<BuildDiagnostic>();
        }

        return new[]
        {
            new BuildDiagnostic(
                ruleId: "BUILD-BINARY-CONFLICT-ORDER",
                area: BuildDiagnosticArea.Build,
                severity: BuildDiagnosticSeverity.Info,
                scope: BuildDiagnosticScope.BuildConfig,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ConfigChange,
                canAutoFix: false,
                summary: "Review RequiredModules import order",
                details: $"Declared RequiredModules order '{string.Join(", ", declaredOrder)}' may load shared assemblies in a suboptimal order. Suggested order: '{string.Join(", ", preferredOrder)}'.",
                recommendedAction: "If shared dependencies are backward compatible, reorder RequiredModules or enable PreferBinaryConflictOrder in New-ConfigurationImportModule.",
                suggestedCommand: "New-ConfigurationImportModule -ImportRequiredModules -PreferBinaryConflictOrder")
        };
    }

    private BuildDiagnostic[] CreateRequiredModuleBinaryConflictDiagnostics(
        InstalledModuleReference[] installedModules,
        string[] editions)
    {
        if (installedModules.Length < 2)
            return Array.Empty<BuildDiagnostic>();

        var detector = new BinaryConflictDetectionService(_logger);
        var diagnostics = new List<BuildDiagnostic>();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in installedModules)
        {
            var otherModulePaths = installedModules
                .Where(other => !string.Equals(other.Name, module.Name, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.ModuleBasePath!)
                .ToArray();
            if (otherModulePaths.Length == 0)
                continue;

            foreach (var edition in editions)
            {
                var result = detector.Analyze(
                    module.ModuleBasePath!,
                    edition,
                    currentModuleName: module.Name,
                    searchModulePaths: otherModulePaths);

                foreach (var issue in result.Issues)
                {
                    var preferredFirst = issue.VersionComparison >= 0 ? module.Name : issue.InstalledModuleName;
                    var preferredSecond = issue.VersionComparison >= 0 ? issue.InstalledModuleName : module.Name;
                    var preferredVersion = issue.VersionComparison >= 0 ? issue.PayloadAssemblyVersion : issue.InstalledAssemblyVersion;
                    var otherVersion = issue.VersionComparison >= 0 ? issue.InstalledAssemblyVersion : issue.PayloadAssemblyVersion;
                    var key = string.Join("|", issue.PowerShellEdition, issue.AssemblyName, preferredFirst, preferredVersion, preferredSecond, otherVersion);
                    if (!emitted.Add(key))
                        continue;

                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "BUILD-BINARY-CONFLICT-REQUIRED",
                        area: BuildDiagnosticArea.Build,
                        severity: BuildDiagnosticSeverity.Info,
                        scope: BuildDiagnosticScope.BuildConfig,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: false,
                        summary: $"Review RequiredModules sharing {issue.AssemblyName}",
                        details: $"{issue.PowerShellEdition}: RequiredModules '{preferredFirst}' ({preferredVersion}) and '{preferredSecond}' ({otherVersion}) both ship '{issue.AssemblyName}'. Import order likely determines which version wins.",
                        recommendedAction: $"Prefer importing '{preferredFirst}' before '{preferredSecond}' if the newer dependency is backward compatible, or align the shared dependency versions."));
                }
            }
        }

        return diagnostics.ToArray();
    }

    private BuildDiagnostic[] CreateBuiltModuleBinaryConflictDiagnostics(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        InstalledModuleReference[] installedModules,
        string[] editions)
    {
        var detector = new BinaryConflictDetectionService(_logger);
        var diagnostics = new List<BuildDiagnostic>();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modulePaths = installedModules
            .Select(module => module.ModuleBasePath!)
            .ToArray();

        foreach (var edition in editions)
        {
            var result = detector.Analyze(
                buildResult.StagingPath,
                edition,
                currentModuleName: plan.ModuleName,
                searchModulePaths: modulePaths);

            foreach (var issue in result.Issues)
            {
                var moduleLabel = string.IsNullOrWhiteSpace(issue.InstalledModuleVersion)
                    ? issue.InstalledModuleName
                    : issue.InstalledModuleName + " " + issue.InstalledModuleVersion;
                var key = string.Join("|", issue.PowerShellEdition, issue.AssemblyName, moduleLabel, issue.InstalledAssemblyVersion, issue.PayloadAssemblyVersion);
                if (!emitted.Add(key))
                    continue;

                diagnostics.Add(new BuildDiagnostic(
                    ruleId: "BUILD-BINARY-CONFLICT-SELF",
                    area: BuildDiagnosticArea.Build,
                    severity: BuildDiagnosticSeverity.Info,
                    scope: BuildDiagnosticScope.Staging,
                    owner: BuildDiagnosticOwner.ModuleAuthor,
                    remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                    canAutoFix: false,
                    summary: $"Review shared assembly {issue.AssemblyName} in required modules",
                    details: $"{issue.PowerShellEdition}: RequiredModule '{moduleLabel}' ships '{issue.AssemblyName}' {issue.InstalledAssemblyVersion}, while the module under build ships {issue.PayloadAssemblyVersion}. RequiredModules load before the module under build.",
                    recommendedAction: "Align the shared dependency version, enable PreferBinaryConflictOrder for dependency ordering, or preload the dependency in the module bootstrapper when that is known to be safe."));
            }
        }

        return diagnostics.ToArray();
    }

    private void WarnOnBuiltModuleBinaryConflicts(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        InstalledModuleReference[] installedModules,
        string[] editions)
    {
        var detector = new BinaryConflictDetectionService(_logger);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modulePaths = installedModules
            .Select(module => module.ModuleBasePath!)
            .ToArray();

        foreach (var edition in editions)
        {
            var result = detector.Analyze(
                buildResult.StagingPath,
                edition,
                currentModuleName: plan.ModuleName,
                searchModulePaths: modulePaths);

            foreach (var issue in result.Issues)
            {
                var moduleLabel = string.IsNullOrWhiteSpace(issue.InstalledModuleVersion)
                    ? issue.InstalledModuleName
                    : issue.InstalledModuleName + " " + issue.InstalledModuleVersion;
                var key = string.Join("|", issue.PowerShellEdition, issue.AssemblyName, moduleLabel, issue.InstalledAssemblyVersion, issue.PayloadAssemblyVersion);
                if (!emitted.Add(key))
                    continue;

                _logger.Warn(
                    $"{issue.PowerShellEdition} binary conflict advisory: RequiredModule '{moduleLabel}' ships '{issue.AssemblyName}' {issue.InstalledAssemblyVersion}, while the module under build ships {issue.PayloadAssemblyVersion}. RequiredModules load before the module under build, so align the shared dependency version or preload it in the module bootstrapper.");
            }
        }
    }

    private string[] BuildPreferredRequiredModuleOrder(
        IReadOnlyList<string> declaredOrder,
        InstalledModuleReference[] installedModules,
        string[] editions)
    {
        if (declaredOrder is null || declaredOrder.Count == 0)
            return Array.Empty<string>();

        var scores = ScoreRequiredModuleConflictOrder(installedModules, editions);
        if (scores.Count == 0)
            return declaredOrder.ToArray();

        var originalIndex = declaredOrder
            .Select((name, index) => new { name, index })
            .Where(static item => !string.IsNullOrWhiteSpace(item.name))
            .ToDictionary(static item => item.name, static item => item.index, StringComparer.OrdinalIgnoreCase);

        return declaredOrder
            .OrderByDescending(name => scores.TryGetValue(name, out var score) ? score : 0)
            .ThenBy(name => originalIndex.TryGetValue(name, out var index) ? index : int.MaxValue)
            .ToArray();
    }

    private Dictionary<string, int> ScoreRequiredModuleConflictOrder(
        InstalledModuleReference[] installedModules,
        string[] editions)
    {
        var modules = installedModules ?? Array.Empty<InstalledModuleReference>();
        var detector = new BinaryConflictDetectionService(_logger);
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
            scores[module.Name] = 0;

        foreach (var module in modules)
        {
            var otherModulePaths = modules
                .Where(other => !string.Equals(other.Name, module.Name, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.ModuleBasePath!)
                .ToArray();
            if (otherModulePaths.Length == 0)
                continue;

            foreach (var edition in editions)
            {
                var result = detector.Analyze(
                    module.ModuleBasePath!,
                    edition,
                    currentModuleName: module.Name,
                    searchModulePaths: otherModulePaths);

                foreach (var issue in result.Issues)
                    scores[module.Name] += Math.Sign(issue.VersionComparison);
            }
        }

        return scores;
    }

    private InstalledModuleReference[] ResolveInstalledRequiredModuleReferences(IReadOnlyList<ManifestEditor.RequiredModule> requiredModules)
    {
        var names = (requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>())
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(static module => module.ModuleName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
            return Array.Empty<InstalledModuleReference>();

        var installed = TryGetLatestInstalledModuleInfo(names);
        var resolved = new List<InstalledModuleReference>(names.Length);
        var missing = new List<string>();
        foreach (var name in names)
        {
            if (!installed.TryGetValue(name, out var module) ||
                string.IsNullOrWhiteSpace(module.ModuleBasePath) ||
                !Directory.Exists(module.ModuleBasePath))
            {
                missing.Add(name);
                continue;
            }

            resolved.Add(module);
        }

        if (missing.Count > 0)
            _logger.Info($"Binary conflict analysis skipped for required modules that are not installed locally: {string.Join(", ", missing.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))}.");

        return resolved.ToArray();
    }

    private static string[] GetBinaryConflictEditions(IReadOnlyList<string>? compatiblePSEditions)
    {
        var editions = (compatiblePSEditions ?? Array.Empty<string>())
            .Where(static edition => !string.IsNullOrWhiteSpace(edition))
            .Select(static edition => string.Equals(edition.Trim(), "Desktop", StringComparison.OrdinalIgnoreCase) ? "Desktop" : "Core")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return editions.Length == 0 ? new[] { "Core" } : editions;
    }
}
