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
            : typeof(object[]);

        var elementType = arrayType.GetElementType()!;
        var elements = new List<PowerShellBoundExpression>();
        foreach (var item in elementSyntax)
        {
            var element = bindExpression(item, elementType);
            if (element is null) return null;
            if (element.ValueState == PowerShellValueState.Null && elementType != typeof(object))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2504", "Null elements in typed arrays require PowerShell element conversion.", element.Span));
                return null;
            }
            // Ordinary $null is one collected value. A typed destination may convert it
            // (for example, string[] replaces null with an empty string), so only the
            // unconstrained Object[] contract can preserve it directly.
            var preservesNull = element.ValueState == PowerShellValueState.Null && arrayType == typeof(object[]);
            if (kind == PowerShellBoundArrayKind.CollectedExpression && !preservesNull &&
                (!PowerShellStableScalarTypePolicy.IsSupported(element.Type.ClrType) || element.ValueState == PowerShellValueState.Null))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2503", "Typed @() expressions require closed scalar output; potentially enumerable, dynamically typed, and null values retain PowerShell collection and error semantics.", element.Span));
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
