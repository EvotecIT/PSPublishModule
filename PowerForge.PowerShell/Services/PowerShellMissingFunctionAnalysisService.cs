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
            functions: report.Functions ?? System.Array.Empty<string>(),
            functionsTopLevelOnly: report.FunctionsTopLevelOnly ?? System.Array.Empty<string>());
    }

    private static MissingCommandReference[] Map(MissingFunctionCommand[]? commands)
    {
        return (commands ?? System.Array.Empty<MissingFunctionCommand>())
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
