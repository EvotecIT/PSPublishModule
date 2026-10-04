namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitLocalInvocation(PowerShellLoweredInvocationExpression invocation)
    {
        var arguments = invocation.Arguments.Select(EmitExpression).ToArray();
        var authored = invocation.AuthoredEvaluationOrder;
        var evaluateArguments = invocation.EvaluationTemporaryNames.Any(static name => name is not null);
        var temporaries = new Dictionary<int, string>();
        if (evaluateArguments)
        {
            foreach (var parameterIndex in authored)
                temporaries[parameterIndex] = invocation.EvaluationTemporaryNames[parameterIndex]
                    ?? throw new InvalidOperationException("Lowered local-call evaluation plan is missing its collision-free temporary name.");
            foreach (var pair in temporaries) arguments[pair.Key] = pair.Value;
        }
        var callArguments = arguments.ToList();
        if (invocation.RequiresPowerShellStatementErrors)
            callArguments.Add(invocation.StatementErrorContextTemporary);
        if (invocation.RequiresPowerShellStopping)
            callArguments.Add("__checkLoopInterrupts");
        if (invocation.RequiresPowerShellStreams)
            callArguments.AddRange(new[] { invocation.CapturesSuccessOutput ? invocation.CapturedOutputTemporary : "__writeOutput",
                "__writeVerbose", "__writeDebug", "__writeWarning", "__writeInformation", "__writeHost", "__writeError" });
        if (invocation.RequiresProviderCancellation)
            callArguments.Add("__providerCancellationToken");
        if (invocation.RequiresPowerShellCommandRegions)
            callArguments.AddRange(new[] { "__invokePowerShellRegion", "__invokePowerShellCapture" });
        if (invocation.RequiresPowerShellRuntimeState)
            callArguments.AddRange(new[] { "__shouldProcessTarget", "__shouldProcessAction", "__psVersion", "__whatIfPreference", "__runtimeState" });
        if (invocation.RequiresPowerShellModuleStateRead)
            callArguments.Add("__readPowerShellModuleVariable");
        if (invocation.RequiresPowerShellModuleStateWrite)
            callArguments.Add("__writePowerShellModuleVariable");
        if (invocation.RequiresBoundParameters) callArguments.Add(EmitBoundParameterSet(invocation.BoundParameterNames));
        var call = $"{PowerShellCSharpSymbolRenderer.Identifier(invocation.Target.Name)}({string.Join(", ", callArguments)})";
        if (invocation.CapturesSuccessOutput)
        {
            if (invocation.CapturesClrReturn)
                call = invocation.StatementErrorContextTemporary + ".WriteOutput(" + call + ", " +
                    invocation.CapturedOutputTemporary + ", " + EmitSourceExtentArguments(invocation.Span) + ")";
            call = "__statementErrors.CaptureFunction(" + PowerShellCSharpLiteral.QuoteString(invocation.Target.Name) + ", " +
                EmitSourceExtentArguments(invocation.Span) + ", (" + invocation.StatementErrorContextTemporary + ", " +
                invocation.CapturedOutputTemporary + ") => { " + call + "; })";
        }
        else if (invocation.RequiresPowerShellStatementErrors)
        {
            var context = invocation.StatementErrorContextTemporary;
            var error = invocation.StatementErrorTemporary;
            var result = invocation.ClrType == typeof(void) ? call + ";" : "return " + call + ";";
            call = "new " + (invocation.ClrType == typeof(void) ? "global::System.Action" :
                "global::System.Func<" + PowerShellCSharpSymbolRenderer.TypeName(invocation.ClrType) + ">") +
                "(() => { using (var " + context + " = __statementErrors.EnterFunction(" + PowerShellCSharpLiteral.QuoteString(invocation.Target.Name) + ", " + EmitSourceExtentArguments(invocation.Span) +
                ")) { try { " + result + " } catch (global::System.Exception " + error +
                ") when (" + StatementErrorContextType + ".IsOperationFailure(" + error + ")) { throw " + context + ".LeaveCommand(" + error + "); } } })()";
        }
        if (!evaluateArguments) return call;
        var evaluations = authored.Select(parameterIndex =>
            $"{PowerShellCSharpSymbolRenderer.TypeName(invocation.Arguments[parameterIndex].ClrType)} {temporaries[parameterIndex]} = {EmitExpression(invocation.Arguments[parameterIndex])};");
        if (invocation.ClrType == typeof(void))
            return $"new global::System.Action(() => {{ {string.Join(" ", evaluations)} {call}; }})()";
        return $"new global::System.Func<{PowerShellCSharpSymbolRenderer.TypeName(invocation.ClrType)}>(() => {{ {string.Join(" ", evaluations)} return {call}; }})()";
    }

    private string EmitSourceExtentArguments(SourceSpan span)
    {
        var function = _sourceFunction ?? throw new InvalidOperationException("Local invocation source requires its lowered function.");
        var text = string.Join("\n", function.SourceText.Replace("\r\n", "\n").Split('\n')
            .Skip(span.StartLine - function.Span.StartLine).Take(span.EndLine - span.StartLine + 1));
        return QuotePortableSourcePath(function.SourcePath) + ", " +
            $"{span.StartLine}, {span.StartColumn}, {span.EndLine}, {span.EndColumn}, " + PowerShellCSharpLiteral.QuoteString(text);
    }

    private string EmitBoundParameterSet(IEnumerable<string> names)
        => "new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.OrdinalIgnoreCase) { " +
           string.Join(", ", names.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).Select(PowerShellCSharpLiteral.QuoteString)) + " }";
}
