namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    /// <summary>Closes existing rejections over callers without imposing whole-function eligibility on regions.</summary>
    private static PowerShellBoundProgram PropagateFallbackCalls(PowerShellBoundProgram program)
    {
        var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
        RunFixedPoint(functions, ApplyFallbackCallDisposition,
            static (left, right) => left.Disposition.Kind == right.Disposition.Kind && left.Disposition.ReasonCode == right.Disposition.ReasonCode);
        return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
    }

    private static PowerShellBoundFunction ApplyFallbackCallDisposition(
        PowerShellBoundFunction function,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
    {
        if (function.Disposition.Kind != PowerShellExecutionDispositionKind.Typed) return function;
        var blocked = GetCallees(function, functions).FirstOrDefault(static callee => callee.Disposition.Kind != PowerShellExecutionDispositionKind.Typed);
        return blocked is null
            ? function
            : function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                PowerShellExecutionDispositionKind.Fallback, "call.fallback", $"Local function '{blocked.Symbol.Name}' requires fallback."));
    }
}
