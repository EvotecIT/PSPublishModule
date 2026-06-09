using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleTestSuiteDisplayService
{
    private readonly ModuleTestFailureDisplayService _failureDisplayService;

    public ModuleTestSuiteDisplayService(ModuleTestFailureDisplayService? failureDisplayService = null)
    {
        _failureDisplayService = failureDisplayService ?? new ModuleTestFailureDisplayService();
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateHeader(string projectRoot, string psVersion, string psEdition, bool ciMode)
    {
        return new[]
        {
            Line(ciMode ? "=== CI/CD Module Testing Pipeline ===" : "=== PowerShell Module Test Suite ===", ConsoleColor.Magenta),
            Line($"Project Path: {projectRoot}", ConsoleColor.Cyan),
            Line($"PowerShell Version: {psVersion}", ConsoleColor.Cyan),
            Line($"PowerShell Edition: {psEdition}", ConsoleColor.Cyan),
            Line(string.Empty)
        };
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateModuleInfoHeader() => new[]
    {
        Line("Step 1: Gathering module information...", ConsoleColor.Yellow)
    };

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateModuleInfoDetails(ModuleInformation info)
    {
        if (info is null)
            throw new ArgumentNullException(nameof(info));

        return new[]
        {
            Line($"  Module Name: {info.ModuleName}", ConsoleColor.Green),
            Line($"  Module Version: {info.ModuleVersion ?? string.Empty}", ConsoleColor.Green),
            Line($"  Manifest Path: {info.ManifestPath}", ConsoleColor.Green),
            Line($"  Required Modules: {(info.RequiredModules ?? Array.Empty<RequiredModuleReference>()).Length}", ConsoleColor.Green),
            Line(string.Empty)
        };
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateExecutionHeader() => new[]
    {
        Line("Step 2: Executing test suite (out-of-process)...", ConsoleColor.Yellow)
    };

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateDependencySummary(
        RequiredModuleReference[] requiredModules,
        string[] additionalModules,
        string[] skipModules)
    {
        var lines = new List<ModuleTestSuiteDisplayLine>
        {
            Line("Step 3: Dependency summary...", ConsoleColor.Yellow)
        };

        if (requiredModules.Length == 0)
        {
            lines.Add(Line("  No required modules specified in manifest", ConsoleColor.Gray));
        }
        else
        {
            lines.Add(Line("Required modules:", ConsoleColor.Cyan));
            foreach (var module in requiredModules)
            {
                var versionInfo = string.Empty;
                if (!string.IsNullOrWhiteSpace(module.ModuleVersion)) versionInfo += $" (Min: {module.ModuleVersion})";
                if (!string.IsNullOrWhiteSpace(module.RequiredVersion)) versionInfo += $" (Required: {module.RequiredVersion})";
                if (!string.IsNullOrWhiteSpace(module.MaximumVersion)) versionInfo += $" (Max: {module.MaximumVersion})";
                lines.Add(Line($"  📦 {module.ModuleName}{versionInfo}", ConsoleColor.Green));
            }
        }

        if (additionalModules.Length > 0)
        {
            lines.Add(Line("Additional modules:", ConsoleColor.Cyan));
            foreach (var module in additionalModules)
            {
                if (skipModules.Contains(module, StringComparer.OrdinalIgnoreCase))
                    continue;

                lines.Add(Line($"  ✅ {module}", ConsoleColor.Green));
            }
        }

        lines.Add(Line(string.Empty));
        return lines;
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateDependencyInstallResults(ModuleDependencyInstallResult[] results)
    {
        var lines = new List<ModuleTestSuiteDisplayLine>
        {
            Line("Step 4: Dependency installation results...", ConsoleColor.Yellow)
        };

        if (results.Length == 0)
        {
            lines.Add(Line("  (no dependency install actions)", ConsoleColor.Gray));
            lines.Add(Line(string.Empty));
            return lines;
        }

        foreach (var result in results)
        {
            switch (result.Status)
            {
                case ModuleDependencyInstallStatus.Skipped:
                    lines.Add(Line($"  ⏭️ Skipping: {result.Name}", ConsoleColor.Gray));
                    break;
                case ModuleDependencyInstallStatus.Satisfied:
                    lines.Add(Line($"  ✅ {result.Name} OK (installed: {result.InstalledVersion ?? "unknown"})", ConsoleColor.Green));
                    break;
                case ModuleDependencyInstallStatus.Installed:
                case ModuleDependencyInstallStatus.Updated:
                {
                    var icon = result.Status == ModuleDependencyInstallStatus.Updated ? "🔄" : "📥";
                    lines.Add(Line($"  {icon} {result.Name} {result.Status} via {result.Installer ?? "installer"} (resolved: {result.ResolvedVersion ?? "unknown"})", ConsoleColor.Green));
                    break;
                }
                case ModuleDependencyInstallStatus.Failed:
                    lines.Add(Line($"  ❌ {result.Name}: {result.Message}", ConsoleColor.Red));
                    break;
            }
        }

        lines.Add(Line(string.Empty));
        return lines;
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateCompletionSummary(ModuleTestSuiteResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var successColor = result.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green;
        var testColor = result.FailedCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
        var lines = new List<ModuleTestSuiteDisplayLine>
        {
            Line(result.FailedCount > 0 ? "=== Test Suite Failed ===" : "=== Test Suite Completed Successfully ===", successColor),
            Line($"Module: {result.ModuleName} v{result.ModuleVersion ?? string.Empty}", ConsoleColor.Green),
            Line($"Tests: {result.PassedCount}/{result.TotalCount} passed", testColor)
        };

        if (result.Duration.HasValue)
            lines.Add(Line($"Duration: {result.Duration.Value}", ConsoleColor.Green));

        lines.Add(Line(string.Empty));
        return lines;
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateFailureSummary(ModuleTestFailureAnalysis? analysis, bool detailed)
    {
        if (analysis is null)
        {
            return new[]
            {
                Line("No failure analysis available.", ConsoleColor.Yellow)
            };
        }

        var sourceLines = detailed
            ? _failureDisplayService.CreateDetailed(analysis)
            : _failureDisplayService.CreateSummary(analysis, showSuccessful: true);

        return sourceLines
            .Select(line => new ModuleTestSuiteDisplayLine
            {
                Text = line.Text,
                Color = line.Color
            })
            .ToArray();
    }

    private static ModuleTestSuiteDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };
}
