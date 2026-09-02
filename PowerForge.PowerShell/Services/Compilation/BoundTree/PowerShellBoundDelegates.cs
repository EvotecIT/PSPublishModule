namespace PowerForge;

/// <summary>A capture-free Boolean script block converted to one exact CLR delegate type.</summary>
internal sealed class PowerShellBoundConstantBooleanDelegateExpression : PowerShellBoundExpression
{
    internal PowerShellBoundConstantBooleanDelegateExpression(SourceSpan span, Type delegateType, Type[] parameterTypes, bool value)
        : base(
            span,
            new PowerShellTypeFact(delegateType, PowerShellTypeFactProvenance.Inferred, "A capture-free constant Boolean script block binds to one exact CLR delegate signature."),
            PowerShellValueState.Known)
    {
        ParameterTypes = parameterTypes ?? Array.Empty<Type>();
        Value = value;
    }

    internal PowerShellImmutableArray<Type> ParameterTypes { get; }
    internal bool Value { get; }
}
