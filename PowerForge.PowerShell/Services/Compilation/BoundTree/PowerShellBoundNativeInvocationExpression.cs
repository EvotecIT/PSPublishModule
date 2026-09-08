namespace PowerForge;

/// <summary>Invokes a member through native overload selection, preserving receiver identity and authored casts.</summary>
internal sealed class PowerShellBoundNativeInvocationExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeInvocationExpression(SourceSpan span, PowerShellBoundExpression? receiver,
        Type? literalTargetType, string name, bool isStatic, PowerShellBoundExpression[] arguments,
        Type? targetConstraint, Type?[] argumentConstraints)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            arguments.Aggregate((receiver?.Effects ?? PowerShellSemanticEffect.None) | PowerShellSemanticEffect.Host |
                PowerShellSemanticEffect.Mutation | PowerShellSemanticEffect.TerminatingError |
                (IsProcessStart(literalTargetType, name) ? PowerShellSemanticEffect.Process : PowerShellSemanticEffect.None),
                static (effects, argument) => effects | argument.Effects),
            arguments.Aggregate((receiver?.Capabilities ?? PowerShellRequiredCapability.None) | PowerShellRequiredCapability.NativeFunctionBinding |
                PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors |
                (IsProcessStart(literalTargetType, name) ? PowerShellRequiredCapability.NativeProcess : PowerShellRequiredCapability.None),
                static (capabilities, argument) => capabilities | argument.Capabilities))
    {
        Receiver = receiver;
        LiteralTargetType = literalTargetType;
        Name = name;
        IsStatic = isStatic;
        Arguments = arguments;
        TargetConstraint = targetConstraint;
        ArgumentConstraints = argumentConstraints;
    }

    internal PowerShellBoundExpression? Receiver { get; }
    internal Type? LiteralTargetType { get; }
    internal string Name { get; }
    internal bool IsStatic { get; }
    internal PowerShellImmutableArray<PowerShellBoundExpression> Arguments { get; }
    internal Type? TargetConstraint { get; }
    internal PowerShellImmutableArray<Type?> ArgumentConstraints { get; }

    private static bool IsProcessStart(Type? type, string name)
        => type == typeof(System.Diagnostics.Process) && name.Equals(nameof(System.Diagnostics.Process.Start), StringComparison.OrdinalIgnoreCase);
}
