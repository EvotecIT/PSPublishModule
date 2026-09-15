using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStringSemanticBinder
{
    internal static PowerShellBoundExpression? BindInterpolated(
        ParsedSourceDocument document,
        ExpandableStringExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool usesNativeInvocation = false)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!PowerShellExpandableStringSyntax.TryRead(syntax, out var fragments))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2804", "The parser's expandable-string format could not be mapped losslessly to its interpolation slots.", span));
            return null;
        }

        var parts = new List<PowerShellBoundInterpolatedStringPart>();
        foreach (var fragment in fragments)
        {
            if (fragment.ExpressionIndex is not int expressionIndex)
            {
                parts.Add(new PowerShellBoundInterpolatedStringPart(fragment.Text, null));
                continue;
            }
            var nested = syntax.NestedExpressions[expressionIndex];
            if (nested is not VariableExpressionAst && !usesNativeInvocation)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2101", "Expandable-string subexpressions are not yet represented by the bound pipeline.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                return null;
            }
            // Native-bound functions evaluate admitted subexpressions through their live
            // invocation owner. Runtime-free expressions retain the narrower scalar contract.
            var expression = bindExpression(nested, null);
            if (expression is null) return null;
            if (!usesNativeInvocation && PowerShellStringificationScopePolicy.RequiresCallerScope(typeof(string), expression))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2803", "Expandable-string values require a closed scalar representation. Opaque stringification can invoke callbacks that observe or mutate caller locals and remains on the PowerShell runtime path.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                return null;
            }
            parts.Add(new PowerShellBoundInterpolatedStringPart(null, expression,
                expression.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble));
        }
        return new PowerShellBoundInterpolatedStringExpression(span, parts.ToArray(), usesNativeInvocation);
    }
}
