namespace PowerForge;

/// <summary>Infers command record metadata independently of the callable CLR return ABI.</summary>
internal static class PowerShellSuccessOutputTypePolicy
{
    internal static Type? Resolve(PowerShellBoundFunction function, IReadOnlyDictionary<string, PowerShellBoundFunction> functions)
        => Resolve(function, functions, new HashSet<string>(StringComparer.Ordinal));

    private static Type? Resolve(PowerShellBoundFunction function, IReadOnlyDictionary<string, PowerShellBoundFunction> functions,
        HashSet<string> visiting)
    {
        if (!visiting.Add(function.Symbol.StableKey)) return typeof(object);
        try
        {
            var types = PowerShellSemanticAnalyzer.EnumerateStatements(function.Body, descendIntoCaptures: false)
                .Select(statement => ResolveStatement(statement, functions, visiting))
                .Where(static type => type is not null).Distinct().ToArray();
            return types.Length == 0 ? null : types.Length == 1 ? types[0] : typeof(object);
        }
        finally { visiting.Remove(function.Symbol.StableKey); }
    }

    private static Type? ResolveStatement(PowerShellBoundStatement statement,
        IReadOnlyDictionary<string, PowerShellBoundFunction> functions, HashSet<string> visiting)
    {
        // A hosted region can emit records without a typed output expression.
        // Captured regions do not contribute records to the enclosing command.
        if (statement is PowerShellBoundCommandRegionStatement) return typeof(object);
        var expression = PowerShellSemanticAnalyzer.GetSuccessOutputExpression(statement);
        // An unconsumed local command writes directly to its caller's success stream.
        // Its invocation has a void CLR value contract, but still contributes the
        // target command's record metadata.
        if (expression is null && statement is PowerShellBoundReturnStatement
            {
                Expression: PowerShellBoundInvocationExpression streamedCall
            } && functions.TryGetValue(streamedCall.Target.StableKey, out var streamedTarget))
            return Resolve(streamedTarget, functions, visiting);
        if (expression is null) return null;
        if (statement is PowerShellBoundStreamWriteStatement { Provider.Adapter.EntryPoint: { } entryPoint })
            return entryPoint.ResultType switch
            {
                PowerShellCompilationProviderValueType.String => typeof(string),
                PowerShellCompilationProviderValueType.Int32 => typeof(int),
                PowerShellCompilationProviderValueType.Int64 => typeof(long),
                PowerShellCompilationProviderValueType.Double => typeof(double),
                PowerShellCompilationProviderValueType.Boolean => typeof(bool),
                _ => throw new ArgumentOutOfRangeException(nameof(entryPoint.ResultType))
            };
        if (statement is PowerShellBoundStreamWriteStatement { OutputBinding: not PowerShellOutputBindingKind.Default } stream)
            return stream.OutputBinding == PowerShellOutputBindingKind.PositionalNoEnumeratePowerShell7
                ? typeof(object) : expression.Type.ClrType;
        return expression is PowerShellBoundInvocationExpression call && functions.TryGetValue(call.Target.StableKey, out var target)
            ? Resolve(target, functions, visiting) : GetRecordType(expression.Type.ClrType);
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
