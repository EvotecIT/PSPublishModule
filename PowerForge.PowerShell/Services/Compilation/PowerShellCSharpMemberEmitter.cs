using System.Globalization;
using System.Management.Automation;
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
    private readonly Func<MemberInfo, bool> _isSupportedMember;
    private readonly Func<ExpressionAst, bool> _canNormalizeNullStringReceiver;
    private readonly bool _canEmitPowerShellRuntimeErrors;
    private readonly Func<Ast, string, PowerShellCSharpEmissionException> _error;
    private int _temporaryIndex;

    internal PowerShellCSharpMemberEmitter(
        Func<Ast, Type> inferExpressionType,
        Func<Ast, string> emitExpression,
        Func<Type, Type, bool> canAssign,
        Func<Type, string> getTypeName,
        Func<Type, bool> isSupportedType,
        Func<MemberInfo, bool> isSupportedMember,
        Func<ExpressionAst, bool> canNormalizeNullStringReceiver,
        bool canEmitPowerShellRuntimeErrors,
        Func<Ast, string, PowerShellCSharpEmissionException> error)
    {
        _inferExpressionType = inferExpressionType;
        _emitExpression = emitExpression;
        _canAssign = canAssign;
        _getTypeName = getTypeName;
        _isSupportedType = isSupportedType;
        _isSupportedMember = isSupportedMember;
        _canNormalizeNullStringReceiver = canNormalizeNullStringReceiver;
        _canEmitPowerShellRuntimeErrors = canEmitPowerShellRuntimeErrors;
        _error = error;
    }

    internal Type InferMemberType(MemberExpressionAst member)
    {
        var target = ResolveTarget(member.Expression);
        var name = GetMemberName(member);
        EnsureSupportedArrayMember(member, target, name);
        var resolved = ResolveFieldOrProperty(member, target.Type, target.IsStatic, name);
        var type = resolved switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw _error(member, $"CLR member '{target.Type.FullName}.{name}' is not a readable field or property.")
        };
        EnsureSupportedReferenceMember(member, target, name, type);
        if (!_isSupportedType(type))
            throw _error(member, $"CLR member '{target.Type.FullName}.{name}' returns type '{type.FullName}' outside the generated project reference set.");
        return type;
    }

    internal string EmitMember(MemberExpressionAst member)
    {
        var target = ResolveTarget(member.Expression);
        var name = GetMemberName(member);
        EnsureSupportedArrayMember(member, target, name);
        var resolved = ResolveFieldOrProperty(member, target.Type, target.IsStatic, name);
        var actualName = resolved.Name;
        var resultType = resolved is PropertyInfo property ? property.PropertyType : ((FieldInfo)resolved).FieldType;
        EnsureSupportedReferenceMember(member, target, name, resultType);
        if (!target.IsStatic && target.Type.IsArray && actualName.Equals("Length", StringComparison.Ordinal))
        {
            var elementType = target.Type.GetElementType()!;
            return $"({target.Code} ?? global::System.Array.Empty<{_getTypeName(elementType)}>()).Length";
        }
        if (!target.IsStatic && target.Type == typeof(string))
            return $"({target.Code} ?? string.Empty).{actualName}";
        if (RequiresNullPropagation(target))
            return $"({target.Code})?.{actualName}";
        return $"{EmitTarget(target)}.{actualName}";
    }

    private void EnsureSupportedArrayMember(Ast node, Target target, string name)
    {
        if (!target.IsStatic && target.Type.IsArray && !name.Equals("Length", StringComparison.OrdinalIgnoreCase))
            throw _error(node, $"CLR array member '{name}' does not preserve PowerShell null-member semantics on the conservative compilation path; only Length is currently eligible.");
    }

    private void EnsureSupportedReferenceMember(Ast node, Target target, string name, Type resultType)
    {
        if (!RequiresNullPropagation(target) || !resultType.IsValueType || Nullable.GetUnderlyingType(resultType) is not null)
            return;
        throw _error(
            node,
            $"CLR member '{target.Type.FullName}.{name}' on a nullable reference receiver returns non-nullable CLR value '{resultType.FullName}', so typed compilation cannot preserve PowerShell's missing-value result.");
    }

    private static bool RequiresNullPropagation(Target target)
        => !target.IsStatic &&
           !target.Type.IsValueType &&
           target.Type != typeof(string) &&
           !target.Type.IsArray &&
           !target.IsKnownNonNull;

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
        if (IsStringDictionary(target.Type))
            return typeof(string);
        if (IsObjectDictionary(target.Type))
            return typeof(object);
        if (IsDirectReturnValue(index))
            return typeof(object);
        return target.Type == typeof(string) ? typeof(char) : target.Type.GetElementType()!;
    }

    internal string EmitIndex(IndexExpressionAst index)
    {
        var target = ResolveIndexTarget(index);
        var targetCode = EmitTarget(target);
        if (target.Type == typeof(string))
            targetCode = $"({targetCode} ?? string.Empty)";
        var indexCode = _emitExpression(index.Index);
        if (IsStringDictionary(target.Type))
        {
            if (IsOrderedStringDictionary(target.Type))
                return $"({targetCode} is null ? null : {targetCode}.Contains({indexCode}) ? (string?){targetCode}[{indexCode}] : null)";
            var temporary = "__powerForgeDictionaryValue" + _temporaryIndex++.ToString(CultureInfo.InvariantCulture);
            return $"({targetCode} is null ? null : {targetCode}.TryGetValue({indexCode}, out var {temporary}) ? {temporary} : null)";
        }
        if (IsObjectDictionary(target.Type))
            return $"({targetCode} is null ? null : {targetCode}[{indexCode}])";
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
            ? $"({targetCode} is null ? throw {GetNullArrayException(index)} : {emitted})"
            : emitted;
    }

    internal string EmitIndexAssignment(IndexExpressionAst index, Ast value)
    {
        if (index.Target is not VariableExpressionAst)
            throw _error(index.Target, "Typed array mutation requires a local or parameter array target.");
        var target = ResolveIndexTarget(index);
        if (!target.Type.IsArray || target.Type.GetArrayRank() != 1)
            throw _error(index.Target, "Typed CLR indexed mutation currently supports one-dimensional arrays only.");
        var elementType = target.Type.GetElementType()!;
        var valueType = _inferExpressionType(value);
        if (!_canAssign(elementType, valueType))
            throw _error(value, $"Array assignment value type '{valueType.FullName}' is not assignable to element type '{elementType.FullName}'.");
        var targetCode = EmitTarget(target);
        var indexCode = _emitExpression(index.Index);
        var checkedTarget = $"({targetCode} ?? throw {GetNullArrayException(index)})";
        var normalizedIndex = $"(({indexCode}) < 0 ? {checkedTarget}.Length + ({indexCode}) : ({indexCode}))";
        var checkedIndex = $"({normalizedIndex} >= 0 && {normalizedIndex} < {checkedTarget}.Length ? {normalizedIndex} : throw {GetArrayIndexException(index)})";
        return $"{checkedTarget}[{checkedIndex}] = {_emitExpression(value)}";
    }

    internal string EmitMemberAssignment(MemberExpressionAst member, Ast value)
    {
        if (member.Expression is not VariableExpressionAst)
            throw _error(member.Expression, "Typed CLR member mutation requires a local or parameter receiver.");
        var target = ResolveTarget(member.Expression);
        if (target.IsStatic)
            throw _error(member.Expression, "Typed CLR member mutation does not modify static state.");
        var name = GetMemberName(member);
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var members = target.Type.GetMember(name, MemberTypes.Field | MemberTypes.Property, flags)
            .Where(_isSupportedMember)
            .Where(candidate => candidate switch
            {
                PropertyInfo property => property.GetMethod is { IsPublic: true } &&
                                         property.SetMethod is { IsPublic: true } &&
                                         property.GetIndexParameters().Length == 0,
                FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
                _ => false
            })
            .ToArray();
        if (members.Length == 0)
            throw _error(member, $"CLR member '{target.Type.FullName}.{name}' was not found as a readable and writable instance field or property.");
        if (members.Length > 1)
            throw _error(member, $"Writable CLR member '{target.Type.FullName}.{name}' is ambiguous on the conservative compilation path.");
        var memberType = members[0] is PropertyInfo propertyInfo ? propertyInfo.PropertyType : ((FieldInfo)members[0]).FieldType;
        var valueType = _inferExpressionType(value);
        if (!_isSupportedType(memberType) || !_canAssign(memberType, valueType))
            throw _error(value, $"Member assignment value type '{valueType.FullName}' is not assignable to '{memberType.FullName}'.");
        var targetCode = EmitTarget(target);
        if (RequiresNullPropagation(target))
        {
            if (!_canEmitPowerShellRuntimeErrors)
            {
                throw _error(
                    member,
                    $"CLR member assignment '{target.Type.FullName}.{members[0].Name}' on a potentially null reference receiver cannot preserve PowerShell's runtime-error identity without a PowerShell host.");
            }
            var message = $"The property '{members[0].Name}' cannot be found on this object. Verify that the property exists and can be set.";
            targetCode = $"({targetCode} ?? throw new global::System.Management.Automation.RuntimeException({PowerShellCSharpLiteral.QuoteString(message)}))";
        }
        return $"{targetCode}.{members[0].Name} = {_emitExpression(value)}";
    }

    internal string EmitInvocation(InvokeMemberExpressionAst invocation)
    {
        var resolved = ResolveInvocation(invocation);
        if (RequiresNullPropagation(resolved.Target))
        {
            if (!_canEmitPowerShellRuntimeErrors)
            {
                throw _error(
                    invocation,
                    $"CLR method invocation '{resolved.Target.Type.FullName}.{resolved.Method!.Name}' on a potentially null reference receiver cannot preserve PowerShell's runtime-error identity without a PowerShell host.");
            }
        }
        var sourceArguments = invocation.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        var arguments = string.Join(", ", sourceArguments.Select((argument, index) =>
            EmitArgument(argument, resolved.Parameters[index].ParameterType)));
        if (resolved.Constructor is not null)
            return $"new {_getTypeName(resolved.Target.Type)}({arguments})";
        var target = EmitTarget(resolved.Target);
        if (RequiresNullPropagation(resolved.Target))
        {
            target = $"({target} ?? throw new global::System.Management.Automation.RuntimeException(" +
                     PowerShellCSharpLiteral.QuoteString("You cannot call a method on a null-valued expression.") + "))";
        }
        return $"{target}.{resolved.Method!.Name}({arguments})";
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
                target.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(constructor => _isSupportedMember(constructor)),
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
                                    !candidate.IsSpecialName &&
                                    !candidate.IsGenericMethodDefinition &&
                                    !candidate.ContainsGenericParameters &&
                                    _isSupportedMember(candidate)),
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
            if (target.IsEnum && arguments[index] is StringConstantExpressionAst enumText && TryResolveEnumLiteral(target, enumText.Value, out _))
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
        if (targetType.IsEnum && argument is StringConstantExpressionAst enumText && TryResolveEnumLiteral(targetType, enumText.Value, out var enumValue))
            return EmitEnumValue(targetType, enumValue);
        return _emitExpression(argument);
    }

    private static bool TryResolveEnumLiteral(Type enumType, string value, out object resolved)
    {
        try
        {
            var candidate = Enum.Parse(enumType, value, ignoreCase: true);
            if (Enum.IsDefined(enumType, candidate))
            {
                resolved = candidate;
                return true;
            }
        }
        catch (ArgumentException) { }
        catch (OverflowException) { }
        resolved = default!;
        return false;
    }

    private string EmitEnumValue(Type enumType, object value)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        var literal = Type.GetTypeCode(underlying) is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            ? Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "UL"
            : Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L";
        return $"({_getTypeName(enumType)}){literal}";
    }

    private MemberInfo ResolveFieldOrProperty(Ast node, Type type, bool isStatic, string name)
    {
        var flags = BindingFlags.Public | BindingFlags.IgnoreCase |
                    (isStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var members = type.GetMember(name, MemberTypes.Field | MemberTypes.Property, flags)
            .Where(_isSupportedMember)
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
        if (Nullable.GetUnderlyingType(instanceType) is not null)
        {
            throw _error(
                expression,
                $"Member observation on nullable value type '{instanceType.FullName}' requires PowerShell's boxing semantics and cannot be lowered as direct CLR member access.");
        }
        if (instanceType == typeof(PSObject) || instanceType == typeof(PSCustomObject))
        {
            throw _error(
                expression,
                "Member observation on a [pscustomobject] value requires PowerShell's adapted-object identity and cannot be lowered as direct CLR member access.");
        }
        return new Target(
            instanceType,
            false,
            _emitExpression(expression),
            _canNormalizeNullStringReceiver(expression),
            IsKnownNonNullReference(expression));
    }

    private static bool IsKnownNonNullReference(ExpressionAst expression)
        => expression is StringConstantExpressionAst or ArrayLiteralAst or HashtableAst ||
           expression is InvokeMemberExpressionAst
           {
               Expression: TypeExpressionAst,
               Member: StringConstantExpressionAst member
           } && member.Value.Equals("new", StringComparison.OrdinalIgnoreCase);

    private string GetNullArrayException(Ast node)
    {
        if (_canEmitPowerShellRuntimeErrors)
            return "new global::System.Management.Automation.RuntimeException(\"Cannot index into a null array.\")";
        if (IsInsideTypeDiscriminatingTry(node))
        {
            throw _error(
                node,
                "Indexing a potentially null array inside a typed try/catch cannot preserve PowerShell's runtime-error identity without a PowerShell host.");
        }
        return "new global::System.InvalidOperationException(\"Cannot index into a null array.\")";
    }

    private string GetArrayIndexException(Ast node)
    {
        if (_canEmitPowerShellRuntimeErrors)
            return "new global::System.Management.Automation.RuntimeException(\"Index was outside the bounds of the array.\")";
        if (IsInsideTypeDiscriminatingTry(node))
        {
            throw _error(
                node,
                "Out-of-range array mutation inside a typed try/catch cannot preserve PowerShell's runtime-error identity without a PowerShell host.");
        }
        return "new global::System.IndexOutOfRangeException(\"Index was outside the bounds of the array.\")";
    }

    private static bool IsInsideTypeDiscriminatingTry(Ast node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TryStatementAst tryStatement &&
                tryStatement.CatchClauses.Any(static clause => clause.CatchTypes.Count > 0))
                return true;
        }
        return false;
    }

    private Target ResolveIndexTarget(IndexExpressionAst index)
    {
        if (index.Target is not VariableExpressionAst and not StringConstantExpressionAst and not ArrayLiteralAst)
            throw _error(index.Target, "Typed indexing requires a side-effect-free local, parameter, string literal, or array literal target.");
        var target = ResolveTarget(index.Target);
        if (target.IsStatic || target.Type != typeof(string) && !target.Type.IsArray && !IsStringDictionary(target.Type) && !IsObjectDictionary(target.Type))
            throw _error(index.Target, "Typed indexing currently supports strings, one-dimensional CLR arrays, homogeneous string dictionaries, and IDictionary parameters only.");
        if (target.Type.IsArray && target.Type.GetArrayRank() != 1)
            throw _error(index.Target, "Typed indexing currently supports one-dimensional CLR arrays only.");
        var expectedIndexType = IsStringDictionary(target.Type) ? typeof(string) : IsObjectDictionary(target.Type) ? typeof(object) : typeof(int);
        var actualIndexType = _inferExpressionType(index.Index);
        if (expectedIndexType != typeof(object) && actualIndexType != expectedIndexType)
            throw _error(index.Index, $"Typed indexing requires one scalar {expectedIndexType.Name} index for this target.");
        if (!IsSideEffectFreeIndex(index.Index))
            throw _error(index.Index, "Typed indexing requires a side-effect-free Int32 variable or constant index.");
        return target;
    }

    private static bool IsStringDictionary(Type type)
        => IsOrderedStringDictionary(type) ||
           type.IsGenericType &&
           type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
           type.GetGenericArguments().SequenceEqual(new[] { typeof(string), typeof(string) });

    private static bool IsOrderedStringDictionary(Type type)
        => type == typeof(System.Collections.Specialized.OrderedDictionary);

    private static bool IsObjectDictionary(Type type)
        => typeof(System.Collections.IDictionary).IsAssignableFrom(type) && !IsStringDictionary(type);

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
        internal Target(Type type, bool isStatic, string code, bool normalizeNullString = false, bool isKnownNonNull = false)
        {
            Type = type;
            IsStatic = isStatic;
            Code = code;
            NormalizeNullString = normalizeNullString;
            IsKnownNonNull = isKnownNonNull;
        }

        internal Type Type { get; }
        internal bool IsStatic { get; }
        internal string Code { get; }
        internal bool NormalizeNullString { get; }
        internal bool IsKnownNonNull { get; }
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
