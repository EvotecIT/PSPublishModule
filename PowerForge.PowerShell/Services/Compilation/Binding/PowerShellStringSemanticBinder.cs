using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStringSemanticBinder
{
    internal static PowerShellBoundExpression? BindInterpolated(
        ParsedSourceDocument document,
        ExpandableStringExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!PowerShellExpandableStringSyntax.TryRead(syntax, out var fragments))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2804", "The parser's expandable-string format could not be mapped losslessly to its interpolation slots.", span));
            return null;
        }

        var parts = new List<PowerShellBoundInterpolatedStringPart>();
        var usePowerShellRuntime = false;
        foreach (var fragment in fragments)
        {
            if (fragment.ExpressionIndex is not int expressionIndex)
            {
                parts.Add(new PowerShellBoundInterpolatedStringPart(fragment.Text, null));
                continue;
            }
            var nested = syntax.NestedExpressions[expressionIndex];
            if (nested is not VariableExpressionAst)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2101", "Expandable-string subexpressions are not yet represented by the bound pipeline.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                return null;
            }
            var expression = bindExpression(nested, null);
            if (expression is null) return null;
            if (!PowerShellStableScalarTypePolicy.IsSupported(expression.Type.ClrType))
            {
                if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2803", "Expandable-string values require a stable scalar representation or the PowerShell statement-error host's stringification contract.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                    return null;
                }
                usePowerShellRuntime = true;
            }
            parts.Add(new PowerShellBoundInterpolatedStringPart(null, expression));
        }
        return new PowerShellBoundInterpolatedStringExpression(span, parts.ToArray(), usePowerShellRuntime);
    }
}
