namespace PowerForge;

internal sealed class PowerShellLoweredConstantBooleanDelegateExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredConstantBooleanDelegateExpression(
        SourceSpan span,
        Type delegateType,
        Type[] parameterTypes,
        string[] parameterNames,
        bool value)
        : base(span, delegateType)
    {
        ParameterTypes = parameterTypes ?? Array.Empty<Type>();
        ParameterNames = parameterNames ?? Array.Empty<string>();
        Value = value;
    }

    internal PowerShellImmutableArray<Type> ParameterTypes { get; }
    internal PowerShellImmutableArray<string> ParameterNames { get; }
    internal bool Value { get; }
}
