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
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.Operator.ToString() != "Equals" ||
            memberSyntax.Expression is not (VariableExpressionAst or TypeExpressionAst))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2614", "Typed CLR member mutation requires a direct local-variable or static CLR member assignment with simple '='; other receivers remain on the PowerShell runtime path.", span));
            return null;
        }
        if (!TryResolveTarget(document, memberSyntax.Expression, bindExpression, targetFramework, diagnostics, out var target))
            return null;
        if (!TryGetMemberName(document, memberSyntax, diagnostics, out var name)) return null;
        if (!target.IsStatic && target.Receiver!.Type.DictionaryValueKind == PowerShellDictionaryValueKind.HelpMetadata)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2934", "The bounded runtime-free Get-Help metadata view is immutable.", span));
            return null;
        }
        if (!target.IsStatic && target.Type == typeof(PSObject) && target.Receiver!.Type.TryGetKnownProperty(name, out var knownProperty))
        {
            var propertyValue = bindExpression(syntax.Right, knownProperty.ClrType == typeof(object) ? null : knownProperty.ClrType);
            if (propertyValue is null ||
                knownProperty.ClrType != typeof(object) && !PowerShellClrTypeSemantics.CanAssign(knownProperty.ClrType, propertyValue.Type.ClrType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2618", $"Known note-property assignment value is not assignable to '{knownProperty.ClrType.FullName}'.", propertyValue?.Span ?? span));
                return null;
            }
            return new PowerShellBoundClrMemberAssignmentStatement(
                span,
                target.Receiver,
                target.Type,
                name,
                PowerShellClrReceiverBehavior.PowerShellAdapter,
                propertyValue);
        }
        if (!target.IsStatic && target.Type == typeof(PSObject))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2612", "PSCustomObject mutation is compiled only for a statically known note-property shape; other members require preservation of adapted-object identity.", span));
            return null;
        }
        var receiverBehavior = PowerShellClrReceiverBehavior.None;
        if (!target.IsStatic && !target.Type.IsValueType && !target.IsKnownNonNull && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects))
            receiverBehavior = PowerShellClrReceiverBehavior.PowerShellRuntimeException;
        else if (!target.IsStatic && !target.Type.IsValueType && !target.IsKnownNonNull)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2615", "CLR member mutation on a potentially null receiver requires PowerShell runtime-error identity.", span));
            return null;
        }
        var flags = BindingFlags.Public | BindingFlags.IgnoreCase |
                    (target.IsStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var members = target.Type.GetMember(name, MemberTypes.Field | MemberTypes.Property, flags)
            .Where(member => IsSupportedMember(member, targetFramework))
            .Where(static member => member switch
            {
                PropertyInfo property => property.GetMethod is { IsPublic: true } && property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0,
                FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
                _ => false
            })
            .ToArray();
        if (!target.IsStatic && members.Length == 0 &&
            typeof(System.Collections.IDictionary).IsAssignableFrom(target.Type) &&
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects))
        {
            var adaptedValue = bindExpression(syntax.Right, typeof(object));
            if (adaptedValue is null) return null;
            return new PowerShellBoundClrMemberAssignmentStatement(
                span,
                target.Receiver!,
                target.Type,
                name,
                PowerShellClrReceiverBehavior.PowerShellAdapter,
                adaptedValue);
        }
        if (members.Length != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2616", members.Length == 0
                ? $"CLR member '{target.Type.FullName}.{name}' was not found as one target-compatible readable and writable member."
                : $"Writable CLR member '{target.Type.FullName}.{name}' is ambiguous.", span));
            return null;
        }
        if (members[0] is PropertyInfo && PowerShellRuntimeExceptionCatchPolicy.Contains(memberSyntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2617", $"CLR property assignment '{target.Type.FullName}.{members[0].Name}' observed by a RuntimeException catch cannot preserve PowerShell error wrapping.", span));
            return null;
        }
        var memberType = members[0] is PropertyInfo property ? property.PropertyType : ((FieldInfo)members[0]).FieldType;
        var value = bindExpression(syntax.Right, memberType);
        if (value is null || !PowerShellGeneratedTypePolicy.IsSupported(memberType, targetFramework) || !PowerShellClrTypeSemantics.CanAssign(memberType, value.Type.ClrType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2618", $"Member assignment value is not assignable to '{memberType.FullName}'.", value?.Span ?? span));
            return null;
        }
        return new PowerShellBoundClrMemberAssignmentStatement(span, target.Receiver, target.Type, members[0].Name, receiverBehavior, value);
    }

    internal static PowerShellBoundExpression? BindMember(
        ParsedSourceDocument document,
        MemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!TryResolveTarget(document, syntax.Expression, bindExpression, targetFramework, diagnostics, out var target)) return null;
        if (!TryGetMemberName(document, syntax, diagnostics, out var name)) return null;
        if (!target.IsStatic && target.Receiver!.Type.DictionaryValueKind == PowerShellDictionaryValueKind.HelpMetadata)
        {
            if (!target.Receiver.Type.TryGetKnownProperty(name, out var helpProperty))
                return Reject(diagnostics, "PSB2933", $"The bounded runtime-free Get-Help contract does not expose property '{name}'.", span);
            return new PowerShellBoundClrMemberExpression(
                span,
                target.Type,
                name,
                false,
                target.Receiver,
                PowerShellClrReceiverBehavior.DictionaryKeyLookup,
                helpProperty);
        }
        var flags = BindingFlags.Public | BindingFlags.IgnoreCase |
                    (target.IsStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var members = GetReadableMembers(target.Type, name, flags, targetFramework);
        if (!target.IsStatic && typeof(System.Collections.IDictionary).IsAssignableFrom(target.Type))
        {
            if (members.Length > 1)
                return Reject(diagnostics, "PSB2602", $"CLR dictionary fallback member '{target.Type.FullName}.{name}' is ambiguous on the conservative typed path.", span);
            var resolvedName = members.Length == 1 ? members[0].Name : name;
            return new PowerShellBoundClrMemberExpression(
                span,
                target.Type,
                resolvedName,
                false,
                target.Receiver,
                members.Length == 1
                    ? PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback
                    : PowerShellClrReceiverBehavior.DictionaryKeyLookup,
                new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Inferred, "A statically typed IDictionary member is resolved dynamically as key-first with an optional CLR-member fallback."));
        }
        if (!target.IsStatic && target.Type == typeof(PSObject) && target.Receiver!.Type.TryGetKnownProperty(name, out var knownProperty))
        {
            return new PowerShellBoundClrMemberExpression(
                span,
                target.Type,
                name,
                false,
                target.Receiver,
                PowerShellClrReceiverBehavior.PowerShellAdapter,
                knownProperty);
        }
        if (!target.IsStatic && target.Type == typeof(PSObject))
            return Reject(diagnostics, "PSB2612", "PSCustomObject reads are compiled only for a statically known note-property shape; other members require preservation of adapted-object identity.", span);
        if (!target.IsStatic && IsClrArray(target.Type) && name.Equals("Count", StringComparison.OrdinalIgnoreCase))
        {
            return new PowerShellBoundClrMemberExpression(
                span,
                target.Type,
                nameof(Array.Length),
                false,
                target.Receiver,
                PowerShellClrReceiverBehavior.NormalizeNullCount,
                new PowerShellTypeFact(typeof(int), PowerShellTypeFactProvenance.Inferred, "PowerShell's adapted Count member on a statically typed CLR array is its total CLR Length, normalized to zero for a null receiver."));
        }
        if (!target.IsStatic && target.Type.IsArray && !name.Equals("Length", StringComparison.OrdinalIgnoreCase))
            return Reject(diagnostics, "PSB2601", $"CLR array member '{name}' does not preserve PowerShell null-member semantics; only Length and the adapted Count member are eligible.", span);

        if (members.Length == 0 &&
            !target.IsStatic &&
            name.Equals("Count", StringComparison.OrdinalIgnoreCase) &&
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects))
        {
            return new PowerShellBoundClrMemberExpression(
                span,
                target.Type,
                name,
                false,
                target.Receiver,
                PowerShellClrReceiverBehavior.PowerShellAdapter,
                new PowerShellTypeFact(typeof(int), PowerShellTypeFactProvenance.Inferred, "PowerShell's adapted Count member is evaluated by the hosted runtime."));
        }
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
        if (!target.IsStatic && !target.IsKnownNonNull &&
            member.Name.Equals("Length", StringComparison.OrdinalIgnoreCase) &&
            !target.Type.IsArray && target.Type != typeof(string) && resultType != typeof(int))
            return Reject(
                diagnostics,
                "PSB2605",
                $"Potentially null CLR member '{target.Type.FullName}.{member.Name}' cannot preserve PowerShell's Int32 zero for null while returning '{resultType.FullName}' for a concrete receiver.",
                span);
        var receiverBehavior = SelectReadBehavior(target, member.Name, resultType);
        var boundResultType = receiverBehavior == PowerShellClrReceiverBehavior.PropagateNull &&
                              resultType.IsValueType && Nullable.GetUnderlyingType(resultType) is null
            ? typeof(Nullable<>).MakeGenericType(resultType)
            : resultType;

        return new PowerShellBoundClrMemberExpression(
            span,
            target.Type,
            member.Name,
            target.IsStatic,
            target.Receiver,
            receiverBehavior,
            new PowerShellTypeFact(boundResultType, PowerShellTypeFactProvenance.Inferred, "The semantic binder resolved one target-compatible CLR field or property, including PowerShell null propagation."));
    }

    internal static PowerShellBoundExpression? BindInvocation(
        ParsedSourceDocument document,
        InvokeMemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!TryResolveTarget(document, syntax.Expression, bindExpression, targetFramework, diagnostics, out var target)) return null;
        if (!TryGetMemberName(document, syntax, diagnostics, out var name)) return null;
        if (!target.IsStatic && target.Receiver!.Type.DictionaryValueKind == PowerShellDictionaryValueKind.HelpMetadata)
            return Reject(diagnostics, "PSB2934", "The bounded runtime-free Get-Help metadata view does not expose mutable CLR dictionary methods.", span);
        if (target.Receiver is PowerShellBoundRuntimeStateExpression { Kind: PowerShellRuntimeStateIntrinsicKind.ErrorCollection })
            return Reject(diagnostics, "PSB2620", "The bounded $Error collection is a read-only invocation snapshot; method invocation remains on the PowerShell runtime path.", span);
        if (!target.IsStatic && target.Type == typeof(PSObject))
            return Reject(diagnostics, "PSB2612", "PSCustomObject method invocation requires preservation of adapted-object identity and remains on the PowerShell runtime path.", span);

        var argumentSyntax = syntax.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        if (target.IsStatic && name.Equals("new", StringComparison.OrdinalIgnoreCase))
            return BindConstructor(
                document,
                syntax,
                target.Type,
                argumentSyntax,
                bindExpression,
                targetFramework,
                diagnostics);

        var arguments = new PowerShellBoundExpression[argumentSyntax.Length];
        for (var index = 0; index < argumentSyntax.Length; index++)
        {
            var argument = bindExpression(argumentSyntax[index], null);
            if (argument is null) return null;
            arguments[index] = argument;
        }

        var flags = BindingFlags.Public | (target.IsStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
        var selected = SelectBest(
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
        var kind = target.IsStatic ? PowerShellClrInvocationKind.StaticMethod : PowerShellClrInvocationKind.InstanceMethod;
        var resultType = ((MethodInfo)selected).ReturnType;

        if (!PowerShellGeneratedTypePolicy.IsSupported(resultType, targetFramework))
            return Reject(diagnostics, "PSB2607", $"CLR invocation returns target-incompatible type '{resultType.FullName}'.", span);
        if (PowerShellRuntimeExceptionCatchPolicy.Contains(syntax))
            return Reject(diagnostics, "PSB2619", $"CLR method invocation '{target.Type.FullName}.{selected.Name}' inside a RuntimeException catch cannot preserve PowerShell runtime-error wrapping.", span);
        if (!TrySelectInvocationBehavior(target, capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects), out var receiverBehavior))
            return Reject(diagnostics, "PSB2608", $"CLR method invocation '{target.Type.FullName}.{selected.Name}' on a potentially null receiver requires PowerShell runtime-error identity.", span);

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

    internal static PowerShellBoundExpression? BindConstructor(
        ParsedSourceDocument document,
        Ast syntax,
        Type targetType,
        ExpressionAst[] argumentSyntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (!PowerShellGeneratedTypePolicy.IsSupported(targetType, targetFramework))
            return Reject(diagnostics, "PSB2611", $"CLR type '{targetType.FullName}' is not available in the generated project reference set for the requested target.", span);

        var arguments = new PowerShellBoundExpression[argumentSyntax.Length];
        for (var index = 0; index < argumentSyntax.Length; index++)
        {
            var argument = bindExpression(argumentSyntax[index], null);
            if (argument is null) return null;
            arguments[index] = argument;
        }
        var selected = SelectBest(
            targetType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => IsSupportedMember(member, targetFramework)),
            arguments,
            argumentSyntax,
            diagnostics,
            span,
            $"constructor for '{targetType.FullName}'")!;
        if (selected is null) return null;
        if (PowerShellRuntimeExceptionCatchPolicy.Contains(syntax))
            return Reject(diagnostics, "PSB2619", $"CLR constructor invocation '{targetType.FullName}' inside a RuntimeException catch cannot preserve PowerShell runtime-error wrapping.", span);

        var parameters = selected.GetParameters();
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] = NormalizeLiteralArgument(arguments[index], argumentSyntax[index], parameters[index].ParameterType);

        return new PowerShellBoundClrInvocationExpression(
            span,
            targetType,
            ".ctor",
            PowerShellClrInvocationKind.Constructor,
            null,
            PowerShellClrReceiverBehavior.None,
            arguments,
            parameters.Select(static parameter => parameter.ParameterType).ToArray(),
            new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Inferred, "The semantic binder selected one exact target-compatible CLR constructor."));
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
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2611", $"CLR type '{typeExpression.TypeName.FullName}' is not available in the generated project reference set for the requested target.", PowerShellSourceParser.GetSpan(document, typeExpression.Extent)));
                target = default;
                return false;
            }
            target = new Target(type, true, null, true);
            return true;
        }

        var receiver = bindExpression(syntax, null);
        if (receiver is null) { target = default; return false; }
        var receiverType = receiver.Type.ClrType;
        if (Nullable.GetUnderlyingType(receiverType) is not null ||
            receiverType == typeof(PSObject) && receiver.Type.KnownProperties.Count == 0 ||
            receiverType == typeof(PSCustomObject) ||
            receiver.Type.Explanation.Contains("SwitchParameter", StringComparison.Ordinal))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2612", $"Receiver type '{receiverType.FullName}' requires PowerShell boxing semantics or preservation of adapted-object identity, including SwitchParameter identity.", receiver.Span));
            target = default;
            return false;
        }
        target = new Target(receiverType, false, receiver, IsKnownNonNull(receiver));
        return true;
    }

    private static PowerShellClrReceiverBehavior SelectReadBehavior(Target target, string memberName, Type resultType)
    {
        if (target.IsStatic || target.Type.IsValueType || target.IsKnownNonNull) return PowerShellClrReceiverBehavior.None;
        if (target.Type.IsArray) return PowerShellClrReceiverBehavior.NormalizeNullArrayLength;
        if (target.Type == typeof(string)) return PowerShellClrReceiverBehavior.NormalizeNullString;
        if ((memberName.Equals("Count", StringComparison.OrdinalIgnoreCase) ||
             memberName.Equals("Length", StringComparison.OrdinalIgnoreCase)) && resultType == typeof(int))
            return PowerShellClrReceiverBehavior.NormalizeNullCount;
        return PowerShellClrReceiverBehavior.PropagateNull;
    }

    private static bool IsClrArray(Type type)
        => type == typeof(Array) || type.IsArray;

    private static bool TrySelectInvocationBehavior(Target target, bool allowPowerShellRuntimeErrors, out PowerShellClrReceiverBehavior behavior)
    {
        behavior = PowerShellClrReceiverBehavior.None;
        if (target.IsStatic || target.Type.IsValueType || target.IsKnownNonNull) return true;
        if (target.Type == typeof(string) && target.Receiver?.Type.Provenance == PowerShellTypeFactProvenance.Explicit)
        {
            behavior = PowerShellClrReceiverBehavior.NormalizeNullString;
            return true;
        }
        if (allowPowerShellRuntimeErrors)
        {
            behavior = PowerShellClrReceiverBehavior.PowerShellRuntimeException;
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

    private static MemberInfo[] GetReadableMembers(Type type, string name, BindingFlags flags, string? targetFramework)
    {
        var declaringTypes = type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()) : new[] { type };
        return declaringTypes
            .SelectMany(candidate => candidate.GetMember(name, MemberTypes.Field | MemberTypes.Property, flags))
            .Distinct()
            .Where(member => IsSupportedMember(member, targetFramework))
            .Where(static member => member switch
            {
                PropertyInfo property => property.GetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0,
                FieldInfo => true,
                _ => false
            })
            .ToArray();
    }

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
