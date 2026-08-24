using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private void EmitIf(IfStatementAst statement, Type returnType)
    {
        for (var index = 0; index < statement.Clauses.Count; index++)
        {
            var clause = statement.Clauses[index];
            AppendLine($"{(index == 0 ? "if" : "else if")} ({EmitBooleanExpression(clause.Item1)})");
            EmitBlock(clause.Item2, returnType);
        }
        if (statement.ElseClause is not null)
        {
            AppendLine("else");
            EmitBlock(statement.ElseClause, returnType);
        }
    }

    private void EmitSwitch(SwitchStatementAst statement, Type returnType)
    {
        if ((statement.Flags & (SwitchFlags.File | SwitchFlags.Regex | SwitchFlags.Wildcard | SwitchFlags.Parallel)) != 0)
            throw Error(statement, $"Switch flags '{statement.Flags}' require PowerShell runtime matching semantics.");
        var conditionType = InferExpressionType(statement.Condition);
        if (!IsConservativeSwitchScalar(conditionType))
            throw Error(statement.Condition, $"Scalar switch requires a Boolean, character, string, or numeric condition; resolved type was '{conditionType.FullName}'.");
        var incompatibleClause = statement.Clauses
            .Select(clause => new { Clause = clause, Type = InferExpressionType(clause.Item1) })
            .FirstOrDefault(item => item.Type != conditionType);
        if (incompatibleClause is not null)
            throw Error(incompatibleClause.Clause.Item1, $"Scalar switch clause type '{incompatibleClause.Type.FullName}' must exactly match condition type '{conditionType.FullName}' to avoid PowerShell coercion semantics.");

        var index = _switchIndex++;
        var valueName = $"__switchValue{index}";
        var matchedName = $"__switchMatched{index}";
        AppendLine($"{GetTypeName(conditionType)} {valueName} = {EmitExpression(statement.Condition)};");
        AppendLine($"bool {matchedName} = false;");
        AppendLine("do");
        AppendLine("{");
        _indent++;
        foreach (var clause in statement.Clauses)
        {
            AppendLine($"if ({EmitSwitchComparison(valueName, conditionType, clause.Item1, statement.Flags)})");
            AppendLine("{");
            _indent++;
            AppendLine($"{matchedName} = true;");
            foreach (var bodyStatement in clause.Item2.Statements) EmitStatement(bodyStatement, returnType);
            _indent--;
            AppendLine("}");
        }
        if (statement.Default is not null)
        {
            AppendLine($"if (!{matchedName})");
            EmitBlock(statement.Default, returnType);
        }
        _indent--;
        AppendLine("}");
        AppendLine("while (false);");
    }

    private string EmitSwitchComparison(string valueName, Type conditionType, ExpressionAst clause, SwitchFlags flags)
    {
        var clauseSource = EmitExpression(clause);
        if (conditionType != typeof(string)) return $"{valueName} == {clauseSource}";
        var comparison = (flags & SwitchFlags.CaseSensitive) != 0 ? "InvariantCulture" : "InvariantCultureIgnoreCase";
        return $"global::System.String.Equals({valueName}, {clauseSource}, global::System.StringComparison.{comparison})";
    }

    private static bool IsConservativeSwitchScalar(Type type)
        => type == typeof(bool) || type == typeof(char) || type == typeof(string) || IsNumeric(type);

    private void EmitFor(ForStatementAst statement, Type returnType)
    {
        var initializer = EmitInlinePipeline(statement.Initializer);
        var condition = statement.Condition is null ? "true" : EmitBooleanExpression(statement.Condition);
        var iterator = EmitInlinePipeline(statement.Iterator);
        AppendLine($"for ({initializer}; {condition}; {iterator})");
        EmitBlock(statement.Body, returnType);
    }

    private void EmitWhile(WhileStatementAst statement, Type returnType)
    {
        AppendLine($"while ({EmitBooleanExpression(statement.Condition)})");
        EmitBlock(statement.Body, returnType);
    }

    private void EmitForEach(ForEachStatementAst statement, Type returnType)
    {
        var name = statement.Variable.VariablePath.UserPath;
        var collectionType = InferExpressionType(statement.Condition);
        var elementType = collectionType.IsArray ? collectionType.GetElementType() : CanUseScalarStringForeach(statement.Condition) ? typeof(string) : null;
        if (elementType is null) throw Error(statement.Condition, "foreach requires a statically typed one-dimensional array or scalar string.");
        var collection = EmitExpression(statement.Condition);
        _declaredLocals.Add(name);
        var enumerable = _scalarForeachLoops.Contains(statement.Extent.StartOffset)
            ? $"new[] {{ {collection} }}"
            : $"({collection} ?? global::System.Array.Empty<{GetTypeName(elementType)}>())";
        AppendLine($"foreach ({GetTypeName(_variables[name])} {GetVariableIdentifier(name)} in {enumerable})");
        EmitBlock(statement.Body, returnType);
    }

    private void EmitTry(TryStatementAst statement, Type returnType)
    {
        var catchTypes = statement.CatchClauses
            .Select(clause => new
            {
                Clause = clause,
                Types = clause.CatchTypes.Select(type => ResolveCatchType(type)).ToArray()
            })
            .ToArray();
        var catchAll = Array.FindIndex(catchTypes, static item => item.Types.Length == 0);
        if (catchAll >= 0 && catchAll != catchTypes.Length - 1)
            throw Error(catchTypes[catchAll].Clause, "A catch-all clause must follow all typed catches on the conservative typed path.");
        var flattened = catchTypes.SelectMany(static item => item.Types.Select(type => new { item.Clause, Type = type })).ToArray();
        for (var index = 0; index < flattened.Length; index++)
        {
            if (flattened.Take(index).Any(previous => previous.Type.IsAssignableFrom(flattened[index].Type)))
                throw Error(flattened[index].Clause, $"Typed catch '{flattened[index].Type.FullName}' is unreachable after a broader earlier catch.");
        }
        if (statement.Finally?.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst, searchNestedScriptBlocks: true).Any() == true)
            throw Error(statement.Finally, "Typed finally blocks cannot alter enclosing return, break, or continue control flow.");
        AppendLine("try");
        EmitBlock(statement.Body, returnType);
        foreach (var item in catchTypes)
        {
            if (item.Types.Length == 0)
            {
                AppendLine("catch (global::System.Exception)");
                EmitBlock(item.Clause.Body, returnType);
                continue;
            }
            foreach (var type in item.Types)
            {
                AppendLine($"catch ({GetTypeName(type)})");
                EmitBlock(item.Clause.Body, returnType);
            }
        }
        if (statement.Finally is not null)
        {
            AppendLine("finally");
            EmitBlock(statement.Finally, returnType);
        }
    }

    private void EmitThrow(ThrowStatementAst statement)
    {
        if (statement.IsRethrow)
        {
            if (!HasAncestor<CatchClauseAst>(statement))
                throw Error(statement, "A bare typed rethrow is valid only inside a catch clause.");
            AppendLine("throw;");
            return;
        }

        if (statement.Pipeline is null)
            throw Error(statement, "Typed throw requires a statically typed CLR exception expression.");
        var exceptionType = InferExpressionType(statement.Pipeline);
        if (!typeof(Exception).IsAssignableFrom(exceptionType))
            throw Error(statement.Pipeline, $"Typed throw requires a CLR exception expression; resolved type was '{exceptionType.FullName}'.");
        AppendLine($"throw {EmitExpression(statement.Pipeline)};");
    }

    private Type ResolveCatchType(TypeConstraintAst constraint)
    {
        var type = constraint.TypeName.GetReflectionType();
        if (type is null || !typeof(Exception).IsAssignableFrom(type))
            throw Error(constraint, $"Typed catch '{constraint.TypeName.FullName}' is not a statically resolvable CLR exception type.");
        if (!PowerShellGeneratedTypePolicy.IsSupported(type, _targetFramework))
            throw Error(constraint, $"Typed catch '{type.FullName}' is outside the generated project reference set.");
        return type;
    }

    private void EmitBlock(StatementBlockAst block, Type returnType)
    {
        AppendLine("{");
        _indent++;
        foreach (var statement in block.Statements) EmitStatement(statement, returnType);
        _indent--;
        AppendLine("}");
    }
}
