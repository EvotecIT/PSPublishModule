using System.Management.Automation.Language;

namespace PowerForge;

internal sealed class PowerShellSemanticSymbolBinding
{
    internal PowerShellSemanticSymbolBinding(PowerShellSymbolId symbol, PowerShellTypeFact type)
    {
        Symbol = symbol;
        Type = type;
        ValueState = PowerShellValueState.Unknown;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellTypeFact Type { get; private set; }
    internal PowerShellValueState ValueState { get; private set; }
    internal bool IsModuleStateDerived { get; private set; }
    internal bool IsBraceFreeString { get; private set; }

    internal void SetInt32Range(PowerShellInt32Range range) => Type = Type.WithInt32Range(range);

    internal void Refine(PowerShellTypeFact type, PowerShellValueState valueState, bool isBraceFreeString = false)
    {
        if (Type.Provenance == PowerShellTypeFactProvenance.Unknown) Type = type;
        else if (Type.ClrType.IsAssignableFrom(type.ClrType) &&
                 type.DictionaryValueKind is PowerShellDictionaryValueKind.String or PowerShellDictionaryValueKind.Object ||
                 Type.ClrType == type.ClrType && type.DictionaryValueKind != PowerShellDictionaryValueKind.None)
            Type = new PowerShellTypeFact(
                Type.ClrType,
                Type.Provenance,
                Type.Explanation,
                type.KnownProperties,
                type.DictionaryValueKind);
        else if (Type.DictionaryValueKind is PowerShellDictionaryValueKind.String or PowerShellDictionaryValueKind.Object)
            Type = new PowerShellTypeFact(Type.ClrType, Type.Provenance,
                "The assigned value does not preserve the preceding dictionary shape.");
        ValueState = valueState;
        SetStringContent(isBraceFreeString);
    }

    internal void SetStringContent(bool isBraceFreeString)
        => IsBraceFreeString = Type.ClrType == typeof(string) && isBraceFreeString;

    internal void AddKnownProperty(string name, PowerShellTypeFact type)
        => Type = Type.WithKnownProperty(name, type);

    internal PowerShellSemanticSymbolBinding Clone()
    {
        var clone = new PowerShellSemanticSymbolBinding(Symbol, Type);
        clone.ValueState = ValueState;
        clone.IsModuleStateDerived = IsModuleStateDerived;
        clone.IsBraceFreeString = IsBraceFreeString;
        return clone;
    }

    internal void ForgetValueState()
    {
        ValueState = PowerShellValueState.Unknown;
        IsBraceFreeString = false;
    }

    internal void SetModuleStateDerived(bool value) => IsModuleStateDerived = value;

    internal void MergeFlowState(IEnumerable<PowerShellSemanticSymbolBinding> paths)
    {
        var materialized = paths.ToArray();
        if (Type.Provenance == PowerShellTypeFactProvenance.Unknown)
        {
            var concreteTypes = materialized
                .Where(static path => path.Type.Provenance != PowerShellTypeFactProvenance.Unknown)
                .Select(static path => path.Type)
                .GroupBy(static type => type.ClrType)
                .Take(2)
                .ToArray();
            if (concreteTypes.Length == 1) Type = concreteTypes[0].First();
        }
        if (Type.DictionaryValueKind != PowerShellDictionaryValueKind.None &&
            materialized.Any(path => path.Type.DictionaryValueKind != Type.DictionaryValueKind))
            Type = new PowerShellTypeFact(Type.ClrType, Type.Provenance,
                "Control-flow paths preserve dictionary storage but do not share one narrowed value contract.",
                dictionaryValueKind: PowerShellDictionaryValueKind.Object);
        var states = materialized.Select(static path => path.ValueState).Distinct().Take(2).ToArray();
        ValueState = states.Length == 1 ? states[0] : PowerShellValueState.Unknown;
        IsModuleStateDerived = materialized.Any(static path => path.IsModuleStateDerived);
        IsBraceFreeString = materialized.Length > 0 && materialized.All(static path => path.IsBraceFreeString);
    }
}

/// <summary>Owns local and parameter mutation semantics.</summary>
internal static partial class PowerShellMutationSemanticBinder
{
    internal static PowerShellBoundMutationExpression? BindAssignment(
        ParsedSourceDocument document,
        AssignmentStatementAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(syntax.Left, capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding));
        if (variable is null || !symbols.TryGetValue(variable.VariablePath.UserPath, out var target))
        {
            if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2417", "Native assignment target has no bound storage symbol: " + variable?.VariablePath.UserPath,
                    PowerShellSourceParser.GetSpan(document, syntax.Left.Extent)));
            return null;
        }
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
            return BindNativeAssignment(document, syntax, variable, target, bindExpression, diagnostics);
        if (PowerShellClosedValueAlternativePolicy.TryGetAssignmentType(
                target.Type, syntax, out var closedAlternativeType))
        {
            if (!syntax.Operator.ToString().Equals("Equals", StringComparison.Ordinal)) return null;
            var alternativeValue = bindExpression(syntax.Right, closedAlternativeType);
            if (alternativeValue is null || alternativeValue.Type.ClrType != closedAlternativeType ||
                !PowerShellClosedValueAlternativePolicy.TryGetAlternativeIndex(
                    target.Type, syntax.Left.Extent.Text, alternativeValue.Type.ClrType, out var alternativeIndex))
                return null;
            var envelope = new PowerShellBoundRegionValueAlternativeExpression(
                PowerShellSourceParser.GetSpan(document, syntax.Right.Extent),
                alternativeIndex,
                alternativeValue,
                target.Type);
            return new PowerShellBoundMutationExpression(
                PowerShellSourceParser.GetSpan(document, syntax.Extent),
                target.Symbol,
                target.Type.ClrType,
                PowerShellBoundMutationOperator.Assign,
                envelope,
                target.Type,
                normalizeNullString: false,
                PowerShellIntegralMutationSemantics.None);
        }
        if (!PowerShellAssignmentTargetPolicy.PreservesConstraint(syntax.Left, target.Type))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2414",
                "Adding or changing a variable type constraint after its first value requires a separate constraint-transition contract.",
                PowerShellSourceParser.GetSpan(document, syntax.Left.Extent)));
            return null;
        }
        var operation = syntax.Operator.ToString() switch
        {
            "Equals" => PowerShellBoundMutationOperator.Assign,
            "PlusEquals" => PowerShellBoundMutationOperator.Add,
            "MinusEquals" => PowerShellBoundMutationOperator.Subtract,
            "MultiplyEquals" => PowerShellBoundMutationOperator.Multiply,
            "DivideEquals" => PowerShellBoundMutationOperator.Divide,
            "RemainderEquals" => PowerShellBoundMutationOperator.Remainder,
            _ => (PowerShellBoundMutationOperator?)null
        };
        if (operation is null) return null;
        var targetType = target.Type.ClrType;
        // An inferred storage type is not an authored Hashtable/IDictionary constraint.
        var contextualType = target.Type.Provenance == PowerShellTypeFactProvenance.Unknown ||
            target.Type.Provenance != PowerShellTypeFactProvenance.Explicit &&
            typeof(System.Collections.IDictionary).IsAssignableFrom(targetType) ? null : targetType;
        var value = bindExpression(syntax.Right, contextualType);
        if (value is null) return null;
        if (operation == PowerShellBoundMutationOperator.Assign && target.Type.Provenance == PowerShellTypeFactProvenance.Explicit &&
            PowerShellConversionSemanticBinder.BindClosedNumericConversion(value, targetType, capabilities) is { } convertedNumeric)
            value = convertedNumeric;
        if (target.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble)
        {
            var nonzeroDivision = operation is PowerShellBoundMutationOperator.Divide or PowerShellBoundMutationOperator.Remainder &&
                PowerShellNumericUnionPolicy.IsNonzeroInteger(value);
            if (syntax.Left is not VariableExpressionAst || !PowerShellNumericUnionPolicy.IsNumeric(value.Type) ||
                (!nonzeroDivision && operation is not (PowerShellBoundMutationOperator.Assign or PowerShellBoundMutationOperator.Add or
                    PowerShellBoundMutationOperator.Subtract or PowerShellBoundMutationOperator.Multiply)))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2413",
                    "This unconstrained numeric local requires Int32/Double assignment, additive/multiplicative mutation, or division/remainder by a proven nonzero Int32; adding an authored variable constraint requires a separate constraint-transition contract.",
                    PowerShellSourceParser.GetSpan(document, syntax.Extent)));
                return null;
            }
            target.Refine(target.Type, PowerShellValueState.Known);
            target.SetModuleStateDerived(operation == PowerShellBoundMutationOperator.Assign
                ? PowerShellModuleStateOriginPolicy.IsDerived(value)
                : target.IsModuleStateDerived || PowerShellModuleStateOriginPolicy.IsDerived(value));
            return new PowerShellBoundMutationExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
                target.Symbol, typeof(object), operation.Value, value, target.Type, false,
                operation == PowerShellBoundMutationOperator.Assign ? PowerShellIntegralMutationSemantics.None :
                    PowerShellIntegralMutationSemantics.UnconstrainedInt32OrDouble);
        }
        if (value.Type.ClrType == typeof(void))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2406", "A void CLR invocation or output-free mutation cannot be assigned to a PowerShell value.", PowerShellSourceParser.GetSpan(document, syntax.Right.Extent)));
            return null;
        }
        if (operation == PowerShellBoundMutationOperator.Assign)
        {
            target.Refine(
                new PowerShellTypeFact(
                    value.Type.ClrType,
                    PowerShellTypeFactProvenance.Inferred,
                    $"The first bound assignment to '${target.Symbol.Name}' provides a stable CLR representation.",
                    value.Type.KnownProperties,
                    value.Type.DictionaryValueKind),
                value.ValueState,
                PowerShellStringContentPolicy.IsBraceFree(value));
            targetType = target.Type.ClrType;
            target.SetModuleStateDerived(PowerShellModuleStateOriginPolicy.IsDerived(value));
        }
        else
        {
            target.SetStringContent(operation == PowerShellBoundMutationOperator.Add &&
                target.IsBraceFreeString && PowerShellStringContentPolicy.IsBraceFree(value));
            if (PowerShellModuleStateOriginPolicy.IsDerived(value)) target.SetModuleStateDerived(true);
        }
        if (operation == PowerShellBoundMutationOperator.Assign &&
            target.Type.Provenance == PowerShellTypeFactProvenance.Inferred &&
            targetType != value.Type.ClrType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2408", $"Assignment changes inferred local '${target.Symbol.Name}' from CLR type '{targetType.FullName}' to '{value.Type.ClrType.FullName}'. Inferred locals require one exact CLR representation.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        if (operation == PowerShellBoundMutationOperator.Assign && !PowerShellClrTypeSemantics.CanAssign(targetType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2401", $"Assignment requires PowerShell conversion from '{value.Type.ClrType.FullName}' to '{targetType.FullName}', which is not an implicit CLR conversion.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        if (operation != PowerShellBoundMutationOperator.Assign &&
            !PowerShellCSharpOperatorPolicy.SupportsCompoundAssignment(syntax.Operator.ToString(), targetType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2402", $"Compound assignment '{syntax.Operator}' is not defined for CLR types '{targetType.FullName}' and '{value.Type.ClrType.FullName}' on the conservative compilation path.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        var explicitType = target.Type.Provenance == PowerShellTypeFactProvenance.Explicit;
        if (operation != PowerShellBoundMutationOperator.Assign && targetType == typeof(float) && !explicitType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2410", "Compound arithmetic on an unconstrained Single local produces a Double result and changes its CLR representation.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        var preserveNumericErrors = capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors) &&
            operation != PowerShellBoundMutationOperator.Assign &&
            (PowerShellClrTypeSemantics.IsIntegral(targetType) || targetType == typeof(decimal));
        if (!preserveNumericErrors && operation != PowerShellBoundMutationOperator.Assign &&
            (PowerShellClrTypeSemantics.IsIntegral(targetType) || targetType == typeof(decimal)) &&
            PowerShellRuntimeExceptionCatchPolicy.ContainsNumericErrorWrapping(syntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2409", "Numeric compound assignment inside a PowerShell-specific typed catch cannot preserve PowerShell error wrapping.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        if (operation != PowerShellBoundMutationOperator.Assign && PowerShellClrTypeSemantics.IsIntegral(targetType) && !explicitType)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2403", $"Integral compound assignment to untyped local '${target.Symbol.Name}' can promote dynamically in PowerShell and is not eligible for typed compilation.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return null;
        }
        return new PowerShellBoundMutationExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            target.Symbol,
            targetType,
            operation.Value,
            value,
            target.Type,
            operation == PowerShellBoundMutationOperator.Assign && explicitType && targetType == typeof(string),
            SelectIntegralSemantics(operation.Value, targetType, value.Type.ClrType), preserveNumericErrors);
    }

    internal static bool TryBindIncrement(
        ParsedSourceDocument document,
        UnaryExpressionAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        out PowerShellBoundMutationExpression? mutation,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        mutation = null;
        var operation = syntax.TokenKind.ToString() switch
        {
            "PlusPlus" => PowerShellBoundMutationOperator.Increment,
            "MinusMinus" => PowerShellBoundMutationOperator.Decrement,
            "PostfixPlusPlus" => PowerShellBoundMutationOperator.PostIncrement,
            "PostfixMinusMinus" => PowerShellBoundMutationOperator.PostDecrement,
            _ => (PowerShellBoundMutationOperator?)null
        };
        if (operation is null) return false;
        var standalone = IsStandaloneStatement(syntax);
        if (!standalone && !capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2407", "Value-producing increment and decrement contexts require PowerShell expression-result semantics.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return true;
        }
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
            PowerShellSemanticBinder.NativeAccessMutationReceiver(syntax.Child) is { } receiver)
        {
            if (!symbols.TryGetValue(receiver.VariablePath.UserPath, out var root)) return false;
            mutation = new PowerShellBoundMutationExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
                root.Symbol, typeof(object), operation.Value, null,
                standalone ? new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred,
                    "Standalone native access mutation does not emit a success record.") : PowerShellTypeFact.Unknown,
                false, PowerShellIntegralMutationSemantics.None,
                nativeTargetRead: PowerShellNativeFunctionBindingPolicy.BindVariable(document, receiver),
                nativeSourceText: PowerShellNativeFunctionBindingPolicy.SourceLines(document, PowerShellSourceParser.GetSpan(document, syntax.Extent)),
                nativeSetSequencePoint: standalone,
                nativeAssignmentTarget: new PowerShellNativeAssignmentTarget(syntax.Child.Extent.Text,
                    document.Path, document.Text, PowerShellSourceParser.GetSpan(document, syntax.Child.Extent),
                    syntax.Child.Extent.StartOffset, syntax.Child.Extent.EndOffset,
                    MutatesReceiver: true, ReadVariables: syntax.Child.FindAll(static node => node is VariableExpressionAst, false)
                        .Cast<VariableExpressionAst>().Select(static variable => variable.VariablePath.UserPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            return true;
        }
        var operand = UnwrapExpression(syntax.Child) as VariableExpressionAst;
        if (operand is null || !symbols.TryGetValue(operand.VariablePath.UserPath, out var target)) return false;
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
        {
            mutation = new PowerShellBoundMutationExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
                target.Symbol, typeof(object), operation.Value, null,
                standalone ? new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred,
                    "Standalone native mutation writes its invocation variable without emitting output.") : PowerShellTypeFact.Unknown,
                false, PowerShellIntegralMutationSemantics.None,
                nativeTargetRead: PowerShellNativeFunctionBindingPolicy.BindVariable(document, operand),
                nativeSourceText: PowerShellNativeFunctionBindingPolicy.SourceLines(document, PowerShellSourceParser.GetSpan(document, syntax.Extent)),
                nativeSetSequencePoint: standalone);
            return true;
        }
        if (target.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble)
        {
            target.Refine(target.Type, PowerShellValueState.Known);
            mutation = new PowerShellBoundMutationExpression(PowerShellSourceParser.GetSpan(document, syntax.Extent),
                target.Symbol, typeof(object), operation.Value, null,
                new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "A standalone numeric increment changes the value without emitting a record."),
                false, PowerShellIntegralMutationSemantics.UnconstrainedInt32OrDouble);
            return true;
        }
        var boundedDecrement = PowerShellInt32RangePolicy.CanDecrement(target, operation.Value);
        if (!PowerShellCSharpOperatorPolicy.SupportsIncrement(target.Type.ClrType) ||
            target.Type.Provenance != PowerShellTypeFactProvenance.Explicit && !boundedDecrement)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2404", $"Increment or decrement of '${target.Symbol.Name}' requires one explicitly typed supported CLR representation.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return true;
        }
        var preserveNumericErrors = !boundedDecrement && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors) &&
            (PowerShellClrTypeSemantics.IsIntegral(target.Type.ClrType) || target.Type.ClrType == typeof(decimal));
        if (!preserveNumericErrors && PowerShellRuntimeExceptionCatchPolicy.ContainsNumericErrorWrapping(syntax) &&
            (PowerShellClrTypeSemantics.IsIntegral(target.Type.ClrType) || target.Type.ClrType == typeof(decimal)))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2409", "Integral increment or decrement inside a RuntimeException catch cannot preserve PowerShell overflow-error wrapping.", PowerShellSourceParser.GetSpan(document, syntax.Extent)));
            return true;
        }
        mutation = new PowerShellBoundMutationExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            target.Symbol,
            target.Type.ClrType,
            operation.Value,
            null,
            new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "Increment and decrement are statement-valued on the conservative path."),
            false,
            boundedDecrement ? PowerShellIntegralMutationSemantics.None : SelectIntegralSemantics(operation.Value, target.Type.ClrType, target.Type.ClrType),
            preserveNumericErrors);
        return true;
    }

    private static PowerShellIntegralMutationSemantics SelectIntegralSemantics(
        PowerShellBoundMutationOperator operation, Type target, Type right)
    {
        if (operation == PowerShellBoundMutationOperator.Assign || !PowerShellClrTypeSemantics.IsIntegral(target))
            return PowerShellIntegralMutationSemantics.None;
        // Native decrement adds a signed -1. UInt32 therefore reaches a signed
        // Int64 result and UInt64 a Decimal result before its variable conversion.
        if (operation is PowerShellBoundMutationOperator.Decrement or PowerShellBoundMutationOperator.PostDecrement &&
            (target == typeof(uint) || target == typeof(ulong)))
            return PowerShellIntegralMutationSemantics.UnsignedDecrement;
        // PowerShell's 64-bit product promotes through BigInteger. Its conversion
        // to Double differs from Decimal rounding near Int64's negative boundary.
        return operation == PowerShellBoundMutationOperator.Multiply &&
            (target == typeof(long) || target == typeof(ulong) || right == typeof(long) || right == typeof(ulong))
            ? PowerShellIntegralMutationSemantics.PromotedBigIntegerProduct
            : PowerShellIntegralMutationSemantics.CheckedConversion;
    }

    private static Ast UnwrapExpression(Ast syntax)
    {
        while (syntax is CommandExpressionAst command) syntax = command.Expression;
        while (syntax is ParenExpressionAst parenthesized) syntax = parenthesized.Pipeline;
        return syntax;
    }

    private static bool IsStandaloneStatement(Ast syntax)
    {
        Ast current = syntax;
        while (current.Parent is CommandExpressionAst or PipelineAst) current = current.Parent;
        return current.Parent is NamedBlockAst or StatementBlockAst or ReturnStatementAst ||
               current.Parent is ForStatementAst loop &&
               (ReferenceEquals(loop.Initializer, current) || ReferenceEquals(loop.Iterator, current));
    }
}
