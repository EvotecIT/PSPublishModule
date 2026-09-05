using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Binds statically representable PowerShell array expressions without leaking AST nodes downstream.
/// </summary>
internal static class PowerShellArraySemanticBinder
{
    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        Ast syntax,
        IReadOnlyList<ExpressionAst> elementSyntax,
        PowerShellBoundArrayKind kind,
        Type? contextualType,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var arrayType = contextualType is { IsArray: true } && contextualType.GetArrayRank() == 1
            ? contextualType
            : elementSyntax.Count == 0
                ? null
                : typeof(object[]);
        if (arrayType is null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2502", "Empty array expressions require an explicit one-dimensional array type on the assignment target.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }

        var elementType = arrayType.GetElementType()!;
        var elements = new List<PowerShellBoundExpression>();
        foreach (var item in elementSyntax)
        {
            var element = bindExpression(item, elementType);
            if (element is null) return null;
            if (kind == PowerShellBoundArrayKind.CollectedExpression &&
                (element.Type.ClrType.IsArray || element.ValueState == PowerShellValueState.Null))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2503", "Typed @() expressions do not accept array-valued or null pipeline output.", element.Span));
                return null;
            }
            if (arrayType != typeof(object[]) && !PowerShellClrTypeSemantics.CanAssign(elementType, element.Type.ClrType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2504", $"Array element type '{element.Type.ClrType.FullName}' cannot be assigned to explicit element type '{elementType.FullName}' without PowerShell runtime conversion.", element.Span));
                return null;
            }
            elements.Add(element);
        }
        return new PowerShellBoundArrayExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent), arrayType, kind, elements.ToArray());
    }
}
