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
        => BindNativeStatements(document, syntax, syntax.SubExpression, contextualType, bindExpression, semanticProfile, diagnostics, false);

    internal static PowerShellBoundExpression? BindNativeSubexpression(ParsedSourceDocument document, SubExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationSemanticOracleProfile semanticProfile, ICollection<PowerShellSemanticDiagnostic> diagnostics)
        => BindNativeStatements(document, syntax, syntax.SubExpression, null, bindExpression, semanticProfile, diagnostics, true);

    private static PowerShellBoundExpression? BindNativeStatements(ParsedSourceDocument document, Ast syntax,
        StatementBlockAst statements, Type? contextualType, Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationSemanticOracleProfile semanticProfile, ICollection<PowerShellSemanticDiagnostic> diagnostics, bool collapseResult)
    {
        if (statements.Traps is not null || contextualType is { IsArray: true } && contextualType != typeof(object[]))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2503",
                "Native collected statements currently require Object array storage without traps.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        // An empty authored $() is an ordinary null literal; nonempty captures with no output use AutomationNull.
        if (collapseResult && statements.Statements.Count == 0)
            return new PowerShellBoundLiteralExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent), null,
                PowerShellTypeFact.Unknown, PowerShellValueState.Null);
        var windowsPowerShell = semanticProfile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51;
        var items = new List<PowerShellBoundNativeCollectionItem>();
        var singlePureExpression = false;
        foreach (var statement in statements.Statements)
        {
            if (statement is AssignmentStatementAst assignment)
            {
                var assigned = bindExpression(assignment, null);
                if (assigned is null) return null;
                var assignmentSpan = PowerShellSourceParser.GetSpan(document, assignment.Extent);
                items.Add(new PowerShellBoundNativeCollectionItem(assignmentSpan,
                    PowerShellSourceParser.GetSourceLines(document, assignmentSpan), assigned,
                    PowerShellNativeStatementStatusPolicy.NeedsSuccessWrite(assignment, windowsPowerShell),
                    false, false));
                continue;
            }
            if (statement is not PipelineAst pipeline)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2501",
                    "Native value collections require expression or command-pipeline statements.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                return null;
            }
            var command = !PowerShellCommandRegionSemanticBinder.RequiresPipelineSyntax(pipeline)
                ? pipeline.PipelineElements[0] as CommandExpressionAst : null;
            var discard = command?.Expression is ConvertExpressionAst conversion && conversion.StaticType == typeof(void);
            var expression = discard ? ((ConvertExpressionAst)command!.Expression).Child : command?.Expression;
            var value = bindExpression(expression is null ? pipeline : expression, typeof(object));
            if (value is null) return null;
            singlePureExpression = !collapseResult && statements.Statements.Count == 1 && command is not null;
            // A single pure expression bypasses the statement-output suppression
            // used by a multi-statement collector, including bare ++/-- results.
            if (!collapseResult && statements.Statements.Count == 1 && value is PowerShellBoundMutationExpression { UsesNativeInvocation: true } mutation)
                value = mutation.WithResultType(PowerShellTypeFact.Unknown,
                    nativeSetSequencePoint: mutation.Value is not null && mutation.NativeSetSequencePoint);
            if (!collapseResult && statements.Statements.Count == 1 && command?.Expression is ArrayLiteralAst)
                return value;
            var sourceText = PowerShellSourceParser.GetSourceLines(document, PowerShellSourceParser.GetSpan(document, statement.Extent));
            items.Add(new PowerShellBoundNativeCollectionItem(PowerShellSourceParser.GetSpan(document, statement.Extent),
                sourceText, value, PowerShellNativeStatementStatusPolicy.NeedsSuccessWrite(statement, windowsPowerShell), !discard, command is null));
        }
        return new PowerShellBoundNativeCollectionExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
            document.Path, items.ToArray(), !windowsPowerShell, singlePureExpression, collapseResult);
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
                case StatementBlockAst { Statements.Count: 1 } block when block.Traps is not { Count: > 0 }:
                    syntax = block.Statements[0]; continue;
                case PipelineAst { PipelineElements.Count: 1 } pipeline: syntax = pipeline.PipelineElements[0]; continue;
                case CommandExpressionAst command: syntax = command.Expression; continue;
                case ParenExpressionAst parentheses: syntax = parentheses.Pipeline; continue;
                case ArrayExpressionAst { SubExpression.Statements.Count: 1 } array when array.SubExpression.Traps is not { Count: > 0 }:
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
