using System;
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
            fullyInlinedApprovedModules: ResolveFullyInlinedApprovedModules(report, options));
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
