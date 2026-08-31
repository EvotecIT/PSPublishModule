using System.Collections;
using System.Collections.Specialized;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns bounded dictionary construction, lookup, and indexed mutation semantics.</summary>
internal static class PowerShellDictionarySemanticBinder
{
    internal static PowerShellBoundExpression? BindLiteral(
        ParsedSourceDocument document,
        HashtableAst syntax,
        bool ordered,
        Type? contextualType,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var objectValues = UsesObjectRepresentation(syntax, contextualType);
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
            var value = bindExpression(valueSyntax, objectValues ? null : typeof(string));
            if (key is null || value is null) return null;
            if (key.Type.ClrType != typeof(string) || !objectValues && value.Type.ClrType != typeof(string))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2702", "Typed dictionary literals require String keys; the homogeneous String representation also requires String values.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
                return null;
            }
            entries.Add(new PowerShellBoundDictionaryEntry(key, value));
        }

        var type = ordered
            ? typeof(OrderedDictionary)
            : objectValues ? typeof(Hashtable) : typeof(Dictionary<string, string>);
        var kind = (ordered, objectValues) switch
        {
            (true, true) => PowerShellBoundDictionaryKind.OrderedObjectDictionary,
            (true, false) => PowerShellBoundDictionaryKind.OrderedStringDictionary,
            (false, true) => PowerShellBoundDictionaryKind.ObjectDictionary,
            _ => PowerShellBoundDictionaryKind.StringDictionary
        };
        return new PowerShellBoundDictionaryExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            CreateLiteralTypeFact(type, objectValues, entries),
            kind,
            entries.ToArray());
    }

    internal static PowerShellBoundExpression? BindIndex(
        ParsedSourceDocument document,
        IndexExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
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
        if (target is null || !TryClassify(target.Type, out var kind, out var indexType, out var resultType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2704", "Typed indexing supports strings, one-dimensional arrays, IList values, homogeneous string dictionaries, and IDictionary values.", PowerShellSourceParser.GetSpan(document, syntax.Target.Extent)));
            return null;
        }
        if (target.Type.ClrType.IsArray && target.Type.ClrType.GetArrayRank() != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2705", "Typed indexing supports one-dimensional CLR arrays only.", target.Span));
            return null;
        }
        if (!IsSideEffectFreeIndex(syntax.Index))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2707", "Typed indexing requires a side-effect-free variable or constant index.", PowerShellSourceParser.GetSpan(document, syntax.Index.Extent)));
            return null;
        }
        var index = bindExpression(syntax.Index, indexType);
        if (index is null) return null;
        if (indexType != typeof(object) && index.Type.ClrType != indexType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2706", $"Typed indexing requires one scalar {indexType.Name} index for this target.", index.Span));
            return null;
        }
        var usePowerShellRuntimeErrors = (kind is PowerShellBoundIndexKind.Array or PowerShellBoundIndexKind.List) && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects);
        if ((kind is PowerShellBoundIndexKind.Array or PowerShellBoundIndexKind.List) && IsInsideTypeDiscriminatingTry(syntax) && !usePowerShellRuntimeErrors)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2708", "Array indexing inside a typed try/catch cannot preserve PowerShell runtime-error identity without a PowerShell host.", target.Span));
            return null;
        }
        return new PowerShellBoundIndexExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            target,
            index,
            kind,
            usePowerShellRuntimeErrors,
            new PowerShellTypeFact(resultType, PowerShellTypeFactProvenance.Inferred, "The indexed target selects one conservative missing-value contract."));
    }

    internal static PowerShellBoundIndexAssignmentStatement? BindAssignment(
        ParsedSourceDocument document,
        AssignmentStatementAst syntax,
        IndexExpressionAst indexSyntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.Operator.ToString() != "Equals" || indexSyntax.Target is not VariableExpressionAst)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2710", "Typed indexed mutation requires simple '=' assignment to a local or parameter target.", span));
            return null;
        }
        var target = bindExpression(indexSyntax.Target, null);
        if (target is PowerShellBoundRuntimeStateExpression { Kind: PowerShellRuntimeStateIntrinsicKind.ErrorCollection })
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2713", "The bounded $Error collection is a read-only invocation snapshot and cannot be mutated.", target.Span));
            return null;
        }
        if (target is null || !TryClassify(target.Type, out var kind, out var indexType, out _) || kind == PowerShellBoundIndexKind.String)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2711", "Typed indexed mutation requires a one-dimensional array, IList, or dictionary target.", PowerShellSourceParser.GetSpan(document, indexSyntax.Target.Extent)));
            return null;
        }
        if (target.Type.ClrType.IsArray && target.Type.ClrType.GetArrayRank() != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2705", "Typed indexing supports one-dimensional CLR arrays only.", target.Span));
            return null;
        }
        if (!IsSideEffectFreeIndex(indexSyntax.Index))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2707", "Typed indexing requires a side-effect-free variable or constant index.", PowerShellSourceParser.GetSpan(document, indexSyntax.Index.Extent)));
            return null;
        }
        var index = bindExpression(indexSyntax.Index, indexType);
        if (index is null || indexType != typeof(object) && index.Type.ClrType != indexType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2706", $"Typed indexing requires one scalar {indexType.Name} index for this target.", index?.Span ?? span));
            return null;
        }
        var valueType = kind == PowerShellBoundIndexKind.Array
            ? target.Type.ClrType.GetElementType()!
            : kind == PowerShellBoundIndexKind.List ? typeof(object)
            : kind is PowerShellBoundIndexKind.StringDictionary or PowerShellBoundIndexKind.OrderedStringDictionary ? typeof(string) : typeof(object);
        var value = bindExpression(syntax.Right, valueType);
        if (value is null || valueType != typeof(object) && !PowerShellClrTypeSemantics.CanAssign(valueType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2712", $"Indexed assignment value must be assignable to '{valueType.FullName}'.", value?.Span ?? span));
            return null;
        }
        var usePowerShellRuntimeErrors = (kind is PowerShellBoundIndexKind.Array or PowerShellBoundIndexKind.List) && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects);
        if ((kind is PowerShellBoundIndexKind.Array or PowerShellBoundIndexKind.List) && IsInsideTypeDiscriminatingTry(syntax) && !usePowerShellRuntimeErrors)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2708", "Array mutation inside a typed try/catch cannot preserve PowerShell runtime-error identity without a PowerShell host.", target.Span));
            return null;
        }
        return new PowerShellBoundIndexAssignmentStatement(span, target, index, value, kind, usePowerShellRuntimeErrors);
    }

    internal static bool IsOrderedHashtableConversion(ConvertExpressionAst syntax)
        => syntax.StaticType == typeof(OrderedDictionary) && syntax.Child is HashtableAst;

    internal static bool UsesObjectRepresentation(
        HashtableAst syntax,
        Type? contextualType)
    {
        if (contextualType == typeof(Hashtable) || contextualType == typeof(IDictionary)) return true;
        return syntax.KeyValuePairs.Any(static pair => GetValueExpression(pair.Item2) is not StringConstantExpressionAst);
    }

    internal static PowerShellTypeFact InferLiteralType(
        HashtableAst syntax,
        bool ordered,
        Type? contextualType,
        PowerShellTypeFactProvenance provenance)
    {
        var objectValues = UsesObjectRepresentation(syntax, contextualType);
        var dictionaryType = ordered
            ? typeof(OrderedDictionary)
            : objectValues ? typeof(Hashtable) : typeof(Dictionary<string, string>);
        var properties = new Dictionary<string, PowerShellTypeFact>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in syntax.KeyValuePairs)
        {
            if (pair.Item1 is not StringConstantExpressionAst key || GetValueExpression(pair.Item2) is not { } value) continue;
            properties[key.Value] = InferValueType(value);
        }
        return new PowerShellTypeFact(
            dictionaryType,
            provenance,
            objectValues
                ? "A bounded dictionary literal selects a case-insensitive BCL object dictionary representation."
                : "A homogeneous dictionary literal selects a case-insensitive CLR String dictionary representation.",
            properties,
            objectValues ? PowerShellDictionaryValueKind.Object : PowerShellDictionaryValueKind.String);
    }

    private static bool TryClassify(
        PowerShellTypeFact typeFact,
        out PowerShellBoundIndexKind kind,
        out Type indexType,
        out Type resultType)
    {
        var type = typeFact.ClrType;
        if (type == typeof(string))
        {
            kind = PowerShellBoundIndexKind.String; indexType = typeof(int); resultType = typeof(object); return true;
        }
        if (type.IsArray)
        {
            kind = PowerShellBoundIndexKind.Array; indexType = typeof(int); resultType = typeof(object); return true;
        }
        if (typeof(IList).IsAssignableFrom(type))
        {
            kind = PowerShellBoundIndexKind.List; indexType = typeof(int); resultType = typeof(object); return true;
        }
        if (type == typeof(Dictionary<string, string>))
        {
            kind = PowerShellBoundIndexKind.StringDictionary; indexType = typeof(string); resultType = typeof(string); return true;
        }
        if (type == typeof(OrderedDictionary))
        {
            if (typeFact.DictionaryValueKind != PowerShellDictionaryValueKind.String)
            {
                kind = PowerShellBoundIndexKind.ObjectDictionary; indexType = typeof(object); resultType = typeof(object); return true;
            }
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

    private static PowerShellTypeFact CreateLiteralTypeFact(
        Type dictionaryType,
        bool objectValues,
        IEnumerable<PowerShellBoundDictionaryEntry> entries)
    {
        var properties = new Dictionary<string, PowerShellTypeFact>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Key is PowerShellBoundLiteralExpression { Value: string key }) properties[key] = entry.Value.Type;
        }
        return new PowerShellTypeFact(
            dictionaryType,
            PowerShellTypeFactProvenance.Inferred,
            objectValues
                ? "A bounded dictionary literal selects a case-insensitive BCL object dictionary representation."
                : "A homogeneous dictionary literal selects a case-insensitive CLR String dictionary representation.",
            properties,
            objectValues ? PowerShellDictionaryValueKind.Object : PowerShellDictionaryValueKind.String);
    }

    private static PowerShellTypeFact InferValueType(ExpressionAst expression)
    {
        if (expression is StringConstantExpressionAst)
            return new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Literal, "The dictionary value is a String literal.");
        if (expression is ConstantExpressionAst constant)
            return new PowerShellTypeFact(constant.Value?.GetType() ?? typeof(object), PowerShellTypeFactProvenance.Literal, "The dictionary value is a scalar literal.");
        if (expression is VariableExpressionAst variable &&
            (variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase)))
            return new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Literal, "The dictionary value is a Boolean literal.");
        if (expression.StaticType != typeof(object))
            return new PowerShellTypeFact(expression.StaticType, PowerShellTypeFactProvenance.Inferred, "The dictionary value syntax provides one static CLR type.");
        return PowerShellTypeFact.Unknown;
    }

    private static bool IsSideEffectFreeIndex(ExpressionAst index)
        => index is VariableExpressionAst or ConstantExpressionAst or StringConstantExpressionAst ||
           index is UnaryExpressionAst { Child: ConstantExpressionAst } unary && unary.TokenKind.ToString() is "Plus" or "Minus";

    private static bool IsDirectReturnValue(IndexExpressionAst index)
    {
        Ast current = index;
        while (current.Parent is CommandExpressionAst or ParenExpressionAst or PipelineAst or ArrayLiteralAst or ArrayExpressionAst or StatementBlockAst) current = current.Parent;
        return current.Parent is ReturnStatementAst or AssignmentStatementAst;
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
