namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
    private static PowerShellLoweredExpression LowerExpression(
        PowerShellBoundExpression expression,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => expression switch
        {
            PowerShellBoundNativeLifecycleExpression lifecycle => new PowerShellLoweredNativeLifecycleExpression(lifecycle.Span, lifecycle.Clause),
            PowerShellBoundLiteralExpression literal => new PowerShellLoweredLiteralExpression(literal.Span, literal.Type.ClrType, literal.Value),
            PowerShellBoundNativeVariableExpression variable => new PowerShellLoweredNativeVariableExpression(
                variable.Span, variable.Name, variable.InExpandableString, variable.SourcePath, variable.SourceText, variable.DirectLocal),
            PowerShellBoundVariableExpression variable => new PowerShellLoweredVariableExpression(variable.Span, variable.Type.ClrType, variable.Symbol),
            PowerShellBoundConstantBooleanDelegateExpression booleanDelegate => new PowerShellLoweredConstantBooleanDelegateExpression(
                booleanDelegate.Span,
                booleanDelegate.Type.ClrType,
                booleanDelegate.ParameterTypes.ToArray(),
                Enumerable.Range(0, booleanDelegate.ParameterTypes.Length)
                    .Select(_ => names.Allocate("pf_delegate_argument"))
                    .ToArray(),
                booleanDelegate.Value),
            PowerShellBoundRuntimeStateExpression runtime => new PowerShellLoweredRuntimeStateExpression(
                runtime.Span,
                runtime.Type.ClrType,
                runtime.Kind,
                runtime.TargetFramework,
                runtime.SemanticProfileId,
                runtime.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                runtime.Provider),
            PowerShellBoundCommandAvailabilityExpression discovery => new PowerShellLoweredCommandAvailabilityExpression(
                discovery.Span,
                LowerExpression(discovery.Name, functions, names, targetCapabilities),
                discovery.ErrorAction,
                discovery.Provider),
            PowerShellBoundHostedBooleanCommandExpression hostedBoolean => new PowerShellLoweredHostedBooleanCommandExpression(
                hostedBoolean.Span,
                hostedBoolean.Provider,
                hostedBoolean.Arguments.Select(argument => new PowerShellLoweredHostedCommandArgument(
                    argument.ParameterName,
                    argument.Value is null ? null : LowerExpression(argument.Value, functions, names, targetCapabilities))).ToArray()),
            PowerShellBoundParameterPresenceExpression presence => new PowerShellLoweredParameterPresenceExpression(presence.Span, presence.ParameterName),
            PowerShellBoundConversionExpression conversion => new PowerShellLoweredConversionExpression(
                conversion.Span,
                conversion.Type.ClrType,
                LowerExpression(conversion.Operand, functions, names, targetCapabilities),
                conversion.UsePowerShellLanguageRuntime,
                conversion.UsePowerShellTruthiness,
                conversion.NormalizeNullString, conversion.NativeSourcePath, conversion.NativeSourceText, conversion.NativePostTestCondition, conversion.UseNativeConversion, conversion.UseNativeCustomObjectConversion),
            PowerShellBoundBinaryExpression binary => new PowerShellLoweredBinaryExpression(
                binary.Span,
                binary.Type.ClrType,
                binary.Operation,
                LowerExpression(binary.Left, functions, names, targetCapabilities),
                LowerExpression(binary.Right, functions, names, targetCapabilities),
                IsNullOrderedComparison(binary.Operation) ? names.Allocate("pf_null_order_left") :
                    binary.PreserveStatementErrors ? names.Allocate("pf_binary_left") : null,
                IsNullOrderedComparison(binary.Operation) ? names.Allocate("pf_null_order_right") :
                    binary.PreserveStatementErrors ? names.Allocate("pf_binary_right") : null,
                binary.PreserveStatementErrors, binary.UsesNativeInvocation, binary.NativeIgnoreCase),
            PowerShellBoundUnaryExpression unary => new PowerShellLoweredUnaryExpression(
                unary.Span,
                unary.Type.ClrType,
                unary.Operation,
                LowerExpression(unary.Operand, functions, names, targetCapabilities)),
            PowerShellBoundTypeTestExpression typeTest => new PowerShellLoweredTypeTestExpression(
                typeTest.Span,
                LowerExpression(typeTest.Operand, functions, names, targetCapabilities),
                typeTest.TargetType,
                typeTest.Negate),
            PowerShellBoundRegexExpression regex => new PowerShellLoweredRegexExpression(
                regex.Span,
                regex.Type.ClrType,
                regex.Operation,
                LowerExpression(regex.Input, functions, names, targetCapabilities),
                LowerExpression(regex.Pattern, functions, names, targetCapabilities),
                regex.Replacement is null ? null : LowerExpression(regex.Replacement, functions, names, targetCapabilities),
                regex.IgnoreCase),
            PowerShellBoundWildcardExpression wildcard => new PowerShellLoweredWildcardExpression(
                wildcard.Span,
                LowerExpression(wildcard.Input, functions, names, targetCapabilities),
                LowerExpression(wildcard.Pattern, functions, names, targetCapabilities),
                wildcard.IgnoreCase,
                wildcard.Negate,
                names.Allocate("pf_wildcard_left"),
                names.Allocate("pf_wildcard_right")),
            PowerShellBoundMembershipExpression membership => new PowerShellLoweredMembershipExpression(
                membership.Span,
                LowerExpression(membership.Left, functions, names, targetCapabilities),
                LowerExpression(membership.Right, functions, names, targetCapabilities),
                membership.ElementType,
                membership.CollectionOnRight,
                membership.IgnoreCase,
                membership.Negate,
                names.Allocate("pf_membership_left"),
                names.Allocate("pf_membership_right"),
                names.Allocate("pf_membership_item")),
            PowerShellBoundStringSplitExpression split => new PowerShellLoweredStringSplitExpression(
                split.Span,
                LowerExpression(split.Input, functions, names, targetCapabilities),
                LowerExpression(split.Pattern, functions, names, targetCapabilities),
                split.IgnoreCase),
            PowerShellBoundStringJoinExpression join => new PowerShellLoweredStringJoinExpression(
                join.Span,
                LowerExpression(join.Values, functions, names, targetCapabilities),
                LowerExpression(join.Separator, functions, names, targetCapabilities),
                names.Allocate("pf_join_left"),
                names.Allocate("pf_join_right"), join.NativeSourcePath, join.NativeSourceText, join.IsUnary),
            PowerShellBoundInterpolatedStringExpression interpolated => new PowerShellLoweredInterpolatedStringExpression(
                interpolated.Span,
                interpolated.Parts.Select(part => new PowerShellLoweredInterpolatedStringPart(
                    part.Text,
                    part.Expression is null ? null : LowerExpression(part.Expression, functions, names, targetCapabilities),
                    part.IsInt32OrDouble ? names.Allocate("pf_interpolation_number") : string.Empty)).ToArray(), interpolated.UsesNativeInvocation),
            PowerShellBoundMutationExpression mutation => new PowerShellLoweredMutationExpression(
                mutation.Span,
                mutation.Type.ClrType,
                mutation.Target,
                mutation.TargetClrType,
                mutation.Operation,
                mutation.Value is null ? null : LowerExpression(mutation.Value, functions, names, targetCapabilities),
                mutation.NormalizeNullString,
                mutation.IntegralSemantics, mutation.PreserveStatementErrors,
                mutation.NativeTargetRead is null ? null : (PowerShellLoweredNativeVariableExpression)
                    LowerExpression(mutation.NativeTargetRead, functions, names, targetCapabilities), mutation.NativeSourceText, mutation.NativeSetSequencePoint, mutation.NativeAssignmentTarget),
            PowerShellBoundArrayExpression array => new PowerShellLoweredArrayExpression(
                array.Span,
                array.Type.ClrType,
                array.Kind,
                array.Elements.Select(element => LowerExpression(element, functions, names, targetCapabilities)).ToArray()),
            PowerShellBoundNativeMemberExpression memberRead => new PowerShellLoweredNativeMemberExpression(
                memberRead.Span, LowerExpression(memberRead.Receiver, functions, names, targetCapabilities), memberRead.Name),
            PowerShellBoundNativeCommandExpression nativeCommand => new PowerShellLoweredNativeCommandExpression(nativeCommand.Span,
                nativeCommand.Source, nativeCommand.SourcePath, nativeCommand.SourceDocument, nativeCommand.PreservePartialOutput, LowerCommandStages(nativeCommand.Stages)),
            PowerShellBoundNativeInvocationExpression nativeInvocation => new PowerShellLoweredNativeInvocationExpression(nativeInvocation.Span,
                nativeInvocation.Receiver is null ? null : LowerExpression(nativeInvocation.Receiver, functions, names, targetCapabilities),
                nativeInvocation.LiteralTargetType, nativeInvocation.Name, nativeInvocation.IsStatic,
                nativeInvocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                nativeInvocation.TargetConstraint, nativeInvocation.ArgumentConstraints.ToArray()),
            PowerShellBoundNativeIndexExpression nativeIndex => new PowerShellLoweredNativeIndexExpression(nativeIndex.Span,
                LowerExpression(nativeIndex.Receiver, functions, names, targetCapabilities),
                nativeIndex.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                nativeIndex.TargetConstraint, nativeIndex.IndexConstraint),
            PowerShellBoundNativeCollectionExpression collection => new PowerShellLoweredNativeCollectionExpression(
                collection.Span, collection.SourcePath,
                collection.Items.Select(item => new PowerShellLoweredNativeCollectionItem(item.Span, item.SourceText,
                    LowerExpression(item.Value, functions, names, targetCapabilities), item.SetSuccess,
                    names.Allocate("pf_collection_value"), names.Allocate("pf_collection_error"))).ToArray(),
                collection.ShareEmptyResult, names.Allocate("pf_collection_result"), collection.SingleExpression),
            PowerShellBoundArrayConcatenationExpression concatenation => new PowerShellLoweredArrayConcatenationExpression(
                concatenation.Span,
                LowerExpression(concatenation.Left, functions, names, targetCapabilities),
                LowerExpression(concatenation.Right, functions, names, targetCapabilities),
                concatenation.EnumerateRight),
            PowerShellBoundArrayCopyExpression copy => new PowerShellLoweredArrayCopyExpression(copy.Span,
                LowerExpression(copy.Source, functions, names, targetCapabilities), copy.ShareEmptyResult,
                names.Allocate("pf_copy_source"), names.Allocate("pf_copy_result"), names.Allocate("pf_copy_index")),
            PowerShellBoundDictionaryExpression dictionary => new PowerShellLoweredDictionaryExpression(
                dictionary.Span,
                dictionary.Type.ClrType,
                dictionary.Kind,
                dictionary.Entries.Select(entry => new PowerShellLoweredDictionaryEntry(
                    LowerExpression(entry.Key, functions, names, targetCapabilities),
                    LowerExpression(entry.Value, functions, names, targetCapabilities))).ToArray()),
            PowerShellBoundPowerShellObjectExpression powerShellObject => new PowerShellLoweredPowerShellObjectExpression(
                powerShellObject.Span,
                powerShellObject.Properties.Select(property => new PowerShellLoweredNoteProperty(
                    property.Name,
                    LowerExpression(property.Value, functions, names, targetCapabilities))).ToArray(),
                names.Allocate("object")),
            PowerShellBoundIndexExpression index => new PowerShellLoweredIndexExpression(
                index.Span,
                index.Type.ClrType,
                LowerExpression(index.Target, functions, names, targetCapabilities),
                LowerExpression(index.Index, functions, names, targetCapabilities),
                index.Kind,
                index.UsePowerShellRuntimeErrors,
                names.Allocate("pf_index_target"),
                names.Allocate("pf_index_key")),
            PowerShellBoundClrMemberExpression member => new PowerShellLoweredClrMemberExpression(
                member.Span,
                member.Type.ClrType,
                member.DeclaringType,
                member.MemberName,
                member.IsStatic,
                member.Receiver is null ? null : LowerExpression(member.Receiver, functions, names, targetCapabilities),
                member.ReceiverBehavior,
                member.ReceiverBehavior is PowerShellClrReceiverBehavior.DictionaryKeyLookup or PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback
                    ? names.Allocate("pf_dictionary")
                    : string.Empty,
                member.ReceiverBehavior is PowerShellClrReceiverBehavior.DictionaryKeyLookup or PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback
                    ? names.Allocate("pf_value")
                    : string.Empty),
            PowerShellBoundClrInvocationExpression invocation => LowerClrInvocation(invocation, functions, names, targetCapabilities),
            PowerShellBoundInvocationExpression invocation when functions.TryGetValue(invocation.Target.StableKey, out var target) =>
                new PowerShellLoweredInvocationExpression(
                    invocation.Span,
                    target.Function.ReturnType.ClrType,
                    invocation.Target,
                    invocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
                    invocation.AuthoredEvaluationOrder.ToArray(),
                    invocation.BoundParameterNames.ToArray(),
                    CreateEvaluationTemporaryNames(invocation, names, target.RequiresPowerShellStatementErrors),
                    target.RequiresPowerShellBoundParameters,
                    target.RequiresPowerShellStreams,
                    target.RequiresProviderCancellation,
                    target.RequiresPowerShellCommandRegions,
                    target.RequiresPowerShellRuntimeState,
                    target.RequiresPowerShellModuleStateRead,
                    target.RequiresPowerShellModuleStateWrite,
                    target.RequiresPowerShellStatementErrors,
                    target.RequiresPowerShellStatementErrors ? names.Allocate("pf_call_error_context") : string.Empty,
                    target.RequiresPowerShellStatementErrors ? names.Allocate("pf_call_error") : string.Empty,
                    target.RequiresPowerShellStopping),
            _ => throw new InvalidOperationException($"Bound expression '{expression.GetType().Name}' reached typed lowering without an owner.")
        };

    private static bool IsNullOrderedComparison(PowerShellBoundBinaryOperator operation)
        => operation is PowerShellBoundBinaryOperator.NullOrderedLessThan or
            PowerShellBoundBinaryOperator.NullOrderedLessThanOrEqual or
            PowerShellBoundBinaryOperator.NullOrderedGreaterThan or
            PowerShellBoundBinaryOperator.NullOrderedGreaterThanOrEqual;

    private static string?[] CreateEvaluationTemporaryNames(
        PowerShellBoundInvocationExpression invocation,
        LoweredNameAllocator names,
        bool preserveCallerErrorScope)
    {
        var result = new string?[invocation.Arguments.Length];
        // Authored argument failures belong to the caller, before a nested command acquires its error identity.
        if (!preserveCallerErrorScope && invocation.AuthoredEvaluationOrder.SequenceEqual(invocation.AuthoredEvaluationOrder.OrderBy(static index => index)))
            return result;
        foreach (var parameterIndex in invocation.AuthoredEvaluationOrder)
            result[parameterIndex] = names.Allocate("pf_local_argument");
        return result;
    }
}
