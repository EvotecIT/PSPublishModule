using System.Collections.ObjectModel;
using System.Text;

namespace PowerForge;

/// <summary>Canonical minimized semantic cases stored as native embedded PowerShell resources.</summary>
public static class PowerShellCompilationSemanticOracleCaseCatalog
{
    private const string ResourcePrefix = "PowerForge.PowerShellCompilation.SemanticOracle.Cases.";
    private static readonly string[] AllProfiles =
    {
        PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId,
        PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId,
        PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId
    };

    /// <summary>All promoted minimized cases, ordered by stable case identity.</summary>
    public static IReadOnlyList<PowerShellCompilationSemanticOracleCase> Cases { get; } =
        new ReadOnlyCollection<PowerShellCompilationSemanticOracleCase>(CreateCases()
            .OrderBy(static item => item.CaseId, StringComparer.Ordinal)
            .ToArray());

    /// <summary>Returns one known case or fails closed for an unknown identity.</summary>
    public static PowerShellCompilationSemanticOracleCase Get(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("A semantic case identity is required.", nameof(caseId));
        return Cases.FirstOrDefault(item => item.CaseId.Equals(caseId.Trim(), StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Unknown PowerForge semantic-oracle case '{caseId}'.");
    }

    /// <summary>Loads the native PowerShell source for one known minimized case.</summary>
    public static string ReadSource(string caseId)
    {
        var definition = Get(caseId);
        using var stream = typeof(PowerShellCompilationSemanticOracleCaseCatalog).Assembly.GetManifestResourceStream(definition.SourceResourceName)
            ?? throw new InvalidOperationException($"Embedded semantic-oracle case '{definition.SourceResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IEnumerable<PowerShellCompilationSemanticOracleCase> CreateCases()
    {
        yield return Case("parameter-type", PowerShellCompilationFeatureIds.ParameterType, new[] { "42" });
        yield return Case("parameter-default", PowerShellCompilationFeatureIds.ParameterDefault);
        yield return Case("parameter-metadata", PowerShellCompilationFeatureIds.ParameterMetadata);
        yield return Case("parameter-binding", PowerShellCompilationFeatureIds.ParameterBinding);
        yield return Case("conversion", PowerShellCompilationFeatureIds.Conversion);
        yield return Case("expandable-string", PowerShellCompilationFeatureIds.ExpandableString);
        yield return Case("assignment-target", PowerShellCompilationFeatureIds.AssignmentTarget, expectedValue: "0");
        yield return Case("switch-flags", PowerShellCompilationFeatureIds.SwitchFlags);
        yield return Case("catch-filter", PowerShellCompilationFeatureIds.CatchFilter);
        yield return Case("pipeline-lifecycle", PowerShellCompilationFeatureIds.PipelineLifecycle);
        yield return Case("function-graph", PowerShellCompilationFeatureIds.FunctionGraph);
        yield return Case("comment-based-help", PowerShellCompilationFeatureIds.CommentBasedHelp);
        yield return Case("requires-directive", PowerShellCompilationFeatureIds.RequiresDirective);
        yield return Case("dictionary-flow", PowerShellCompilationFeatureIds.DictionaryFlow);
        yield return Case("operator-arithmetic", "operator.arithmetic");
        yield return Case("operator-comparison", "operator.comparison", expectedValue: "True", expectedTypeName: "System.Boolean");
        yield return Case("operator-logical", "operator.logical", expectedValue: "True", expectedTypeName: "System.Boolean");
        yield return Case("pipeline-enumeration", "pipeline.enumeration");
        yield return Case("runtime-read-only-state", "runtime.read-only-state");
    }

    private static PowerShellCompilationSemanticOracleCase Case(
        string caseId,
        string featureId,
        IEnumerable<string>? arguments = null,
        string expectedValue = "42",
        string expectedTypeName = "System.Int32")
        => new(
            "PowerForge.Semantic/" + caseId,
            featureId,
            ResourcePrefix + caseId + ".ps1",
            AllProfiles,
            arguments,
            expectedValue: expectedValue,
            expectedTypeName: expectedTypeName);
}
