namespace PowerForge;

/// <summary>Retains native variable-reference acquisition without converting its current value.</summary>
internal sealed class PowerShellLoweredNativeReferenceExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeReferenceExpression(SourceSpan span, string name, bool directLocal) : base(span, typeof(object))
    {
        Name = name;
        DirectLocal = directLocal;
    }

    internal string Name { get; }
    internal bool DirectLocal { get; }
}
