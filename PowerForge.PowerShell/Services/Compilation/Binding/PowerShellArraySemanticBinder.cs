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
        PowerShellCompilationSemanticOracleProfile semanticProfile,
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
            if (kind == PowerShellBoundArrayKind.CollectedExpression &&
                elementSyntax.Count == 1 && element is PowerShellBoundVariableExpression or PowerShellBoundArrayCopyExpression &&
                element.Type.ClrType.IsArray && element.Type.ClrType.GetArrayRank() == 1 &&
                element.Type.ClrType == element.Type.ClrType.GetElementType()!.MakeArrayType())
            {
                if (semanticProfile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51 &&
                    element is PowerShellBoundVariableExpression && element.Type.Provenance == PowerShellTypeFactProvenance.Explicit)
                    return element;
                if (arrayType == typeof(object[]))
                    return new PowerShellBoundArrayCopyExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent), element,
                        semanticProfile.Family == PowerShellCompilationSemanticHostFamily.PowerShell7);
            }
            if (kind == PowerShellBoundArrayKind.CollectedExpression && element is PowerShellBoundArrayExpression array)
            {
                // A command expression enumerates its result exactly once. The values
                // inside an authored array are already records, including nested arrays
                // and dynamic objects; enumerating those records again changes grouping.
                foreach (var record in array.Elements)
                {
                    var stored = BindElement(record);
                    if (stored is null) return null;
                    elements.Add(stored);
                }
                continue;
            }
            element = BindElement(element);
            if (element is null) return null;
            // Ordinary $null is one collected value. Dynamic objects still require
            // runtime enumeration unless an authored array has wrapped them above.
            var preservesNull = element is PowerShellBoundLiteralExpression { Value: null } && arrayType == typeof(object[]);
            if (kind == PowerShellBoundArrayKind.CollectedExpression && !preservesNull &&
                (!PowerShellStableScalarTypePolicy.IsSupported(element.Type.ClrType) || element.ValueState == PowerShellValueState.Null))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2503", "Typed @() expressions require closed scalar output or authored array records; other values retain PowerShell collection and error semantics.", element.Span));
                return null;
            }
            elements.Add(element);
        }
        // An empty authored @() allocates, but collecting an expression with no
        // records uses the PowerShell 7 shared Object[] result. Typed destinations
        // still allocate their converted array; Windows PowerShell always allocates.
        var resultKind = kind == PowerShellBoundArrayKind.CollectedExpression &&
                         elementSyntax.Count != 0 && elements.Count == 0 && arrayType == typeof(object[]) &&
                         semanticProfile.Family == PowerShellCompilationSemanticHostFamily.PowerShell7
            ? PowerShellBoundArrayKind.SharedEmptyCollection
            : kind;
        return new PowerShellBoundArrayExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent), arrayType, resultKind, elements.ToArray());

        PowerShellBoundExpression? BindElement(PowerShellBoundExpression element)
        {
            if (elementType == typeof(string) &&
                PowerShellConversionSemanticBinder.BindClosedStringConversion(element) is { } stringValue)
                return stringValue;
            if (element.ValueState == PowerShellValueState.Null && elementType != typeof(object))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2504", "Null elements in typed arrays require PowerShell element conversion.", element.Span));
                return null;
            }
            if (arrayType != typeof(object[]) && !PowerShellClrTypeSemantics.CanAssign(elementType, element.Type.ClrType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2504", $"Array element type '{element.Type.ClrType.FullName}' cannot be assigned to explicit element type '{elementType.FullName}' without PowerShell runtime conversion.", element.Span));
                return null;
            }
            return element;
        }
    }

    /// <summary>Infers Windows PowerShell's typed-vector pass-through before local storage is declared.</summary>
    internal static Type? InferPreservedVectorType(Ast syntax, Func<string, PowerShellTypeFact?> findType,
        PowerShellCompilationSemanticOracleProfile semanticProfile)
    {
        if (semanticProfile.Family != PowerShellCompilationSemanticHostFamily.WindowsPowerShell51) return null;
        var collected = false;
        while (true)
        {
            switch (syntax)
            {
                case StatementBlockAst { Statements.Count: 1 } block: syntax = block.Statements[0]; continue;
                case PipelineAst { PipelineElements.Count: 1 } pipeline: syntax = pipeline.PipelineElements[0]; continue;
                case CommandExpressionAst command: syntax = command.Expression; continue;
                case ParenExpressionAst parentheses: syntax = parentheses.Pipeline; continue;
                case ArrayExpressionAst { SubExpression.Statements.Count: 1 } array:
                    collected = true; syntax = array.SubExpression.Statements[0]; continue;
                case VariableExpressionAst variable when collected:
                    var type = findType(variable.VariablePath.UserPath);
                    return type is { Provenance: PowerShellTypeFactProvenance.Explicit } && type.ClrType.IsArray &&
                           type.ClrType.GetArrayRank() == 1 && type.ClrType == type.ClrType.GetElementType()!.MakeArrayType()
                        ? type.ClrType : null;
                default: return null;
            }
        }
    }
}
