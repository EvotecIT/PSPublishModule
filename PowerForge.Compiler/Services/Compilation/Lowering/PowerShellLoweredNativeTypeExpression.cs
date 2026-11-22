namespace PowerForge;

/// <summary>Preserves qualified lexical type resolution and its authored source extent.</summary>
internal sealed class PowerShellLoweredNativeTypeExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredNativeTypeExpression(SourceSpan span, string name) : base(span, typeof(Type)) => Name = name;
    internal string Name { get; }
}
