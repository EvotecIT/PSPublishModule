using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    // Keep the existing SDK invocation/error owners. This qualifier only proves that
    // a class-valued factory or explicit cast did not pass its authored Type as a value.
    private static bool HasClosedReferenceValueArgumentCatchAll(InvokeMemberExpressionAst invocation)
    {
        var body = FindOwningFunctionBody(invocation);
        if (body is null || !HasCatchAllObservation(invocation) || invocation.Arguments is not { } arguments ||
            HasUnqualifiedStatementValuedNativeArguments(invocation)) return false;
        for (Ast? parent = invocation.Parent; parent is not null && !ReferenceEquals(parent, body); parent = parent.Parent)
            if (parent is LoopStatementAst) return false;
        Type? receiver;
        if (invocation.Static && invocation.Expression is TypeExpressionAst literal)
            receiver = literal.TypeName.GetReflectionType();
        else receiver = ClosedObservedConstructorReceiver(invocation.Expression, invocation, body);
        if (receiver is null || invocation.Member is not StringConstantExpressionAst member) return false;
        var candidates = ObservedCallCandidates(receiver, member.Value, invocation.Static, arguments.Count);
        return candidates.Length > 0 && candidates.All(static candidate => !candidate.ContainsGenericParameters &&
                   candidate.GetParameters().All(IsClosedObservedParameter)) &&
               arguments.All(argument => IsClosedObservedArgument(argument, invocation, body,
                   new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    private static MethodBase[] ObservedCallCandidates(Type receiver, string name, bool isStatic, int count)
    {
        IEnumerable<MethodBase> candidates = isStatic && name.Equals("new", StringComparison.OrdinalIgnoreCase)
            ? receiver.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            : receiver.GetMethods(BindingFlags.Public | BindingFlags.FlattenHierarchy |
                    (isStatic ? BindingFlags.Static : BindingFlags.Instance))
                .Where(method => method.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return candidates.Where(candidate => CanAcceptNativeArgumentCount(candidate.GetParameters(), count)).ToArray();
    }

    private static bool IsClosedObservedParameter(ParameterInfo parameter)
    {
        if (!parameter.ParameterType.IsByRef) return IsClosedObservedValueType(parameter.ParameterType);
        // Modern framework overload sets can contain readonly-in value structs.
        // These are closed non-Type value slots; authored [ref] arguments
        // still fail the value-origin proof below.
        return parameter.IsIn && !parameter.IsOut && parameter.ParameterType.GetElementType() is { IsValueType: true,
            IsGenericType: false, ContainsGenericParameters: false };
    }

    private static bool IsClosedObservedValueType(Type type)
        => !typeof(Delegate).IsAssignableFrom(type) &&
           !typeof(System.Management.Automation.PSReference).IsAssignableFrom(type) &&
           (PowerShellStableScalarTypePolicy.IsSupported(type) ||
            PowerShellNativeTypeArgumentPolicy.IsClosedNonTypeReferenceResult(type));

    private static bool IsClosedObservedArgument(Ast syntax, InvokeMemberExpressionAst invocation,
        ScriptBlockAst body, HashSet<string> visiting)
    {
        syntax = UnwrapExpression(syntax, preservePipeline: true);
        if (syntax.Find(static node => node is LoopStatementAst or TryStatementAst or SwitchStatementAst,
                searchNestedScriptBlocks: false) is not null) return false;
        switch (syntax)
        {
            case StringConstantExpressionAst or ExpandableStringExpressionAst:
                return true;
            case ConstantExpressionAst constant:
                return constant.Value is null || IsClosedObservedValueType(constant.Value.GetType());
            case ConvertExpressionAst conversion:
                return conversion.Type.TypeName.GetReflectionType() is { } converted && IsClosedObservedValueType(converted);
            case InvokeMemberExpressionAst { Static: true, Expression: TypeExpressionAst target,
                Member: StringConstantExpressionAst member } factory:
                if (target.TypeName.GetReflectionType() is not { } type) return false;
                if (member.Value.Equals("new", StringComparison.OrdinalIgnoreCase)) return IsClosedObservedValueType(type);
                var methods = ObservedCallCandidates(type, member.Value, true, factory.Arguments?.Count ?? 0);
                return methods.Length > 0 && methods.All(static method => method is MethodInfo result &&
                    !result.ContainsGenericParameters && IsClosedObservedValueType(result.ReturnType));
            case VariableExpressionAst variable:
                if (variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
                var name = ObservedLocalName(variable);
                if (name is null || !PowerShellNativeVariableAnalysis.IsDirectLocal(variable) || !visiting.Add(name)) return false;
                try
                {
                    var writes = ClosedObservedLocalWrites(variable, invocation, body);
                    if (writes is null) return false;
                    if (writes.Length > 0) return writes.All(write => IsClosedObservedArgument(write.Right, invocation, body, visiting));
                    var parameter = PowerShellParameterSyntax.GetParameters(body).FirstOrDefault(parameter =>
                        parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return parameter?.Attributes.OfType<TypeConstraintAst>().LastOrDefault()?.TypeName.GetReflectionType() is { } parameterType &&
                           IsClosedObservedValueType(parameterType);
                }
                finally { visiting.Remove(name); }
            default:
                return false;
        }
    }

    private static Type? ClosedObservedConstructorReceiver(Ast syntax, InvokeMemberExpressionAst invocation, ScriptBlockAst body)
    {
        syntax = UnwrapExpression(syntax, preservePipeline: true);
        if (syntax is VariableExpressionAst variable)
        {
            if (!PowerShellNativeVariableAnalysis.IsDirectLocal(variable)) return null;
            var writes = ClosedObservedLocalWrites(variable, invocation, body);
            if (writes is not { Length: 1 }) return null;
            syntax = UnwrapExpression(writes[0].Right, preservePipeline: true);
        }
        return syntax is InvokeMemberExpressionAst { Static: true, Expression: TypeExpressionAst target,
                   Member: StringConstantExpressionAst member } &&
               member.Value.Equals("new", StringComparison.OrdinalIgnoreCase) &&
               target.TypeName.GetReflectionType() is { IsClass: true, IsGenericType: false } type &&
               !typeof(Type).IsAssignableFrom(type) && !typeof(System.Management.Automation.PSObject).IsAssignableFrom(type)
            ? type : null;
    }

    private static AssignmentStatementAst[]? ClosedObservedLocalWrites(VariableExpressionAst variable,
        InvokeMemberExpressionAst invocation, ScriptBlockAst body)
    {
        var name = ObservedLocalName(variable);
        if (name is null) return null;
        // Reference cells, iterator writes and branch/later writes need their own
        // flow proof. Do not replace the conservative origin guard for those paths.
        if (body.Find(node => node is ConvertExpressionAst { Child: VariableExpressionAst referenced } conversion &&
                    conversion.Type.TypeName.GetReflectionType() == typeof(System.Management.Automation.PSReference) &&
                    string.Equals(ObservedLocalName(referenced), name, StringComparison.OrdinalIgnoreCase) ||
                node is ForEachStatementAst iterator &&
                    string.Equals(ObservedLocalName(iterator.Variable), name, StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: false) is not null) return null;
        var writes = body.FindAll(node => node is AssignmentStatementAst assignment &&
                PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left, true) is { } assigned &&
                string.Equals(ObservedLocalName(assigned), name, StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: false).OfType<AssignmentStatementAst>().ToArray();
        if (writes.Any(write => write.Left is not VariableExpressionAst || write.Operator != TokenKind.Equals ||
                !IsLinearObservedWrite(write, invocation))) return null;
        var lastWrite = writes.OrderByDescending(static write => write.Extent.EndOffset).FirstOrDefault();
        if (lastWrite is not null && body.Find(node => node is CommandAst command &&
                command.Extent.StartOffset > lastWrite.Extent.EndOffset && command.Extent.EndOffset <= invocation.Extent.StartOffset,
                searchNestedScriptBlocks: false) is not null) return null;
        return writes;
    }

    private static bool IsLinearObservedWrite(AssignmentStatementAst write, InvokeMemberExpressionAst invocation)
    {
        if (write.Extent.EndOffset > invocation.Extent.StartOffset) return false;
        for (Ast? parent = invocation.Parent; parent is not null && parent is not FunctionDefinitionAst; parent = parent.Parent)
        {
            if (parent is StatementBlockAst block && block.Statements.Any(statement => ReferenceEquals(statement, write)) ||
                parent is NamedBlockAst named && named.Statements.Any(statement => ReferenceEquals(statement, write))) return true;
        }
        return false;
    }

    private static string? ObservedLocalName(VariableExpressionAst variable)
        => variable.VariablePath.IsUnqualified ? variable.VariablePath.UserPath :
           variable.VariablePath.IsLocal ? variable.VariablePath.UserPath.Substring(6) : null;
}
