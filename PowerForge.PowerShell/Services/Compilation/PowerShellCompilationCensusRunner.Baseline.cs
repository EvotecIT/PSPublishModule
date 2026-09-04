namespace PowerForge;

public sealed partial class PowerShellCompilationCensusRunner
{
    private static void AddFunctionDispositionRegressions(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        int baselineFunctionCount,
        IReadOnlyList<PowerShellCompilationFunctionDisposition> baseline,
        int currentFunctionCount,
        IReadOnlyList<PowerShellCompilationFunctionDisposition> current)
    {
        AddDispositionCountRegression(regressions, product, "BaselineFunctionDispositionCount", baselineFunctionCount, baseline.Count);
        AddDispositionCountRegression(regressions, product, "CurrentFunctionDispositionCount", currentFunctionCount, current.Count);
        var currentById = BuildDispositionMap(regressions, product, "CurrentFunctionDispositionIdentity", current);
        BuildDispositionMap(regressions, product, "BaselineFunctionDispositionIdentity", baseline);
        foreach (var expected in baseline)
        {
            if (string.IsNullOrWhiteSpace(expected.UnitId)) continue;
            if (!currentById.TryGetValue(expected.UnitId, out var actual))
            {
                regressions.Add(new PowerShellCompilationCensusRegression(product, "FunctionDisposition:" + expected.UnitId, 1, 0));
                continue;
            }

            AddIdentityChange(regressions, product, "RelativePathFunction:" + expected.UnitId, expected.RelativePath, actual.RelativePath);
            AddIdentityChange(regressions, product, "NameFunction:" + expected.UnitId, expected.Name, actual.Name);
            AddValueChange(regressions, product, "StartLineFunction:" + expected.UnitId, expected.StartLine, actual.StartLine);
            AddBooleanLoss(regressions, product, "SemanticEligibleFunction:" + expected.UnitId, expected.SemanticEligible, actual.SemanticEligible);
            AddBooleanLoss(regressions, product, "EmittedFunction:" + expected.UnitId, expected.Emitted, actual.Emitted);
            AddBooleanGain(regressions, product, "RuntimeRoutedFunction:" + expected.UnitId, expected.RuntimeRouted, actual.RuntimeRouted);
            AddLowerIsRegression(
                regressions,
                product,
                "PromotedTypedRegionsFunction:" + expected.UnitId,
                expected.PromotedTypedRegions,
                actual.PromotedTypedRegions);
            if (expected.SemanticEligible)
                AddBooleanGain(regressions, product, "ShapingFallbackFunction:" + expected.UnitId, expected.ShapingFallback, actual.ShapingFallback);
        }
    }

    private static Dictionary<string, PowerShellCompilationFunctionDisposition> BuildDispositionMap(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        IEnumerable<PowerShellCompilationFunctionDisposition> dispositions)
    {
        var byId = new Dictionary<string, PowerShellCompilationFunctionDisposition>(StringComparer.Ordinal);
        foreach (var disposition in dispositions)
        {
            if (!string.IsNullOrWhiteSpace(disposition.UnitId) && !byId.ContainsKey(disposition.UnitId))
            {
                byId.Add(disposition.UnitId, disposition);
                continue;
            }
            regressions.Add(new PowerShellCompilationCensusRegression(product, metric, 1, 0));
        }
        return byId;
    }

    private static void AddDispositionCountRegression(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        int expected,
        int actual)
    {
        if (expected != actual) regressions.Add(new PowerShellCompilationCensusRegression(product, metric, expected, actual));
    }

    private static void AddIdentityChange(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        string baseline,
        string current)
    {
        if (!baseline.Equals(current, StringComparison.Ordinal))
            regressions.Add(new PowerShellCompilationCensusRegression(product, metric, 1, 0));
    }

    private static void AddValueChange(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        int baseline,
        int current)
    {
        if (baseline != current) regressions.Add(new PowerShellCompilationCensusRegression(product, metric, baseline, current));
    }

    private static void AddBooleanLoss(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        bool baseline,
        bool current)
    {
        if (baseline && !current) regressions.Add(new PowerShellCompilationCensusRegression(product, metric, 1, 0));
    }

    private static void AddBooleanGain(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        bool baseline,
        bool current)
    {
        if (!baseline && current) regressions.Add(new PowerShellCompilationCensusRegression(product, metric, 0, 1));
    }
}
