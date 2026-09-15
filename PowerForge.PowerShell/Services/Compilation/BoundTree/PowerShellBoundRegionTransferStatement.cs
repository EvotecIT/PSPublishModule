namespace PowerForge;

/// <summary>
/// Returns ordered region-local storage through the helper ABI, without producing PowerShell
/// success records. Multiple locals occupy fixed Object-array slots, including null values.
/// </summary>
internal sealed class PowerShellBoundRegionTransferStatement : PowerShellBoundReturnStatement
{
    internal PowerShellBoundRegionTransferStatement(SourceSpan span, PowerShellBoundVariableExpression[] locals)
        : base(span, CreateResult(span, locals), emitsValue: true, emitsSuccessOutput: false)
        => Locals = locals;

    internal PowerShellImmutableArray<PowerShellBoundVariableExpression> Locals { get; }

    private static PowerShellBoundExpression CreateResult(SourceSpan span, PowerShellBoundVariableExpression[] locals)
    {
        if (locals is not { Length: > 0 } || locals.Any(static local => local.Symbol.Kind != PowerShellSymbolKind.Local) ||
            locals.Select(static local => local.Symbol.StableKey).Distinct(StringComparer.Ordinal).Count() != locals.Length)
            throw new ArgumentException("A region transfer requires distinct ordered local-storage targets.", nameof(locals));
        return locals.Length == 1 ? locals[0] : new PowerShellBoundArrayExpression(
            span, typeof(object[]), PowerShellBoundArrayKind.Literal, locals.Cast<PowerShellBoundExpression>().ToArray());
    }
}
