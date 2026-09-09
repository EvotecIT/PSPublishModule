using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundLocal[] DeclareLocals(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticResolver commandResolver,
        int? excludedTailOffset = null)
    {
        var locals = new List<PowerShellBoundLocal>();
        var assignments = GetFunctionStatements(function.Body)
            .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
            .Cast<AssignmentStatementAst>()
            .OrderBy(static assignment => assignment.Extent.StartOffset)
            .ToArray();
        foreach (var assignment in assignments)
        {
            if (excludedTailOffset.HasValue && assignment.Extent.StartOffset >= excludedTailOffset.Value) continue;
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left, capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding));
            if (variable is null) continue;
            var name = variable.VariablePath.UserPath;
            if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
                (PowerShellRuntimeStateIntrinsicPolicy.TryGetModuleVariableAssignmentName(assignment, capabilities, out _) ||
                name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(PowerShellBoundParametersPolicy.VariableName, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (symbols.ContainsKey(name)) continue;
            var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
            var type = ResolveAssignmentType(assignment, functions, capabilities, commandResolver);
            if (type.Provenance == PowerShellTypeFactProvenance.Unknown &&
                UnwrapExpression(assignment.Right) is VariableExpressionAst copied &&
                symbols.TryGetValue(copied.VariablePath.UserPath, out var copiedSymbol) &&
                copiedSymbol.Type.Provenance != PowerShellTypeFactProvenance.Unknown)
                type = copiedSymbol.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble
                    ? PowerShellNumericUnionPolicy.Int32OrDouble
                    : new PowerShellTypeFact(copiedSymbol.Type.ClrType, PowerShellTypeFactProvenance.Inferred,
                        "A copied value preserves its known representation without inheriting the source variable's constraint.",
                        copiedSymbol.Type.KnownProperties, copiedSymbol.Type.DictionaryValueKind);
            if (type.Provenance == PowerShellTypeFactProvenance.Inferred &&
                PowerShellArraySemanticBinder.InferPreservedVectorType(assignment.Right,
                    variableName => symbols.TryGetValue(variableName, out var source) ? source.Type : null,
                    _semanticProfile) is { } preservedArrayType)
                type = new PowerShellTypeFact(preservedArrayType, PowerShellTypeFactProvenance.Inferred,
                    "The selected Windows PowerShell profile preserves this constrained vector through collection syntax.");
            if (type.ClrType == typeof(int) && type.Provenance == PowerShellTypeFactProvenance.Inferred &&
                PowerShellNumericUnionPolicy.RequiresPromotion(function, name, assignments))
                type = PowerShellNumericUnionPolicy.Int32OrDouble;
            if (type.Provenance == PowerShellTypeFactProvenance.Unknown &&
                TryInferNullSeededReferenceType(name, assignment, assignments, functions, capabilities, commandResolver, out var inferred))
                type = inferred;
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/local/" + name);
            var local = new PowerShellBoundLocal(symbol, type);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            locals.Add(local);
        }
        var loops = GetFunctionStatements(function.Body)
            .SelectMany(static statement => statement.FindAll(static node => node is ForEachStatementAst, searchNestedScriptBlocks: false))
            .Cast<ForEachStatementAst>()
            .OrderBy(static loop => loop.Extent.StartOffset);
        foreach (var loop in loops)
        {
            var name = loop.Variable.VariablePath.UserPath;
            if (symbols.ContainsKey(name)) continue;
            var condition = UnwrapExpression(loop.Condition);
            Type? collectionType = condition switch
            {
                VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var binding) => binding.Type.ClrType,
                ExpressionAst expression when expression.StaticType != typeof(object) => expression.StaticType,
                _ => null
            };
            var elementType = PowerShellForEachCollectionPolicy.GetElementType(collectionType, capabilities, out var enumerationKind);
            if (elementType is null) continue;
            var span = PowerShellSourceParser.GetSpan(document, loop.Variable.Extent);
            var type = new PowerShellTypeFact(
                elementType,
                PowerShellTypeFactProvenance.Inferred,
                enumerationKind is PowerShellForEachEnumerationKind.SystemArray or PowerShellForEachEnumerationKind.PowerShellEnumerable or PowerShellForEachEnumerationKind.NativeInvocation
                    ? "The generated PowerShell host preserves collection elements as object-valued foreach items."
                    : "The foreach collection provides one stable CLR element type.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/foreach/" + loop.Extent.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + name);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            locals.Add(new PowerShellBoundLocal(symbol, type));
        }
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
        {
            // A native mutation can read existing session storage or create a variable from
            // a missing value. It does not require an assignment in this function first.
            foreach (var unary in GetFunctionStatements(function.Body)
                         .SelectMany(static statement => statement.FindAll(static node => node is UnaryExpressionAst
                             { TokenKind: TokenKind.PlusPlus or TokenKind.PostfixPlusPlus or TokenKind.MinusMinus or TokenKind.PostfixMinusMinus },
                             searchNestedScriptBlocks: false)).Cast<UnaryExpressionAst>())
            {
                if (excludedTailOffset.HasValue && unary.Extent.StartOffset >= excludedTailOffset.Value) continue;
                if (PowerShellAssignmentTargetPolicy.FindDirectVariable(unary.Child) is not { } variable) continue;
                var name = variable.VariablePath.UserPath;
                if (symbols.ContainsKey(name)) continue;
                var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
                var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/local/" + name);
                symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, PowerShellTypeFact.Unknown));
                locals.Add(new PowerShellBoundLocal(symbol, PowerShellTypeFact.Unknown));
            }
        }
        if (_runtimeFreeModule is not null) locals.AddRange(_runtimeFreeModule.Fields);
        return locals.ToArray();
    }

    private static PowerShellTypeFact ResolveAssignmentType(
        AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticResolver commandResolver)
    {
        var expression = UnwrapExpression(assignment.Right);
        if (PowerShellCommentHelpSemanticBinder.TryInferType(expression, functions, capabilities, out var helpType))
            return helpType;
        if (assignment.Left is ConvertExpressionAst typedDictionary &&
            expression is HashtableAst typedHashtable &&
            (typedDictionary.StaticType == typeof(System.Collections.Hashtable) ||
             typedDictionary.StaticType == typeof(System.Collections.IDictionary)))
            return PowerShellDictionarySemanticBinder.InferLiteralType(
                typedHashtable,
                ordered: false,
                typedDictionary.StaticType,
                PowerShellTypeFactProvenance.Explicit);
        if (assignment.Left is ConvertExpressionAst typedLeft)
            return new PowerShellTypeFact(typedLeft.StaticType, PowerShellTypeFactProvenance.Explicit, "The assignment target has an authored type constraint.");
        if (expression is HashtableAst hashtable)
            return PowerShellDictionarySemanticBinder.InferLiteralType(
                hashtable,
                ordered: false,
                contextualType: null,
                PowerShellTypeFactProvenance.Inferred);
        if (expression is ConvertExpressionAst ordered && PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(ordered))
            return PowerShellDictionarySemanticBinder.InferLiteralType(
                (HashtableAst)ordered.Child,
                ordered: true,
                typeof(System.Collections.Specialized.OrderedDictionary),
                PowerShellTypeFactProvenance.Inferred);
        if (expression is ConvertExpressionAst powerShellObject && PowerShellObjectConstructionPolicy.IsLiteral(powerShellObject))
            return PowerShellObjectSemanticBinder.InferLiteralType(powerShellObject);
        if (expression is ConvertExpressionAst conversion && conversion.StaticType != typeof(object))
            // A cast constrains this value only. Later writes to the untyped local can change
            // its representation; only a type constraint on the assignment target persists.
            return new PowerShellTypeFact(conversion.StaticType, PowerShellTypeFactProvenance.Inferred, "The assignment value has an authored conversion; the local has no type constraint.");
        if (expression is InvokeMemberExpressionAst
            {
                Static: true,
                Expression: TypeExpressionAst constructedType,
                Member: StringConstantExpressionAst { Value: var memberName }
            } &&
            memberName.Equals("new", StringComparison.OrdinalIgnoreCase) &&
            constructedType.TypeName.GetReflectionType() is { } constructorType)
            return new PowerShellTypeFact(constructorType, PowerShellTypeFactProvenance.Inferred, "The assignment invokes one statically named CLR constructor.");
        if (expression is CommandAst runtimeStateCommand &&
            PowerShellRuntimeStateCommandSemanticBinder.TryGetResultType(
                runtimeStateCommand,
                commandResolver,
                functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                capabilities,
                out var runtimeStateType))
            return new PowerShellTypeFact(runtimeStateType, PowerShellTypeFactProvenance.Inferred, "The assignment uses one bounded runtime-state command provider.");
        if (expression is CommandAst command &&
            commandResolver.Resolve(command, functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), capabilities) is
            {
                IsProvider: true,
                Contract.Family: PowerShellCompilationCommandFamily.ClrConstruction
            } &&
            PowerShellNewObjectSemanticBinder.TryGetConstructionShape(command, out var commandType, out _))
            return new PowerShellTypeFact(commandType, PowerShellTypeFactProvenance.Inferred, "The assignment uses one statically named bounded New-Object CLR constructor.");
        if (expression is ExpressionAst typedExpression && typedExpression.StaticType != typeof(object))
            return new PowerShellTypeFact(typedExpression.StaticType, PowerShellTypeFactProvenance.Inferred, "The first assignment provides a static CLR type.");
        return PowerShellTypeFact.Unknown;
    }

    private static bool TryInferNullSeededReferenceType(
        string name,
        AssignmentStatementAst first,
        IReadOnlyList<AssignmentStatementAst> assignments,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticResolver commandResolver,
        out PowerShellTypeFact type)
    {
        type = PowerShellTypeFact.Unknown;
        if (!IsNullAssignment(first)) return false;
        Type? inferredType = null;
        foreach (var assignment in assignments)
        {
            if (assignment.Extent.StartOffset <= first.Extent.StartOffset) continue;
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left, capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding));
            if (variable is null || !variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!assignment.Operator.ToString().Equals("Equals", StringComparison.Ordinal)) return false;
            if (IsNullAssignment(assignment)) continue;
            var candidate = ResolveAssignmentType(assignment, functions, capabilities, commandResolver);
            if (candidate.Provenance == PowerShellTypeFactProvenance.Unknown ||
                candidate.ClrType == typeof(object) ||
                candidate.ClrType.IsValueType ||
                inferredType is not null && inferredType != candidate.ClrType)
                return false;
            inferredType = candidate.ClrType;
        }
        if (inferredType is null) return false;
        type = new PowerShellTypeFact(
            inferredType,
            PowerShellTypeFactProvenance.Inferred,
            "A null-seeded local has one exact reference type across every later concrete assignment.");
        return true;
    }

    private static bool IsNullAssignment(AssignmentStatementAst assignment)
        => assignment.Operator.ToString().Equals("Equals", StringComparison.Ordinal) &&
           UnwrapExpression(assignment.Right) is VariableExpressionAst variable &&
           variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase);
}
