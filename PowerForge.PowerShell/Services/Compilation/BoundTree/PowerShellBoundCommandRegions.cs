namespace PowerForge;

internal sealed class PowerShellBoundCommandRegionArgument
{
    internal PowerShellBoundCommandRegionArgument(PowerShellSymbolId symbol, bool isSwitch)
    {
        Symbol = symbol;
        IsSwitch = isSwitch;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal bool IsSwitch { get; }
}

internal sealed class PowerShellBoundCommandRegionStatement : PowerShellBoundStatement
{
    internal PowerShellBoundCommandRegionStatement(SourceSpan span, string source, PowerShellBoundCommandRegionArgument[] arguments)
        : base(span, PowerShellSemanticEffect.Host | PowerShellSemanticEffect.SuccessOutput, PowerShellRequiredCapability.CommandRegion)
    {
        Source = source;
        Arguments = arguments ?? Array.Empty<PowerShellBoundCommandRegionArgument>();
    }

    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandRegionArgument> Arguments { get; }
}

internal sealed class PowerShellBoundCommandCaptureStatement : PowerShellBoundStatement
{
    internal PowerShellBoundCommandCaptureStatement(
        SourceSpan span,
        PowerShellSymbolId target,
        Type targetType,
        string source,
        PowerShellBoundCommandRegionArgument[] arguments)
        : base(span, PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation, PowerShellRequiredCapability.CommandRegion)
    {
        Target = target;
        TargetType = targetType;
        Source = source;
        Arguments = arguments ?? Array.Empty<PowerShellBoundCommandRegionArgument>();
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandRegionArgument> Arguments { get; }
}
