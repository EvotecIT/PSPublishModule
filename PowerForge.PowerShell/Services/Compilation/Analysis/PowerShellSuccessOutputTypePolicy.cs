namespace PowerForge;

/// <summary>Infers command record metadata independently of the callable CLR return ABI.</summary>
internal static class PowerShellSuccessOutputTypePolicy
{
    internal static Type? Resolve(PowerShellBoundFunction function, IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
    {
        var types = ResolveTypes(function, functions);
        return types.Length == 0 ? null : types.Length == 1 ? types[0] : typeof(object);
    }

    internal static Type[] ResolveTypes(PowerShellBoundFunction function, IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => ResolveTypes(function, functions, new HashSet<string>(StringComparer.Ordinal));

    private static Type[] ResolveTypes(PowerShellBoundFunction function, IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        HashSet<string> visiting)
    {
        if (!visiting.Add(function.Symbol.StableKey)) return new[] { typeof(object) };
        try
        {
            return PowerShellSemanticAnalyzer.EnumerateStatements(function.Body, descendIntoCaptures: false)
                .SelectMany(statement => ResolveStatement(statement, functions, visiting)).Distinct().ToArray();
        }
        finally { visiting.Remove(function.Symbol.StableKey); }
    }

    private static Type[] ResolveStatement(PowerShellBoundStatement statement,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions, HashSet<string> visiting)
    {
        // A hosted region can emit records without a typed output expression.
        // Captured regions do not contribute records to the enclosing command.
        if (statement is PowerShellBoundCommandRegionStatement) return new[] { typeof(object) };
        if (statement is PowerShellBoundStreamWriteStatement { Message: PowerShellBoundClrInvocationExpression invocation } &&
            invocation.Arguments.OfType<PowerShellBoundStrictSuccessSinkExpression>().SingleOrDefault() is { } sink)
            return new[] { sink.RecordType };
        var expression = PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement);
        // An unconsumed local command writes directly to its caller's success stream.
        // Its invocation has a void CLR value contract, but still contributes the
        // target command's record metadata.
        if (expression is null && statement is PowerShellBoundReturnStatement
            {
                Expression: PowerShellBoundInvocationExpression streamedCall
            } && functions.TryGetValue(streamedCall.Target.StableKey, out var streamedTarget))
            return ResolveTypes(streamedTarget, functions, visiting);
        if (expression is null) return Type.EmptyTypes;
        // Null records do not add a concrete record type.
        if (statement is PowerShellBoundStreamWriteStatement { RuntimeFree: true } &&
            expression.ValueState == PowerShellValueState.Null)
            return Type.EmptyTypes;
        if (statement is PowerShellBoundStreamWriteStatement { Provider.Adapter.EntryPoint: { } entryPoint })
            return new[] { entryPoint.ResultType switch
            {
                PowerShellCompilationProviderValueType.String => typeof(string),
                PowerShellCompilationProviderValueType.Int32 => typeof(int),
                PowerShellCompilationProviderValueType.Int64 => typeof(long),
                PowerShellCompilationProviderValueType.Double => typeof(double),
                PowerShellCompilationProviderValueType.Boolean => typeof(bool),
                _ => throw new ArgumentOutOfRangeException(nameof(entryPoint.ResultType))
            } };
        if (statement is PowerShellBoundStreamWriteStatement { OutputBinding: not PowerShellOutputBindingKind.Default } stream)
            return new[] { stream.OutputBinding == PowerShellOutputBindingKind.PositionalNoEnumeratePowerShell7
                ? typeof(object) : expression.Type.ClrType };
        if (expression is PowerShellBoundInvocationExpression call && functions.TryGetValue(call.Target.StableKey, out var target))
            return ResolveTypes(target, functions, visiting);
        var recordType = GetRecordType(expression.Type.ClrType);
        return recordType is null ? Type.EmptyTypes : new[] { recordType };
    }

    private static Type? GetRecordType(Type type)
    {
        if (type == typeof(void)) return null;
        if (type.IsArray) return type.GetElementType();
        if (type == typeof(string) || typeof(System.Collections.IDictionary).IsAssignableFrom(type)) return type;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || typeof(System.Collections.IEnumerator).IsAssignableFrom(type))
            return typeof(object);
        return type;
    }
}
