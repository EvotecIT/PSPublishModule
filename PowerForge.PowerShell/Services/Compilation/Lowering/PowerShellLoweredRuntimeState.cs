namespace PowerForge;

internal sealed class PowerShellLoweredRuntimeStateExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredRuntimeStateExpression(
        SourceSpan span,
        Type clrType,
        PowerShellRuntimeStateIntrinsicKind kind,
        string targetFramework,
        PowerShellLoweredExpression[] arguments)
        : base(span, clrType)
    {
        Kind = kind;
        TargetFramework = targetFramework;
        Arguments = arguments ?? Array.Empty<PowerShellLoweredExpression>();
    }

    internal PowerShellRuntimeStateIntrinsicKind Kind { get; }
    internal string TargetFramework { get; }
    internal PowerShellLoweredExpression[] Arguments { get; }
}
