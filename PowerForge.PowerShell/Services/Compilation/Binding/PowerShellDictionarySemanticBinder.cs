using System.Collections;
using System.Collections.Specialized;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns homogeneous dictionary construction, lookup, and indexed mutation semantics.</summary>
internal static class PowerShellDictionarySemanticBinder
{
    internal static PowerShellBoundExpression? BindLiteral(
        ParsedSourceDocument document,
        HashtableAst syntax,
        bool ordered,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var entries = new List<PowerShellBoundDictionaryEntry>();
        foreach (var pair in syntax.KeyValuePairs)
        {
            var valueSyntax = GetValueExpression(pair.Item2);
            if (valueSyntax is null)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2701", "Typed dictionary values must be one side-effect-free scalar expression.", PowerShellSourceParser.GetSpan(document, pair.Item2.Extent)));
                return null;
            }
            var key = bindExpression(pair.Item1, typeof(string));
            var value = bindExpression(valueSyntax, typeof(string));
            if (key is null || value is null) return null;
            if (key.Type.ClrType != typeof(string) || value.Type.ClrType != typeof(string))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2702", "Typed dictionary literals require homogeneous String keys and String values.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
                return null;
            }
            entries.Add(new PowerShellBoundDictionaryEntry(key, value));
        }

        var type = ordered ? typeof(OrderedDictionary) : typeof(Dictionary<string, string>);
        return new PowerShellBoundDictionaryExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            type,
            ordered ? PowerShellBoundDictionaryKind.OrderedStringDictionary : PowerShellBoundDictionaryKind.StringDictionary,
            entries.ToArray());
    }

    internal static PowerShellBoundExpression? BindIndex(
        ParsedSourceDocument document,
        IndexExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (!IsDirectReturnValue(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2703", "Typed indexing is value-producing only as a direct return; indexed mutation has a separate statement contract.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        if (syntax.Target is not VariableExpressionAst and not StringConstantExpressionAst and not ArrayLiteralAst)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2709", "Typed indexing requires a side-effect-free local, parameter, string literal, or array literal target.", PowerShellSourceParser.GetSpan(document, syntax.Target.Extent)));
            return null;
        }
        var target = bindExpression(syntax.Target, null);
        if (target is null || !TryClassify(target.Type.ClrType, out var kind, out var indexType, out var resultType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2704", "Typed indexing supports strings, one-dimensional arrays, homogeneous string dictionaries, and IDictionary values.", PowerShellSourceParser.GetSpan(document, syntax.Target.Extent)));
            return null;
        }
        if (target.Type.ClrType.IsArray && target.Type.ClrType.GetArrayRank() != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2705", "Typed indexing supports one-dimensional CLR arrays only.", target.Span));
            return null;
        }
        var index = bindExpression(syntax.Index, indexType);
        if (index is null) return null;
        if (indexType != typeof(object) && index.Type.ClrType != indexType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2706", $"Typed indexing requires one scalar {indexType.Name} index for this target.", index.Span));
            return null;
        }
        if (!IsSideEffectFreeIndex(syntax.Index))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2707", "Typed indexing requires a side-effect-free variable or constant index.", index.Span));
            return null;
        }
        if (kind == PowerShellBoundIndexKind.Array && IsInsideTypeDiscriminatingTry(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2708", "Array indexing inside a typed try/catch cannot preserve PowerShell runtime-error identity without a PowerShell host.", target.Span));
            return null;
        }
        return new PowerShellBoundIndexExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            target,
            index,
            kind,
            new PowerShellTypeFact(resultType, PowerShellTypeFactProvenance.Inferred, "The indexed target selects one conservative missing-value contract."));
    }

    internal static PowerShellBoundIndexAssignmentStatement? BindAssignment(
        ParsedSourceDocument document,
        AssignmentStatementAst syntax,
        IndexExpressionAst indexSyntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.Operator.ToString() != "Equals" || indexSyntax.Target is not VariableExpressionAst)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2710", "Typed indexed mutation requires simple '=' assignment to a local or parameter target.", span));
            return null;
        }
        var target = bindExpression(indexSyntax.Target, null);
        if (target is null || !TryClassify(target.Type.ClrType, out var kind, out var indexType, out _) || kind == PowerShellBoundIndexKind.String)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2711", "Typed indexed mutation requires a one-dimensional array or dictionary target.", PowerShellSourceParser.GetSpan(document, indexSyntax.Target.Extent)));
            return null;
        }
        if (target.Type.ClrType.IsArray && target.Type.ClrType.GetArrayRank() != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2705", "Typed indexing supports one-dimensional CLR arrays only.", target.Span));
            return null;
        }
        var index = bindExpression(indexSyntax.Index, indexType);
        if (index is null || indexType != typeof(object) && index.Type.ClrType != indexType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2706", $"Typed indexing requires one scalar {indexType.Name} index for this target.", index?.Span ?? span));
            return null;
        }
        if (!IsSideEffectFreeIndex(indexSyntax.Index))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2707", "Typed indexing requires a side-effect-free variable or constant index.", index.Span));
            return null;
        }
        var valueType = kind == PowerShellBoundIndexKind.Array
            ? target.Type.ClrType.GetElementType()!
            : kind is PowerShellBoundIndexKind.StringDictionary or PowerShellBoundIndexKind.OrderedStringDictionary ? typeof(string) : typeof(object);
        var value = bindExpression(syntax.Right, valueType);
        if (value is null || valueType != typeof(object) && !PowerShellClrTypeSemantics.CanAssign(valueType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2712", $"Indexed assignment value must be assignable to '{valueType.FullName}'.", value?.Span ?? span));
            return null;
        }
        if (kind == PowerShellBoundIndexKind.Array && IsInsideTypeDiscriminatingTry(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2708", "Array mutation inside a typed try/catch cannot preserve PowerShell runtime-error identity without a PowerShell host.", target.Span));
            return null;
        }
        return new PowerShellBoundIndexAssignmentStatement(span, target, index, value, kind);
    }

    internal static bool IsOrderedHashtableConversion(ConvertExpressionAst syntax)
        => syntax.StaticType == typeof(OrderedDictionary) && syntax.Child is HashtableAst;

    private static bool TryClassify(Type type, out PowerShellBoundIndexKind kind, out Type indexType, out Type resultType)
    {
        if (type == typeof(string))
        {
            kind = PowerShellBoundIndexKind.String; indexType = typeof(int); resultType = typeof(object); return true;
        }
        if (type.IsArray)
        {
            kind = PowerShellBoundIndexKind.Array; indexType = typeof(int); resultType = typeof(object); return true;
        }
        if (type == typeof(Dictionary<string, string>))
        {
            kind = PowerShellBoundIndexKind.StringDictionary; indexType = typeof(string); resultType = typeof(string); return true;
        }
        if (type == typeof(OrderedDictionary))
        {
            kind = PowerShellBoundIndexKind.OrderedStringDictionary; indexType = typeof(string); resultType = typeof(string); return true;
        }
        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            kind = PowerShellBoundIndexKind.ObjectDictionary; indexType = typeof(object); resultType = typeof(object); return true;
        }
        kind = default; indexType = typeof(object); resultType = typeof(object); return false;
    }

    private static ExpressionAst? GetValueExpression(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline && pipeline.PipelineElements[0] is CommandExpressionAst expression
            ? expression.Expression
            : null;

    private static bool IsSideEffectFreeIndex(ExpressionAst index)
        => index is VariableExpressionAst or ConstantExpressionAst or StringConstantExpressionAst ||
           index is UnaryExpressionAst { Child: ConstantExpressionAst } unary && unary.TokenKind.ToString() is "Plus" or "Minus";

    private static bool IsDirectReturnValue(IndexExpressionAst index)
    {
        Ast current = index;
        while (current.Parent is CommandExpressionAst or ParenExpressionAst or PipelineAst) current = current.Parent;
        return current.Parent is ReturnStatementAst;
    }

    private static bool IsInsideTypeDiscriminatingTry(Ast node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TryStatementAst tryStatement && tryStatement.CatchClauses.Any(static clause => clause.CatchTypes.Count > 0)) return true;
        }
        return false;
    }
}
