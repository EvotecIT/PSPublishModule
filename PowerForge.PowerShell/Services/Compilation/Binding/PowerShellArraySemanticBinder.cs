using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Binds statically representable PowerShell array expressions without leaking AST nodes downstream.
/// </summary>
internal static class PowerShellArraySemanticBinder
{
    internal static PowerShellBoundExpression? BindNativeCollection(ParsedSourceDocument document, ArrayExpressionAst syntax,
        Type? contextualType, Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationSemanticOracleProfile semanticProfile, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (syntax.SubExpression.Traps is not null || contextualType is { IsArray: true } && contextualType != typeof(object[]))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2503",
                "Native collected statements currently require Object array storage without traps.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        var windowsPowerShell = semanticProfile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51;
        var sourceLines = document.Text.Replace("\r\n", "\n").Split('\n');
        var items = new List<PowerShellBoundNativeCollectionItem>();
        foreach (var statement in syntax.SubExpression.Statements)
        {
            if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                pipeline.PipelineElements[0] is not CommandExpressionAst { Redirections.Count: 0 } command)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2501",
                    "Native collected arrays currently accept expression statements without redirection.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                return null;
            }
            var value = bindExpression(command.Expression, typeof(object));
            if (value is null) return null;
            // A single pure expression bypasses the statement-output suppression
            // used by a multi-statement collector, including bare ++/-- results.
            if (syntax.SubExpression.Statements.Count == 1 && value is PowerShellBoundMutationExpression { UsesNativeInvocation: true } mutation)
                value = mutation.WithResultType(PowerShellTypeFact.Unknown);
            if (syntax.SubExpression.Statements.Count == 1 && command.Expression is ArrayLiteralAst)
                return value;
            var sourceText = string.Join("\n", sourceLines.Skip(statement.Extent.StartLineNumber - 1)
                .Take(statement.Extent.EndLineNumber - statement.Extent.StartLineNumber + 1));
            items.Add(new PowerShellBoundNativeCollectionItem(PowerShellSourceParser.GetSpan(document, statement.Extent),
                sourceText, value, PowerShellNativeStatementStatusPolicy.NeedsSuccessWrite(statement, windowsPowerShell)));
        }
        return new PowerShellBoundNativeCollectionExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
            document.Path, items.ToArray(), !windowsPowerShell, syntax.SubExpression.Statements.Count == 1);
    }

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
                elementSyntax.Count == 1 && element is PowerShellBoundVariableExpression or PowerShellBoundArrayCopyExpression or PowerShellBoundNativeCollectionExpression &&
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
