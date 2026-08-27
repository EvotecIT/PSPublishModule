using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

/// <summary>
/// Resolves conservative CLR member operations at the AST boundary. Downstream stages receive exact neutral operations.
/// </summary>
internal static class PowerShellClrMemberSemanticBinder
{
    internal static PowerShellBoundClrMemberAssignmentStatement? BindAssignment(
        ParsedSourceDocument document,
        AssignmentStatementAst syntax,
        MemberExpressionAst memberSyntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.Operator.ToString() != "Equals" || memberSyntax.Expression is not VariableExpressionAst)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2614", "Typed CLR member mutation requires simple '=' assignment to a local or parameter receiver.", span));
            return null;
        }
        if (!TryResolveTarget(document, memberSyntax.Expression, bindExpression, targetFramework, diagnostics, out var target) || target.IsStatic)
            return null;
        if (!target.Type.IsValueType && !target.IsKnownNonNull)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2615", "CLR member mutation on a potentially null receiver requires PowerShell runtime error identity.", span));
            return null;
        }
        if (!TryGetMemberName(document, memberSyntax, diagnostics, out var name)) return null;
        var members = target.Type.GetMember(name, MemberTypes.Field | MemberTypes.Property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            .Where(member => IsSupportedMember(member, targetFramework))
            .Where(static member => member switch
            {
                PropertyInfo property => property.GetMethod is { IsPublic: true } && property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0,
                FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
                _ => false
            })
            .ToArray();
        if (members.Length != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2616", members.Length == 0
                ? $"CLR member '{target.Type.FullName}.{name}' was not found as one target-compatible readable and writable member."
                : $"Writable CLR member '{target.Type.FullName}.{name}' is ambiguous.", span));
            return null;
        }
        if (members[0] is PropertyInfo && PowerShellRuntimeExceptionCatchPolicy.Contains(memberSyntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2617", $"CLR property assignment '{target.Type.FullName}.{members[0].Name}' inside a RuntimeException catch cannot preserve PowerShell error wrapping.", span));
            return null;
        }
        var memberType = members[0] is PropertyInfo property ? property.PropertyType : ((FieldInfo)members[0]).FieldType;
        var value = bindExpression(syntax.Right, memberType);
        if (value is null || !PowerShellGeneratedTypePolicy.IsSupported(memberType, targetFramework) || !PowerShellClrTypeSemantics.CanAssign(memberType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2618", $"Member assignment value is not assignable to '{memberType.FullName}'.", value?.Span ?? span));
            return null;
        }
        return new PowerShellBoundClrMemberAssignmentStatement(span, target.Receiver!, target.Type, members[0].Name, value);
    }

    internal static PowerShellBoundExpression? BindMember(
        ParsedSourceDocument document,
        MemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!TryResolveTarget(document, syntax.Expression, bindExpression, targetFramework, diagnostics, out var target)) return null;
        if (!TryGetMemberName(document, syntax, diagnostics, out var name)) return null;
        if (!target.IsStatic && target.Type.IsArray && !name.Equals("Length", StringComparison.OrdinalIgnoreCase))
            return Reject(diagnostics, "PSB2601", $"CLR array member '{name}' does not preserve PowerShell null-member semantics; only Length is eligible.", span);

        var flags = BindingFlags.Public | BindingFlags.IgnoreCase |
                    (target.IsStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var members = target.Type.GetMember(name, MemberTypes.Field | MemberTypes.Property, flags)
            .Where(member => IsSupportedMember(member, targetFramework))
            .Where(static member => member switch
            {
                PropertyInfo property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0,
                FieldInfo => true,
                _ => false
            })
            .ToArray();
        if (members.Length != 1)
            return Reject(diagnostics, "PSB2602", members.Length == 0
                ? $"CLR member '{target.Type.FullName}.{name}' was not found as one target-compatible readable field or property."
                : $"CLR member '{target.Type.FullName}.{name}' is ambiguous on the conservative typed path.", span);

        var member = members[0];
        if (member is PropertyInfo && PowerShellRuntimeExceptionCatchPolicy.Contains(syntax) &&
            !(target.Type == typeof(string) || target.Type.IsArray && member.Name.Equals("Length", StringComparison.Ordinal)))
            return Reject(diagnostics, "PSB2603", $"CLR property read '{target.Type.FullName}.{member.Name}' inside a RuntimeException catch cannot preserve PowerShell error wrapping.", span);

        var resultType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        if (!PowerShellGeneratedTypePolicy.IsSupported(resultType, targetFramework))
            return Reject(diagnostics, "PSB2604", $"CLR member '{target.Type.FullName}.{member.Name}' returns target-incompatible type '{resultType.FullName}'.", span);
        if (!TrySelectReadBehavior(target, resultType, out var receiverBehavior))
            return Reject(diagnostics, "PSB2605", $"CLR member '{target.Type.FullName}.{member.Name}' on a potentially null receiver returns non-nullable value '{resultType.FullName}'.", span);

        return new PowerShellBoundClrMemberExpression(
            span,
            target.Type,
            member.Name,
            target.IsStatic,
            target.Receiver,
            receiverBehavior,
            new PowerShellTypeFact(resultType, PowerShellTypeFactProvenance.Inferred, "The semantic binder resolved one target-compatible CLR field or property."));
    }

    internal static PowerShellBoundExpression? BindInvocation(
        ParsedSourceDocument document,
        InvokeMemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!TryResolveTarget(document, syntax.Expression, bindExpression, targetFramework, diagnostics, out var target)) return null;
        if (!TryGetMemberName(document, syntax, diagnostics, out var name)) return null;

        var argumentSyntax = syntax.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        var arguments = new PowerShellBoundExpression[argumentSyntax.Length];
        for (var index = 0; index < argumentSyntax.Length; index++)
        {
            var argument = bindExpression(argumentSyntax[index], null);
            if (argument is null) return null;
            arguments[index] = argument;
        }

        MethodBase selected;
        PowerShellClrInvocationKind kind;
        Type resultType;
        if (target.IsStatic && name.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            selected = SelectBest(
                target.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(member => IsSupportedMember(member, targetFramework)),
                arguments,
                argumentSyntax,
                diagnostics,
                span,
                $"constructor for '{target.Type.FullName}'")!;
            if (selected is null) return null;
            kind = PowerShellClrInvocationKind.Constructor;
            resultType = target.Type;
        }
        else
        {
            var flags = BindingFlags.Public | (target.IsStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
            selected = SelectBest(
                target.Type.GetMethods(flags).Where(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    !candidate.IsSpecialName &&
                    !candidate.IsGenericMethodDefinition &&
                    !candidate.ContainsGenericParameters &&
                    IsSupportedMember(candidate, targetFramework)),
                arguments,
                argumentSyntax,
                diagnostics,
                span,
                $"method '{target.Type.FullName}.{name}'")!;
            if (selected is null) return null;
            kind = target.IsStatic ? PowerShellClrInvocationKind.StaticMethod : PowerShellClrInvocationKind.InstanceMethod;
            resultType = ((MethodInfo)selected).ReturnType;
            if (resultType == typeof(void))
                return Reject(diagnostics, "PSB2606", "Void CLR invocations require statement-output lowering and are not yet eligible for this bound expression path.", span);
        }

        if (!PowerShellGeneratedTypePolicy.IsSupported(resultType, targetFramework))
            return Reject(diagnostics, "PSB2607", $"CLR invocation returns target-incompatible type '{resultType.FullName}'.", span);
        if (!TrySelectInvocationBehavior(target, out var receiverBehavior))
            return Reject(diagnostics, "PSB2608", $"CLR method invocation '{target.Type.FullName}.{selected.Name}' on a potentially null receiver requires PowerShell runtime error identity.", span);

        var parameters = selected.GetParameters();
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] = NormalizeLiteralArgument(arguments[index], argumentSyntax[index], parameters[index].ParameterType);

        return new PowerShellBoundClrInvocationExpression(
            span,
            target.Type,
            selected is ConstructorInfo ? ".ctor" : selected.Name,
            kind,
            target.Receiver,
            receiverBehavior,
            arguments,
            parameters.Select(static parameter => parameter.ParameterType).ToArray(),
            new PowerShellTypeFact(resultType, PowerShellTypeFactProvenance.Inferred, "The semantic binder selected one exact target-compatible CLR overload."));
    }

    private static MethodBase? SelectBest<TMember>(
        IEnumerable<TMember> candidates,
        PowerShellBoundExpression[] arguments,
        ExpressionAst[] argumentSyntax,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        SourceSpan span,
        string description)
        where TMember : MethodBase
    {
        var matches = candidates
            .Select(candidate => new { Candidate = (MethodBase)candidate, Parameters = candidate.GetParameters() })
            .Where(match => match.Parameters.Length == arguments.Length &&
                            match.Parameters.All(static parameter => !parameter.ParameterType.IsByRef && !parameter.IsOut) &&
                            !match.Parameters.Any(static parameter => parameter.GetCustomAttribute<ParamArrayAttribute>() is not null))
            .Select(match => new { match.Candidate, Score = ScoreArguments(arguments, argumentSyntax, match.Parameters) })
            .Where(static match => match.Score >= 0)
            .OrderBy(static match => match.Score)
            .ThenBy(static match => match.Candidate.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (matches.Length == 0)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2609", $"No exact CLR overload was found for {description} with the bound argument types.", span));
            return null;
        }
        if (matches.Length > 1 && matches[0].Score == matches[1].Score)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2610", $"CLR overload resolution for {description} is ambiguous on the conservative typed path.", span));
            return null;
        }
        return matches[0].Candidate;
    }

    private static int ScoreArguments(PowerShellBoundExpression[] arguments, ExpressionAst[] syntax, ParameterInfo[] parameters)
    {
        var score = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var source = arguments[index].Type.ClrType;
            var target = parameters[index].ParameterType;
            if (source == target) continue;
            if (target.IsAssignableFrom(source)) { score += 1; continue; }
            if (PowerShellClrTypeSemantics.CanAssign(target, source)) { score += 2; continue; }
            if (target == typeof(char) && syntax[index] is StringConstantExpressionAst text && text.Value.Length == 1) { score += 3; continue; }
            if (target.IsEnum && syntax[index] is StringConstantExpressionAst enumText && TryResolveEnumLiteral(target, enumText.Value, out _)) { score += 3; continue; }
            return -1;
        }
        return score;
    }

    private static PowerShellBoundExpression NormalizeLiteralArgument(PowerShellBoundExpression argument, ExpressionAst syntax, Type targetType)
    {
        if (targetType == typeof(char) && syntax is StringConstantExpressionAst text && text.Value.Length == 1)
            return new PowerShellBoundLiteralExpression(argument.Span, text.Value[0], new PowerShellTypeFact(typeof(char), PowerShellTypeFactProvenance.Literal, "A one-character literal binds to the selected Char parameter."), PowerShellValueState.Known);
        if (targetType.IsEnum && syntax is StringConstantExpressionAst enumText && TryResolveEnumLiteral(targetType, enumText.Value, out var value))
            return new PowerShellBoundLiteralExpression(argument.Span, value, new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Literal, "A named enum literal binds to the selected enum parameter."), PowerShellValueState.Known);
        return argument;
    }

    private static bool TryResolveTarget(
        ParsedSourceDocument document,
        ExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        out Target target)
    {
        if (syntax is TypeExpressionAst typeExpression)
        {
            var type = typeExpression.TypeName.GetReflectionType();
            if (type is null || !PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2611", $"CLR type '{typeExpression.TypeName.FullName}' is not target-compatible.", PowerShellSourceParser.GetSpan(document, typeExpression.Extent)));
                target = default;
                return false;
            }
            target = new Target(type, true, null, true);
            return true;
        }

        var receiver = bindExpression(syntax, null);
        if (receiver is null) { target = default; return false; }
        var receiverType = receiver.Type.ClrType;
        if (Nullable.GetUnderlyingType(receiverType) is not null || receiverType == typeof(PSObject) || receiverType == typeof(PSCustomObject))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2612", $"Receiver type '{receiverType.FullName}' requires PowerShell boxing or adapted-object semantics.", receiver.Span));
            target = default;
            return false;
        }
        target = new Target(receiverType, false, receiver, IsKnownNonNull(receiver));
        return true;
    }

    private static bool TrySelectReadBehavior(Target target, Type resultType, out PowerShellClrReceiverBehavior behavior)
    {
        behavior = PowerShellClrReceiverBehavior.None;
        if (target.IsStatic || target.Type.IsValueType || target.IsKnownNonNull) return true;
        if (target.Type.IsArray) { behavior = PowerShellClrReceiverBehavior.NormalizeNullArrayLength; return true; }
        if (target.Type == typeof(string)) { behavior = PowerShellClrReceiverBehavior.NormalizeNullString; return true; }
        if (!resultType.IsValueType || Nullable.GetUnderlyingType(resultType) is not null)
        {
            behavior = PowerShellClrReceiverBehavior.PropagateNull;
            return true;
        }
        return false;
    }

    private static bool TrySelectInvocationBehavior(Target target, out PowerShellClrReceiverBehavior behavior)
    {
        behavior = PowerShellClrReceiverBehavior.None;
        if (target.IsStatic || target.Type.IsValueType || target.IsKnownNonNull) return true;
        if (target.Type == typeof(string) && target.Receiver?.Type.Provenance == PowerShellTypeFactProvenance.Explicit)
        {
            behavior = PowerShellClrReceiverBehavior.NormalizeNullString;
            return true;
        }
        return false;
    }

    private static bool IsKnownNonNull(PowerShellBoundExpression expression)
        => expression.ValueState == PowerShellValueState.Known && !expression.Type.ClrType.IsValueType ||
           expression is PowerShellBoundLiteralExpression { Value: not null } or PowerShellBoundArrayExpression ||
           expression is PowerShellBoundClrInvocationExpression { InvocationKind: PowerShellClrInvocationKind.Constructor };

    private static bool IsSupportedMember(MemberInfo member, string? targetFramework)
        => string.IsNullOrWhiteSpace(targetFramework) || PowerShellGeneratedMemberPolicy.IsSupported(member, targetFramework!);

    private static bool TryGetMemberName(
        ParsedSourceDocument document,
        MemberExpressionAst syntax,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        out string name)
    {
        if (syntax.Member is StringConstantExpressionAst member && !string.IsNullOrWhiteSpace(member.Value))
        {
            name = member.Value;
            return true;
        }
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2613", "Dynamic CLR member names require PowerShell runtime binding.", PowerShellSourceParser.GetSpan(document, syntax.Member.Extent)));
        name = string.Empty;
        return false;
    }

    private static bool TryResolveEnumLiteral(Type enumType, string value, out object resolved)
    {
        try
        {
            var candidate = Enum.Parse(enumType, value, ignoreCase: true);
            if (Enum.IsDefined(enumType, candidate)) { resolved = candidate; return true; }
        }
        catch (ArgumentException) { }
        catch (OverflowException) { }
        resolved = default!;
        return false;
    }

    private static PowerShellBoundExpression? Reject(
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string code,
        string message,
        SourceSpan span)
    {
        diagnostics.Add(new PowerShellSemanticDiagnostic(code, message, span));
        return null;
    }

    private readonly struct Target
    {
        internal Target(Type type, bool isStatic, PowerShellBoundExpression? receiver, bool isKnownNonNull)
        {
            Type = type;
            IsStatic = isStatic;
            Receiver = receiver;
            IsKnownNonNull = isKnownNonNull;
        }

        internal Type Type { get; }
        internal bool IsStatic { get; }
        internal PowerShellBoundExpression? Receiver { get; }
        internal bool IsKnownNonNull { get; }
    }
}
