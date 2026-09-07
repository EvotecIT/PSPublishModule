namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
    private static PowerShellLoweredClrInvocationExpression LowerClrInvocation(
        PowerShellBoundClrInvocationExpression invocation,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions,
        LoweredNameAllocator names,
        PowerShellCompilationCapability targetCapabilities)
        => new(
            invocation.Span,
            invocation.Type.ClrType,
            invocation.DeclaringType,
            invocation.MemberName,
            invocation.InvocationKind,
            invocation.Receiver is null ? null : LowerExpression(invocation.Receiver, functions, names, targetCapabilities),
            invocation.ReceiverBehavior,
            invocation.Arguments.Select(argument => LowerExpression(argument, functions, names, targetCapabilities)).ToArray(),
            invocation.ParameterTypes.ToArray(),
            invocation.PreserveStatementErrors,
            invocation.PreserveStatementErrors ? names.Allocate("pf_clr_receiver") : string.Empty,
            invocation.PreserveStatementErrors ? invocation.Arguments.Select(_ => names.Allocate("pf_clr_argument")).ToArray() : Array.Empty<string>(),
            invocation.PreserveStatementErrors ? names.Allocate("pf_clr_error") : string.Empty,
            invocation.ReceiverByReference,
            invocation.ArgumentConversions.ToArray(),
            invocation.ArgumentConversions.Any(static conversion => conversion.Kind != PowerShellClrArgumentConversionKind.None)
                ? invocation.Arguments.Select(_ => names.Allocate("pf_clr_raw_argument")).ToArray()
                : Array.Empty<string>());
}
