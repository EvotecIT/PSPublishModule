using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class PowerShellMissingFunctionAnalysisService : IMissingFunctionAnalysisService
{
    public MissingFunctionAnalysisResult Analyze(string? filePath, string? code, MissingFunctionsOptions options)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var report = analyzer.Analyze(filePath, code, options);

        return new MissingFunctionAnalysisResult(
            summary: Map(report.Summary),
            summaryFiltered: Map(report.SummaryFiltered),
            functions: report.Functions ?? Array.Empty<string>(),
            functionsTopLevelOnly: report.FunctionsTopLevelOnly ?? Array.Empty<string>(),
            fullyInlinedApprovedModules: ResolveFullyInlinedApprovedModules(report, options))
        {
            ApprovedModuleMergeErrors = FindApprovedModuleMergeErrors(report, options),
            ApprovedModulesUnusedInInput = report.AnalysisComplete
                ? options.ApprovedModules.Except(
                    report.ApprovedModuleRuntimeReferences.Concat(report.Summary.Select(command => command.Source)),
                    StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>()
        };
    }

    private static string[] FindApprovedModuleMergeErrors(MissingFunctionsReport report, MissingFunctionsOptions options)
    {
        var approved = new HashSet<string>(options.ApprovedModules, StringComparer.OrdinalIgnoreCase);
        var references = report.Summary.Where(command => approved.Contains(command.Source)).ToArray();
        var blockers = references
            .Where(command => command.ScriptBlock is null || !string.IsNullOrWhiteSpace(command.Error))
            .Select(command => !string.IsNullOrWhiteSpace(command.Error)
                ? command.Error
                : $"'{command.Name}' from '{command.Source}' is a {command.CommandType} and requires its original module.")
            .ToList();
        foreach (string module in report.ApprovedModuleRuntimeReferences)
        {
            string commands = string.Join(", ", references.Where(command =>
                string.Equals(command.Source, module, StringComparison.OrdinalIgnoreCase)).Select(command => command.Name));
            blockers.Add(commands.Length == 0
                ? $"'{module}' is referenced at runtime."
                : $"'{module}' is referenced at runtime by: {commands}.");
        }
        if (blockers.Count == 0 && approved.Count > 0 && !report.AnalysisComplete)
            blockers.Add($"A dynamic command prevents proving which helpers are required from: {string.Join(", ", approved.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}.");
        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] ResolveFullyInlinedApprovedModules(
        MissingFunctionsReport report,
        MissingFunctionsOptions options)
    {
        if (!report.AnalysisComplete)
            return Array.Empty<string>();

        var approved = new HashSet<string>(
            options.ApprovedModules
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var nonInlineable = new HashSet<string>(
            report.NonInlineableApprovedModules ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var commandsByModule = (report.Summary ?? Array.Empty<MissingFunctionCommand>())
            .Where(command => !string.IsNullOrWhiteSpace(command.Source) && approved.Contains(command.Source))
            .GroupBy(command => command.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        // A dependency can be satisfied without contributing a new function: the consumer or
        // another approved donor may already contain its helpers. Complete analysis also
        // excludes donors whose inspected runtime surface cannot be replaced by function merging.
        return approved
            .Where(module => !nonInlineable.Contains(module) &&
                             (!commandsByModule.TryGetValue(module, out var commands) ||
                              commands.All(command => command.ScriptBlock is not null)))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MissingCommandReference[] Map(MissingFunctionCommand[]? commands)
    {
        return (commands ?? Array.Empty<MissingFunctionCommand>())
            .Select(static command => new MissingCommandReference(
                command.Name,
                command.Source,
                command.CommandType,
                command.IsAlias,
                command.IsPrivate,
                command.Error))
            .ToArray();
    }
}
