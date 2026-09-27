namespace PowerForge;

/// <summary>Retains native variable-reference acquisition without converting its current value.</summary>
internal sealed class PowerShellLoweredNativeReferenceExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeReferenceExpression(SourceSpan span, string name) : base(span, typeof(object))
        => Name = name;

    internal string Name { get; }
}
