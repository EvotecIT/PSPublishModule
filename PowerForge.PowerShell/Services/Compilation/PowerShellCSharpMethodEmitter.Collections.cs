using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private Type InferStringDictionaryType(HashtableAst hashtable)
    {
        var assignment = FindDictionaryAssignment(hashtable);
        if (assignment is null ||
            FindAssignedVariable(assignment.Left) is not { } variable)
            throw Error(hashtable, "Typed hashtable literals are currently supported only as lookup-only local dictionaries.");
        var variableName = variable.VariablePath.UserPath;
        foreach (var reference in _body.FindAll(
                     node => node is VariableExpressionAst candidate &&
                             candidate.VariablePath.UserPath.Equals(variableName, StringComparison.OrdinalIgnoreCase),
                     searchNestedScriptBlocks: false).Cast<VariableExpressionAst>())
        {
            if (ReferenceEquals(reference, variable))
                continue;
            if (reference.Parent is IndexExpressionAst index && ReferenceEquals(index.Target, reference))
                continue;
            throw Error(reference, $"Typed dictionary local '${variableName}' may currently be used only as an index lookup target.");
        }
        foreach (var pair in hashtable.KeyValuePairs)
        {
            if (InferExpressionType(pair.Item1) != typeof(string) || InferExpressionType(GetHashtableValue(pair.Item2)) != typeof(string))
                throw Error(hashtable, "Typed hashtable literals currently require homogeneous String keys and String values.");
        }
        return typeof(Dictionary<string, string>);
    }

    private string EmitStringDictionary(HashtableAst hashtable)
    {
        _ = InferStringDictionaryType(hashtable);
        var entries = hashtable.KeyValuePairs.Select(pair =>
            $"{{ {EmitExpression(pair.Item1)}, {EmitExpression(GetHashtableValue(pair.Item2))} }}");
        return $"new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase) {{ {string.Join(", ", entries)} }}";
    }

    private Type InferOrderedStringDictionaryType(ConvertExpressionAst conversion)
    {
        var hashtable = GetOrderedHashtable(conversion);
        _ = InferStringDictionaryType(hashtable);
        return typeof(System.Collections.Specialized.OrderedDictionary);
    }

    private string EmitOrderedStringDictionary(ConvertExpressionAst conversion)
    {
        var hashtable = GetOrderedHashtable(conversion);
        _ = InferStringDictionaryType(hashtable);
        var entries = hashtable.KeyValuePairs.Select(pair =>
            $"{{ {EmitExpression(pair.Item1)}, {EmitExpression(GetHashtableValue(pair.Item2))} }}");
        return $"new global::System.Collections.Specialized.OrderedDictionary(global::System.StringComparer.OrdinalIgnoreCase) {{ {string.Join(", ", entries)} }}";
    }

    private static bool IsOrderedHashtableConversion(ConvertExpressionAst conversion)
        => conversion.StaticType == typeof(System.Collections.Specialized.OrderedDictionary) && conversion.Child is HashtableAst;

    private HashtableAst GetOrderedHashtable(ConvertExpressionAst conversion)
        => conversion.Child as HashtableAst ?? throw Error(conversion, "[ordered] must directly wrap a hashtable literal.");

    private AssignmentStatementAst? FindDictionaryAssignment(HashtableAst hashtable)
    {
        Ast candidate = hashtable;
        if (candidate.Parent is ConvertExpressionAst conversion && IsOrderedHashtableConversion(conversion))
            candidate = conversion;
        if (candidate.Parent is CommandExpressionAst commandExpression)
            candidate = commandExpression;
        return candidate.Parent as AssignmentStatementAst;
    }

    private Ast GetHashtableValue(StatementAst statement)
    {
        if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            pipeline.PipelineElements[0] is not CommandExpressionAst expression)
            throw Error(statement, "Typed hashtable values must be one side-effect-free scalar expression.");
        return expression.Expression;
    }

    private string EmitAssignmentExpression(AssignmentStatementAst assignment)
    {
        if (assignment.Operator.ToString() != "Equals")
            throw Error(assignment, "Only simple assignment can be used as a typed expression.");
        var variable = FindAssignedVariable(assignment.Left)
            ?? throw Error(assignment.Left, "Only local-variable assignment is supported.");
        var name = variable.VariablePath.UserPath;
        if (!_declaredLocals.Contains(name))
            throw Error(assignment, $"Inline assignment target '${name}' was not safely predeclared.");
        var right = EmitExpression(assignment.Right);
        if (_variables[name] == typeof(string) && _explicitlyTypedVariables.Contains(name))
            right = $"({right} ?? string.Empty)";
        return $"({GetVariableIdentifier(name)} = {right})";
    }

    private bool HasTerminalValue(StatementAst[] statements)
    {
        var terminal = statements.LastOrDefault();
        if (terminal is ReturnStatementAst)
            return true;
        if (terminal is TryStatementAst tryStatement)
            return BlockReturns(tryStatement.Body) &&
                   tryStatement.CatchClauses.All(static clause => BlockReturns(clause.Body));
        return terminal is PipelineAst { PipelineElements.Count: 1 } pipeline &&
               (pipeline.PipelineElements[0] is CommandExpressionAst || IsLocalFunctionPipeline(pipeline)) &&
               InferExpressionType(pipeline) != typeof(void);
    }

    private static bool BlockReturns(StatementBlockAst block)
        => block.Statements.LastOrDefault() is ReturnStatementAst;
}
