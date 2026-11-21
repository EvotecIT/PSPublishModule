using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellStrictCsvPolicy
{
    internal static readonly Type RowType = typeof(Dictionary<string, string>);
    internal static readonly Type CollectionType = RowType.MakeArrayType();

    internal static bool TryGetRead(CommandAst command, ISet<string>? localFunctionNames,
        out ExpressionAst path, out string[] header, out bool literal)
    {
        path = null!;
        header = Array.Empty<string>();
        literal = false;
        var name = command.GetCommandName();
        if (!(string.Equals(name, "Import-Csv", StringComparison.OrdinalIgnoreCase) && localFunctionNames?.Contains(name!) != true ||
              string.Equals(name, "Microsoft.PowerShell.Utility\\Import-Csv", StringComparison.OrdinalIgnoreCase)) ||
            command.InvocationOperator != TokenKind.Unknown || command.Redirections.Count != 0 ||
            command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            PowerShellCommandRegionSemanticBinder.IsBackground(pipeline) ||
            pipeline.Parent is not AssignmentStatementAst
            {
                Operator: TokenKind.Equals, Left: VariableExpressionAst { VariablePath.IsUnqualified: true } target,
                Parent: StatementBlockAst block
            } assignment ||
            !PowerShellStrictCatchObservation.HasContainingUntypedCatch(block))
            return false;

        var position = block.Statements.IndexOf(assignment);
        if (position < 0 || position == block.Statements.Count - 1 ||
            block.Statements[position + 1] is not ForEachStatementAst { Variable.VariablePath.IsUnqualified: true } loop ||
            loop.Condition is not PipelineAst collection ||
            collection.GetPureExpression() is not VariableExpressionAst { VariablePath.IsUnqualified: true } source ||
            !SameVariable(target, source))
            return false;

        ExpressionAst? operand = null;
        ExpressionAst? names = null;
        var elements = command.CommandElements;
        for (var index = 1; index < elements.Count; index++)
        {
            if (elements[index] is not CommandParameterAst parameter)
            {
                if (operand is not null || elements[index] is not ExpressionAst value) return false;
                operand = value;
                continue;
            }
            var argument = parameter.Argument;
            if (argument is null)
            {
                if (++index >= elements.Count || elements[index] is not ExpressionAst value) return false;
                argument = value;
            }
            if (parameter.ParameterName.Equals("Header", StringComparison.OrdinalIgnoreCase))
            {
                if (names is not null) return false;
                names = argument;
            }
            else if (parameter.ParameterName.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                     parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
            {
                if (operand is not null) return false;
                operand = argument;
                literal = parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase);
            }
            else
                return false;
        }
        if (operand is null or VariableExpressionAst { Splatted: true }) return false;
        var headers = names switch
        {
            StringConstantExpressionAst { Value.Length: > 0 } text => new[] { text.Value },
            ArrayLiteralAst array when array.Elements.All(static item => item is StringConstantExpressionAst { Value.Length: > 0 }) =>
                array.Elements.Cast<StringConstantExpressionAst>().Select(static item => item.Value).ToArray(),
            _ => Array.Empty<string>()
        };
        if (headers.Length == 0 || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length ||
            !HasClosedReaders(block, target, source, loop, headers))
            return false;

        path = operand;
        header = headers;
        return true;
    }

    internal static PowerShellTypeFact GetCollectionType(string[] headers)
        => new(CollectionType, PowerShellTypeFactProvenance.CommandContract, "The CSV result is observed only through typed row enumeration.",
            headers.ToDictionary(static name => name, static _ => new PowerShellTypeFact(typeof(string),
                PowerShellTypeFactProvenance.CommandContract, "A literal CSV header defines a scalar field."),
                StringComparer.OrdinalIgnoreCase), PowerShellDictionaryValueKind.CsvRow);

    private static bool HasClosedReaders(StatementBlockAst block, VariableExpressionAst target,
        VariableExpressionAst source, ForEachStatementAst loop, string[] headers)
    {
        var scope = (Ast)block;
        while (scope.Parent is not null && scope is not ScriptBlockAst) scope = scope.Parent;
        foreach (var node in scope.FindAll(static item => item is VariableExpressionAst, searchNestedScriptBlocks: true))
        {
            var variable = (VariableExpressionAst)node;
            if (SameVariable(variable, target) && !ReferenceEquals(variable, target) && !ReferenceEquals(variable, source))
                return false;
            if (SameVariable(variable, loop.Variable) && !ReferenceEquals(variable, loop.Variable) &&
                !IsInterpolatedField(variable, loop.Body, headers))
                return false;
        }
        return true;
    }

    private static bool IsInterpolatedField(VariableExpressionAst variable, StatementBlockAst loopBody, string[] headers)
    {
        if (!variable.VariablePath.IsUnqualified ||
            variable.Parent is not MemberExpressionAst { Static: false, Member: StringConstantExpressionAst member } field ||
            field is InvokeMemberExpressionAst || !headers.Contains(member.Value, StringComparer.OrdinalIgnoreCase) ||
            field.Parent is not CommandExpressionAst { Redirections.Count: 0 } expression ||
            expression.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.Parent is not StatementBlockAst { Statements.Count: 1 } ||
            pipeline.Parent.Parent is not SubExpressionAst { Parent: ExpandableStringExpressionAst })
            return false;
        for (var ancestor = field.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ReferenceEquals(ancestor, loopBody)) return true;
        return false;
    }

    private static bool SameVariable(VariableExpressionAst left, VariableExpressionAst right)
        => PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(left.VariablePath.UserPath)
            .Equals(right.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase);
}
