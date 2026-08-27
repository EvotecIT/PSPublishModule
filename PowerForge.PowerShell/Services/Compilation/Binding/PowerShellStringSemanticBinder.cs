using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStringSemanticBinder
{
    internal static PowerShellBoundExpression? BindInterpolated(
        ParsedSourceDocument document,
        ExpandableStringExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.Extent.Text.Contains("`$", StringComparison.Ordinal))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2801", "Expandable strings that mix escaped dollar signs with interpolation require PowerShell token-preserving semantics.", span));
            return null;
        }

        var parts = new List<PowerShellBoundInterpolatedStringPart>();
        var cursor = 0;
        foreach (var nested in syntax.NestedExpressions)
        {
            if (nested is not VariableExpressionAst)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2802", "Typed expandable strings accept statically typed String variables; subexpressions remain on the PowerShell path.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                return null;
            }
            var expression = bindExpression(nested, typeof(string));
            if (expression is null || expression.Type.ClrType != typeof(string))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2803", "Typed expandable-string variables must have a stable String representation.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                return null;
            }
            var token = nested.Extent.Text;
            var tokenIndex = syntax.Value.IndexOf(token, cursor, StringComparison.Ordinal);
            if (tokenIndex < 0)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2804", "Expandable string source could not be mapped losslessly to its parsed interpolation token.", PowerShellSourceParser.GetSpan(document, nested.Extent)));
                return null;
            }
            if (tokenIndex > cursor)
                parts.Add(new PowerShellBoundInterpolatedStringPart(syntax.Value.Substring(cursor, tokenIndex - cursor), null));
            parts.Add(new PowerShellBoundInterpolatedStringPart(null, expression));
            cursor = tokenIndex + token.Length;
        }
        if (cursor < syntax.Value.Length)
            parts.Add(new PowerShellBoundInterpolatedStringPart(syntax.Value.Substring(cursor), null));
        if (parts.Count == 0)
            parts.Add(new PowerShellBoundInterpolatedStringPart(syntax.Value, null));
        return new PowerShellBoundInterpolatedStringExpression(span, parts.ToArray());
    }
}
