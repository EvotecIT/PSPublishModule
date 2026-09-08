namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitNativeVariableRead(PowerShellLoweredNativeVariableExpression variable)
        => "__nativeFunction.GetVariable(" + PowerShellCSharpLiteral.QuoteString(variable.Name) + ", " +
           (variable.InExpandableString ? "true" : "false") + ", " + PowerShellCSharpLiteral.QuoteString(variable.SourcePath) + ", " +
           variable.Span.StartLine + ", " + variable.Span.StartColumn + ", " + variable.Span.EndLine + ", " + variable.Span.EndColumn + ", " +
           PowerShellCSharpLiteral.QuoteString(variable.SourceText) + ", " + (variable.DirectLocal ? "true" : "false") + ")";
}
