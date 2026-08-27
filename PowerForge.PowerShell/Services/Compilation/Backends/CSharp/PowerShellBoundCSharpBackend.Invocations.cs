namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitLocalInvocation(PowerShellLoweredInvocationExpression invocation)
    {
        var arguments = invocation.Arguments.Select(EmitExpression).ToArray();
        var authored = invocation.AuthoredEvaluationOrder;
        var declarationOrder = authored.OrderBy(static index => index).ToArray();
        var reordered = !authored.SequenceEqual(declarationOrder);
        var temporaries = new Dictionary<int, string>();
        if (reordered)
        {
            foreach (var parameterIndex in authored)
                temporaries[parameterIndex] = invocation.EvaluationTemporaryNames[parameterIndex]
                    ?? throw new InvalidOperationException("Lowered local-call evaluation order is missing its collision-free temporary name.");
            foreach (var pair in temporaries) arguments[pair.Key] = pair.Value;
        }
        var callArguments = arguments.ToList();
        if (invocation.RequiresPowerShellStreams)
            callArguments.AddRange(new[] { "__writeOutput", "__writeVerbose", "__writeDebug", "__writeWarning", "__writeInformation", "__writeHost", "__writeError" });
        if (invocation.RequiresPowerShellCommandRegions)
            callArguments.AddRange(new[] { "__invokePowerShellRegion", "__invokePowerShellCapture" });
        if (invocation.RequiresPowerShellRuntimeState)
            callArguments.AddRange(new[] { "__shouldProcessTarget", "__shouldProcessAction", "__psVersion", "__whatIfPreference", "__runtimeState" });
        if (invocation.RequiresBoundParameters) callArguments.Add(EmitBoundParameterSet(invocation.BoundParameterNames));
        var call = $"{PowerShellCSharpSymbolRenderer.Identifier(invocation.Target.Name)}({string.Join(", ", callArguments)})";
        if (!reordered) return call;
        var evaluations = authored.Select(parameterIndex =>
            $"{PowerShellCSharpSymbolRenderer.TypeName(invocation.Arguments[parameterIndex].ClrType)} {temporaries[parameterIndex]} = {EmitExpression(invocation.Arguments[parameterIndex])};");
        return $"new global::System.Func<{PowerShellCSharpSymbolRenderer.TypeName(invocation.ClrType)}>(() => {{ {string.Join(" ", evaluations)} return {call}; }})()";
    }

    private static string EmitBoundParameterSet(IEnumerable<string> names)
        => "new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.OrdinalIgnoreCase) { " +
           string.Join(", ", names.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).Select(PowerShellCSharpLiteral.QuoteString)) + " }";
}
