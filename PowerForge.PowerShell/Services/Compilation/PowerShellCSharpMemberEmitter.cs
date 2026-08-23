using System.Globalization;
using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

/// <summary>
/// Resolves a conservative subset of CLR member access without using PowerShell's dynamic binder.
/// </summary>
internal sealed class PowerShellCSharpMemberEmitter
{
    private readonly Func<Ast, Type> _inferExpressionType;
    private readonly Func<Ast, string> _emitExpression;
    private readonly Func<Type, Type, bool> _canAssign;
    private readonly Func<Type, string> _getTypeName;
    private readonly Func<Type, bool> _isSupportedType;
    private readonly Func<ExpressionAst, bool> _canNormalizeNullStringReceiver;
    private readonly Func<Ast, string, PowerShellCSharpEmissionException> _error;

    internal PowerShellCSharpMemberEmitter(
        Func<Ast, Type> inferExpressionType,
        Func<Ast, string> emitExpression,
        Func<Type, Type, bool> canAssign,
        Func<Type, string> getTypeName,
        Func<Type, bool> isSupportedType,
        Func<ExpressionAst, bool> canNormalizeNullStringReceiver,
        Func<Ast, string, PowerShellCSharpEmissionException> error)
    {
        _inferExpressionType = inferExpressionType;
        _emitExpression = emitExpression;
        _canAssign = canAssign;
        _getTypeName = getTypeName;
        _isSupportedType = isSupportedType;
        _canNormalizeNullStringReceiver = canNormalizeNullStringReceiver;
        _error = error;
    }

    internal Type InferMemberType(MemberExpressionAst member)
    {
        var target = ResolveTarget(member.Expression);
        var name = GetMemberName(member);
        var resolved = ResolveFieldOrProperty(member, target.Type, target.IsStatic, name);
        var type = resolved switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw _error(member, $"CLR member '{target.Type.FullName}.{name}' is not a readable field or property.")
        };
        if (!_isSupportedType(type))
            throw _error(member, $"CLR member '{target.Type.FullName}.{name}' returns type '{type.FullName}' outside the generated project reference set.");
        return type;
    }

    internal string EmitMember(MemberExpressionAst member)
    {
        var target = ResolveTarget(member.Expression);
        var name = GetMemberName(member);
        var resolved = ResolveFieldOrProperty(member, target.Type, target.IsStatic, name);
        var actualName = resolved.Name;
        if (!target.IsStatic && target.Type.IsArray && actualName.Equals("Length", StringComparison.Ordinal))
        {
            var elementType = target.Type.GetElementType()!;
            return $"({target.Code} ?? global::System.Array.Empty<{_getTypeName(elementType)}>()).Length";
        }
        return $"{EmitTarget(target)}.{actualName}";
    }

    internal Type InferInvocationType(InvokeMemberExpressionAst invocation)
    {
        var type = ResolveInvocation(invocation).ReturnType;
        if (!_isSupportedType(type))
            throw _error(invocation, $"CLR invocation returns type '{type.FullName}' outside the generated project reference set.");
        return type;
    }

    internal Type InferIndexType(IndexExpressionAst index)
    {
        var target = ResolveIndexTarget(index);
        if (IsDirectReturnValue(index))
            return typeof(object);
        return target.Type == typeof(string) ? typeof(char) : target.Type.GetElementType()!;
    }

    internal string EmitIndex(IndexExpressionAst index)
    {
        var target = ResolveIndexTarget(index);
        var targetCode = EmitTarget(target);
        var indexCode = _emitExpression(index.Index);
        var normalizedIndex = $"(({indexCode}) < 0 ? {targetCode}.Length + ({indexCode}) : ({indexCode}))";
        var missing = $"{normalizedIndex} < 0 || {normalizedIndex} >= {targetCode}.Length";
        var value = $"{targetCode}[{normalizedIndex}]";
        string emitted;
        if (IsDirectReturnValue(index))
            emitted = $"({missing} ? null : (object){value})";
        else if (IsCompoundAssignmentValue(index))
        {
            var elementType = target.Type == typeof(string) ? typeof(char) : target.Type.GetElementType()!;
            emitted = $"({missing} ? default({_getTypeName(elementType)}) : {value})";
        }
        else
        {
            throw _error(index, "Typed indexing is currently supported only as a direct return value or compound-assignment operand so missing-index semantics remain exact.");
        }
        return target.Type.IsArray
            ? $"({targetCode} is null ? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\") : {emitted})"
            : emitted;
    }

    internal string EmitInvocation(InvokeMemberExpressionAst invocation)
    {
        var resolved = ResolveInvocation(invocation);
        var sourceArguments = invocation.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        var arguments = string.Join(", ", sourceArguments.Select((argument, index) =>
            EmitArgument(argument, resolved.Parameters[index].ParameterType)));
        if (resolved.Constructor is not null)
            return $"new {_getTypeName(resolved.Target.Type)}({arguments})";
        return $"{EmitTarget(resolved.Target)}.{resolved.Method!.Name}({arguments})";
    }

    private ResolvedInvocation ResolveInvocation(InvokeMemberExpressionAst invocation)
    {
        var target = ResolveTarget(invocation.Expression);
        var name = GetMemberName(invocation);
        var arguments = invocation.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        var argumentTypes = arguments.Select(_inferExpressionType).ToArray();
        if (target.IsStatic && name.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            var constructor = SelectBest(
                invocation,
                target.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
                arguments,
                argumentTypes,
                static candidate => candidate.GetParameters(),
                $"constructor for '{target.Type.FullName}'");
            return new ResolvedInvocation(target, null, constructor, constructor.GetParameters(), target.Type);
        }

        var flags = BindingFlags.Public | (target.IsStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var method = SelectBest(
            invocation,
            target.Type.GetMethods(flags)
                .Where(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                    !candidate.IsGenericMethodDefinition &&
                                    !candidate.ContainsGenericParameters),
            arguments,
            argumentTypes,
            static candidate => candidate.GetParameters(),
            $"method '{target.Type.FullName}.{name}'");
        return new ResolvedInvocation(target, method, null, method.GetParameters(), method.ReturnType);
    }

    private TMember SelectBest<TMember>(
        Ast node,
        IEnumerable<TMember> candidates,
        ExpressionAst[] arguments,
        Type[] argumentTypes,
        Func<TMember, ParameterInfo[]> getParameters,
        string description)
        where TMember : MemberInfo
    {
        var matches = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Parameters = getParameters(candidate)
            })
            .Where(match => match.Parameters.Length == argumentTypes.Length &&
                            match.Parameters.All(static parameter => !parameter.ParameterType.IsByRef && !parameter.IsOut) &&
                            !match.Parameters.Any(static parameter => parameter.GetCustomAttribute<ParamArrayAttribute>() is not null))
            .Select(match => new
            {
                match.Candidate,
                Score = ScoreArguments(arguments, argumentTypes, match.Parameters)
            })
            .Where(static match => match.Score >= 0)
            .OrderBy(static match => match.Score)
            .ToArray();
        if (matches.Length == 0)
            throw _error(node, $"No exact CLR overload was found for {description} with the inferred argument types.");
        if (matches.Length > 1 && matches[0].Score == matches[1].Score)
            throw _error(node, $"CLR overload resolution for {description} is ambiguous on the conservative compilation path.");
        return matches[0].Candidate;
    }

    private int ScoreArguments(ExpressionAst[] arguments, Type[] argumentTypes, ParameterInfo[] parameters)
    {
        var score = 0;
        for (var index = 0; index < argumentTypes.Length; index++)
        {
            var source = argumentTypes[index];
            var target = parameters[index].ParameterType;
            if (source == target)
                continue;
            if (target.IsAssignableFrom(source))
            {
                score += 1;
                continue;
            }
            if (_canAssign(target, source))
            {
                score += 2;
                continue;
            }
            if (target == typeof(char) && arguments[index] is StringConstantExpressionAst text && text.Value.Length == 1)
            {
                score += 3;
                continue;
            }
            return -1;
        }
        return score;
    }

    private string EmitArgument(ExpressionAst argument, Type targetType)
    {
        if (targetType == typeof(char) && argument is StringConstantExpressionAst text && text.Value.Length == 1)
            return EmitChar(text.Value[0]);
        return _emitExpression(argument);
    }

    private MemberInfo ResolveFieldOrProperty(Ast node, Type type, bool isStatic, string name)
    {
        var flags = BindingFlags.Public | BindingFlags.IgnoreCase |
                    (isStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var members = type.GetMember(name, MemberTypes.Field | MemberTypes.Property, flags)
            .Where(member => member switch
            {
                PropertyInfo property => property.GetMethod is not null && property.GetIndexParameters().Length == 0,
                FieldInfo => true,
                _ => false
            })
            .ToArray();
        if (members.Length == 0)
            throw _error(node, $"CLR member '{type.FullName}.{name}' was not found as a readable field or property.");
        if (members.Length > 1)
            throw _error(node, $"CLR member '{type.FullName}.{name}' is ambiguous on the conservative compilation path.");
        return members[0];
    }

    private Target ResolveTarget(ExpressionAst expression)
    {
        if (expression is TypeExpressionAst typeExpression)
        {
            var type = typeExpression.TypeName.GetReflectionType()
                ?? throw _error(typeExpression, $"CLR type '{typeExpression.TypeName.FullName}' could not be resolved.");
            if (!_isSupportedType(type))
                throw _error(typeExpression, $"CLR type '{typeExpression.TypeName.FullName}' is not available in the generated runtime-independent project reference set.");
            return new Target(type, true, _getTypeName(type));
        }
        var instanceType = _inferExpressionType(expression);
        return new Target(instanceType, false, _emitExpression(expression), _canNormalizeNullStringReceiver(expression));
    }

    private Target ResolveIndexTarget(IndexExpressionAst index)
    {
        if (index.Target is not VariableExpressionAst and not StringConstantExpressionAst and not ArrayLiteralAst)
            throw _error(index.Target, "Typed indexing requires a side-effect-free local, parameter, string literal, or array literal target.");
        var target = ResolveTarget(index.Target);
        if (target.IsStatic || target.Type != typeof(string) && !target.Type.IsArray)
            throw _error(index.Target, "Typed indexing currently supports strings and one-dimensional CLR arrays only.");
        if (target.Type.IsArray && target.Type.GetArrayRank() != 1)
            throw _error(index.Target, "Typed indexing currently supports one-dimensional CLR arrays only.");
        if (_inferExpressionType(index.Index) != typeof(int))
            throw _error(index.Index, "Typed indexing requires one scalar Int32 index.");
        if (!IsSideEffectFreeIndex(index.Index))
            throw _error(index.Index, "Typed indexing requires a side-effect-free Int32 variable or constant index.");
        return target;
    }

    private static bool IsSideEffectFreeIndex(ExpressionAst index)
        => index is VariableExpressionAst or ConstantExpressionAst ||
           index is UnaryExpressionAst { Child: ConstantExpressionAst } unary &&
           unary.TokenKind.ToString() is "Plus" or "Minus";

    private static bool IsDirectReturnValue(IndexExpressionAst index)
    {
        Ast current = index;
        while (current.Parent is CommandExpressionAst or ParenExpressionAst or PipelineAst)
            current = current.Parent;
        return current.Parent is ReturnStatementAst;
    }

    private static bool IsCompoundAssignmentValue(IndexExpressionAst index)
    {
        Ast current = index;
        while (current.Parent is CommandExpressionAst or ParenExpressionAst)
            current = current.Parent;
        return current.Parent is AssignmentStatementAst assignment && assignment.Operator.ToString() != "Equals";
    }

    private static string GetMemberName(MemberExpressionAst member)
        => member.Member is StringConstantExpressionAst name && !string.IsNullOrWhiteSpace(name.Value)
            ? name.Value
            : throw new PowerShellCSharpEmissionException(member.Member, "Dynamic CLR member names require PowerShell runtime binding.");

    private static string EmitTarget(Target target)
    {
        if (target.IsStatic)
            return target.Code;
        return target.Type == typeof(string) && target.NormalizeNullString
            ? $"({target.Code} ?? string.Empty)"
            : $"({target.Code})";
    }

    private static string EmitChar(char value)
        => value switch
        {
            '\\' => "'\\\\'",
            '\'' => "'\\\''",
            '\0' => "'\\0'",
            '\r' => "'\\r'",
            '\n' => "'\\n'",
            '\t' => "'\\t'",
            _ when char.IsControl(value) => "'\\u" + ((int)value).ToString("x4", CultureInfo.InvariantCulture) + "'",
            _ => "'" + value + "'"
        };

    private sealed class Target
    {
        internal Target(Type type, bool isStatic, string code, bool normalizeNullString = false)
        {
            Type = type;
            IsStatic = isStatic;
            Code = code;
            NormalizeNullString = normalizeNullString;
        }

        internal Type Type { get; }
        internal bool IsStatic { get; }
        internal string Code { get; }
        internal bool NormalizeNullString { get; }
    }

    private sealed class ResolvedInvocation
    {
        internal ResolvedInvocation(
            Target target,
            MethodInfo? method,
            ConstructorInfo? constructor,
            ParameterInfo[] parameters,
            Type returnType)
        {
            Target = target;
            Method = method;
            Constructor = constructor;
            Parameters = parameters;
            ReturnType = returnType;
        }

        internal Target Target { get; }
        internal MethodInfo? Method { get; }
        internal ConstructorInfo? Constructor { get; }
        internal ParameterInfo[] Parameters { get; }
        internal Type ReturnType { get; }
    }
}
