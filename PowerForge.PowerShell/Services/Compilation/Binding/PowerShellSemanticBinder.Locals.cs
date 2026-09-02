using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static PowerShellBoundLocal[] DeclareLocals(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticRegistry commandRegistry,
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
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is null) continue;
            var name = variable.VariablePath.UserPath;
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(PowerShellBoundParametersPolicy.VariableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (symbols.ContainsKey(name)) continue;
            var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
            var type = ResolveAssignmentType(assignment, functions, capabilities, commandRegistry);
            if (type.Provenance == PowerShellTypeFactProvenance.Unknown &&
                TryInferNullSeededReferenceType(name, assignment, assignments, functions, capabilities, commandRegistry, out var inferred))
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
            var elementType = collectionType is { IsArray: true } && collectionType.GetArrayRank() == 1
                ? collectionType.GetElementType()
                : collectionType == typeof(string)
                    ? typeof(string)
                    : collectionType == typeof(Array) && PowerShellCompilationParameterTypePolicy.CanUseUntypedObject(capabilities)
                        ? typeof(object)
                        : null;
            if (elementType is null) continue;
            var span = PowerShellSourceParser.GetSpan(document, loop.Variable.Extent);
            var type = new PowerShellTypeFact(
                elementType,
                PowerShellTypeFactProvenance.Inferred,
                collectionType == typeof(Array)
                    ? "The generated PowerShell host preserves System.Array elements as object-valued foreach items."
                    : "The foreach collection provides one stable CLR element type.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/foreach/" + loop.Extent.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + name);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            locals.Add(new PowerShellBoundLocal(symbol, type));
        }
        return locals.ToArray();
    }

    private static PowerShellTypeFact ResolveAssignmentType(
        AssignmentStatementAst assignment,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticRegistry commandRegistry)
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
                PowerShellTypeFactProvenance.Explicit);
        if (expression is ConvertExpressionAst powerShellObject && PowerShellObjectConstructionPolicy.IsLiteral(powerShellObject))
            return PowerShellObjectSemanticBinder.InferLiteralType(powerShellObject);
        if (expression is ConvertExpressionAst conversion && conversion.StaticType != typeof(object))
            return new PowerShellTypeFact(conversion.StaticType, PowerShellTypeFactProvenance.Explicit, "The assignment value has an authored conversion.");
        if (expression is InvokeMemberExpressionAst
            {
                Static: true,
                Expression: TypeExpressionAst constructedType,
                Member: StringConstantExpressionAst { Value: var memberName }
            } &&
            memberName.Equals("new", StringComparison.OrdinalIgnoreCase) &&
            constructedType.TypeName.GetReflectionType() is { } constructorType)
            return new PowerShellTypeFact(constructorType, PowerShellTypeFactProvenance.Inferred, "The assignment invokes one statically named CLR constructor.");
        if (expression is CommandAst command &&
            commandRegistry.Resolve(command.GetCommandName()) is
            {
                Status: PowerShellCommandResolutionStatus.Resolved,
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
        PowerShellCommandSemanticRegistry commandRegistry,
        out PowerShellTypeFact type)
    {
        type = PowerShellTypeFact.Unknown;
        if (!IsNullAssignment(first)) return false;
        Type? inferredType = null;
        foreach (var assignment in assignments)
        {
            if (assignment.Extent.StartOffset <= first.Extent.StartOffset) continue;
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is null || !variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!assignment.Operator.ToString().Equals("Equals", StringComparison.Ordinal)) return false;
            if (IsNullAssignment(assignment)) continue;
            var candidate = ResolveAssignmentType(assignment, functions, capabilities, commandRegistry);
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
