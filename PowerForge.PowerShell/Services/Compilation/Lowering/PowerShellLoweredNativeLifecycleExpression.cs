namespace PowerForge;

internal sealed class PowerShellLoweredNativeLifecycleExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeLifecycleExpression(SourceSpan span, int clause) : base(span, typeof(bool))
        => Clause = clause;

    internal int Clause { get; }
}
