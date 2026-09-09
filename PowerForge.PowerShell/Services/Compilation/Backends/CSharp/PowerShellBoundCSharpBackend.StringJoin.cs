namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitStringJoin(PowerShellLoweredStringJoinExpression join)
    {
        var values = EmitExpression(join.Values);
        var separator = EmitExpression(join.Separator);
        if (join.NativeSourcePath is not null)
            // CLR argument evaluation preserves both operands before native separator
            // conversion or enumeration can call back into the active invocation.
            return "__nativeFunction.Join(" + values + ", " + separator + ", " +
                (join.IsUnary ? "true" : "false") + ", " + PowerShellCSharpLiteral.QuoteString(join.NativeSourcePath) + ", " +
                join.Span.StartLine + ", " + join.Span.StartColumn + ", " + join.Span.EndLine + ", " + join.Span.EndColumn + ", " +
                PowerShellCSharpLiteral.QuoteString(join.NativeSourceText) + ")";
        return $"new global::System.Func<string>(() => {{ var {join.ValuesTemporary} = {values}; var {join.SeparatorTemporary} = {separator}; return global::System.String.Join(({join.SeparatorTemporary} ?? string.Empty), ({join.ValuesTemporary} ?? global::System.Array.Empty<string>())); }})()";
    }
}
