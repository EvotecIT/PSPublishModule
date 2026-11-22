namespace PowerForge;

/// <summary>Preserves native indexing, slicing, and explicit overload constraints.</summary>
internal sealed class PowerShellBoundNativeIndexExpression : PowerShellBoundExpression
{
    internal PowerShellBoundNativeIndexExpression(SourceSpan span, PowerShellBoundExpression receiver,
        PowerShellBoundExpression[] arguments, Type? targetConstraint, Type? indexConstraint)
        : base(span, PowerShellTypeFact.Unknown, PowerShellValueState.Unknown,
            arguments.Aggregate(receiver.Effects | PowerShellSemanticEffect.Host | PowerShellSemanticEffect.TerminatingError,
                static (effects, argument) => effects | argument.Effects),
            arguments.Aggregate(receiver.Capabilities | PowerShellRequiredCapability.NativeFunctionBinding |
                PowerShellRequiredCapability.PowerShellHost | PowerShellRequiredCapability.PowerShellStatementErrors,
                static (capabilities, argument) => capabilities | argument.Capabilities))
    {
        Receiver = receiver;
        Arguments = arguments;
        TargetConstraint = targetConstraint;
        IndexConstraint = indexConstraint;
    }

    internal PowerShellBoundExpression Receiver { get; }
    internal PowerShellImmutableArray<PowerShellBoundExpression> Arguments { get; }
    internal Type? TargetConstraint { get; }
    internal Type? IndexConstraint { get; }
}
