namespace PowerForge;

internal sealed class PowerShellLoweredRuntimeStateExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredRuntimeStateExpression(
        SourceSpan span,
        Type clrType,
        PowerShellRuntimeStateIntrinsicKind kind,
        string targetFramework,
        string semanticProfileId,
        PowerShellLoweredExpression[] arguments)
        : base(span, clrType)
    {
        Kind = kind;
        TargetFramework = targetFramework;
        SemanticProfileId = semanticProfileId;
        Arguments = arguments ?? Array.Empty<PowerShellLoweredExpression>();
    }

    internal PowerShellRuntimeStateIntrinsicKind Kind { get; }
    internal string TargetFramework { get; }
    internal string SemanticProfileId { get; }
    internal PowerShellImmutableArray<PowerShellLoweredExpression> Arguments { get; }
}
