using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Recognizes one closed conditional initializer whose branches apply distinct authored scalar
/// and matching one-dimensional vector constraints to the same fresh local. This policy never
/// widens arbitrary object storage or admits later compiled mutation.
/// </summary>
internal static class PowerShellClosedValueAlternativePolicy
{
    internal sealed record Alternative(Type Type, string ConstraintSyntax, AssignmentStatementAst Assignment);

    internal sealed record Initializer(
        string Name,
        string ConditionVariableName,
        IfStatementAst Statement,
        Alternative[] Alternatives)
    {
        internal PowerShellTypeFact CreateTypeFact()
            => new(
                typeof(PowerForge.Generated.Runtime.PowerShellRegionValueAlternative),
                PowerShellTypeFactProvenance.Explicit,
                "A closed conditional initializer selects one authored stable scalar or matching vector constraint.",
                closedAlternativeTypes: Alternatives.Select(static alternative => alternative.Type).ToArray());
    }

    internal static IReadOnlyDictionary<string, Initializer> Find(FunctionDefinitionAst function)
    {
        var matches = new Dictionary<string, Initializer>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in GetTopLevelStatements(function.Body).OfType<IfStatementAst>())
        {
            if (!TryMatch(statement, out var initializer)) continue;
            if (matches.ContainsKey(initializer.Name)) ambiguous.Add(initializer.Name);
            else matches.Add(initializer.Name, initializer);
        }
        foreach (var name in ambiguous) matches.Remove(name);
        return matches;
    }

    internal static bool TryMatch(IfStatementAst statement, out Initializer initializer)
    {
        initializer = null!;
        if (statement.Clauses.Count != 1 || statement.ElseClause is null ||
            statement.Clauses[0].Item2.Traps is { Count: > 0 } || statement.ElseClause.Traps is { Count: > 0 } ||
            statement.Clauses[0].Item2.Statements.Count != 1 || statement.ElseClause.Statements.Count != 1 ||
            statement.Clauses[0].Item2.Statements[0] is not AssignmentStatementAst whenTrue ||
            statement.ElseClause.Statements[0] is not AssignmentStatementAst whenFalse ||
            !TryGetAlternative(whenTrue, out var trueName, out var trueAlternative) ||
            !TryGetAlternative(whenFalse, out var falseName, out var falseAlternative) ||
            !trueName.Equals(falseName, StringComparison.OrdinalIgnoreCase) ||
            !TryGetDirectConditionVariable(statement.Clauses[0].Item1, out var conditionVariableName) ||
            statement.Clauses[0].Item1.FindAll(node =>
                    node is VariableExpressionAst variable && variable.VariablePath.IsUnqualified &&
                    variable.VariablePath.UserPath.Equals(trueName, StringComparison.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: false).Any())
            return false;

        var types = new[] { trueAlternative.Type, falseAlternative.Type };
        var scalars = types.Where(PowerShellStableScalarTypePolicy.IsSupported).ToArray();
        var vectors = types.Where(type => type.IsArray && type.GetArrayRank() == 1 &&
            type.GetElementType() is { } element && type == element.MakeArrayType() &&
            PowerShellStableScalarTypePolicy.IsSupported(element)).ToArray();
        if (scalars.Length != 1 || vectors.Length != 1 ||
            vectors[0].GetElementType() != scalars[0] || types.Distinct().Count() != 2)
            return false;

        initializer = new Initializer(
            trueName,
            conditionVariableName,
            statement,
            new[] { trueAlternative, falseAlternative });
        return true;
    }

    private static bool TryGetDirectConditionVariable(PipelineBaseAst condition, out string name)
    {
        name = string.Empty;
        if (condition is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandExpressionAst
            {
                Expression: VariableExpressionAst variable
            } ||
            !variable.VariablePath.IsUnqualified)
            return false;
        name = variable.VariablePath.UserPath;
        return true;
    }

    internal static bool TryGetAlternativeIndex(
        PowerShellTypeFact envelopeType,
        string authoredTarget,
        Type valueType,
        out int alternativeIndex)
    {
        alternativeIndex = -1;
        if (envelopeType.ClrType != typeof(PowerForge.Generated.Runtime.PowerShellRegionValueAlternative) ||
            envelopeType.ClosedAlternativeTypes.Count != 2 ||
            !TryParseTargetType(authoredTarget, out var targetType) || targetType != valueType)
            return false;
        for (var index = 0; index < envelopeType.ClosedAlternativeTypes.Count; index++)
        {
            if (envelopeType.ClosedAlternativeTypes[index] != targetType) continue;
            alternativeIndex = index;
            return true;
        }
        return false;
    }

    internal static bool TryGetAssignmentType(
        PowerShellTypeFact envelopeType,
        AssignmentStatementAst assignment,
        out Type alternativeType)
    {
        alternativeType = typeof(object);
        return envelopeType.ClrType == typeof(PowerForge.Generated.Runtime.PowerShellRegionValueAlternative) &&
               TryGetTarget(assignment, out _, out alternativeType, out _) &&
               envelopeType.ClosedAlternativeTypes.Contains(alternativeType);
    }

    private static bool TryGetAlternative(
        AssignmentStatementAst assignment,
        out string name,
        out Alternative alternative)
    {
        name = string.Empty;
        alternative = null!;
        if (!assignment.Operator.ToString().Equals("Equals", StringComparison.Ordinal) ||
            !TryGetTarget(assignment, out name, out var type, out var syntax))
            return false;
        alternative = new Alternative(type, syntax, assignment);
        return true;
    }

    private static bool TryGetTarget(
        AssignmentStatementAst assignment,
        out string name,
        out Type type,
        out string constraintSyntax)
    {
        name = string.Empty;
        type = typeof(object);
        constraintSyntax = string.Empty;
        if (assignment.Left is not AttributedExpressionAst
            {
                Attribute: TypeConstraintAst constraint,
                Child: VariableExpressionAst variable
            } ||
            !variable.VariablePath.IsUnqualified ||
            constraint.TypeName.GetReflectionType() is not { } resolved)
            return false;
        name = variable.VariablePath.UserPath;
        type = resolved;
        constraintSyntax = constraint.Extent.Text;
        return true;
    }

    private static bool TryParseTargetType(string target, out Type type)
    {
        type = typeof(object);
        var parsed = Parser.ParseInput(target + " = $null", out _, out var errors);
        return errors.Length == 0 && parsed.EndBlock.Statements.Count == 1 &&
               parsed.EndBlock.Statements[0] is AssignmentStatementAst assignment &&
               TryGetTarget(assignment, out _, out type, out _);
    }

    private static IEnumerable<StatementAst> GetTopLevelStatements(ScriptBlockAst body)
    {
        if (body.DynamicParamBlock is not null)
            foreach (var statement in body.DynamicParamBlock.Statements) yield return statement;
        if (body.BeginBlock is not null)
            foreach (var statement in body.BeginBlock.Statements) yield return statement;
        if (body.ProcessBlock is not null)
            foreach (var statement in body.ProcessBlock.Statements) yield return statement;
        if (body.EndBlock is not null)
            foreach (var statement in body.EndBlock.Statements) yield return statement;
    }
}
