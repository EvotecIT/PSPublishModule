namespace PowerForge;

/// <summary>Tracks literal brace absence as a value fact, independent of a variable's CLR constraint.</summary>
internal static class PowerShellStringContentPolicy
{
    internal static bool IsBraceFree(PowerShellBoundExpression expression)
    {
        if (expression.Type.ClrType != typeof(string)) return false;
        return expression switch
        {
            PowerShellBoundLiteralExpression literal => literal.Value is null ||
                literal.Value is string text && IsBraceFree(text),
            PowerShellBoundVariableExpression variable => variable.IsBraceFreeString,
            PowerShellBoundBinaryExpression { Operation: PowerShellBoundBinaryOperator.Add } addition =>
                IsBraceFree(addition.Left) && IsBraceFree(addition.Right),
            PowerShellBoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part =>
                (part.Text is null || IsBraceFree(part.Text)) && (part.Expression is null || IsBraceFree(part.Expression))),
            PowerShellBoundConversionExpression conversion when conversion.Operand.Type.ClrType == typeof(string) =>
                IsBraceFree(conversion.Operand),
            _ => false
        };
    }

    private static bool IsBraceFree(string text) => text.IndexOf('{') < 0 && text.IndexOf('}') < 0;
}
