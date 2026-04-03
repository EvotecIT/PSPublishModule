namespace PowerForge;

/// <summary>
/// Adapts <see cref="MissingFunctionsAnalyzer"/> output to the host-neutral pipeline contract.
/// </summary>
public sealed class PowerShellMissingFunctionAnalysisService : IMissingFunctionAnalysisService
{
    /// <inheritdoc />
    public MissingFunctionAnalysisResult Analyze(string? filePath, string? code, MissingFunctionsOptions? options = null)
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
            .Select(command => new MissingCommandReference(
                name: command.Name,
                source: command.Source,
                commandType: command.CommandType,
                isAlias: command.IsAlias,
                isPrivate: command.IsPrivate,
                error: command.Error))
            .ToArray();
    }
}
