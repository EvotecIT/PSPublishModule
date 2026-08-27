namespace PowerForge;

internal sealed class PowerShellBoundPipelineSymbol
{
    internal PowerShellBoundPipelineSymbol(PowerShellSymbolId symbol) => Symbol = symbol;
    internal PowerShellSymbolId Symbol { get; }
}

internal sealed class PowerShellBoundCommandStage
{
    internal PowerShellBoundCommandStage(
        SourceSpan span,
        PowerShellCompilationCommandProviderContract provider,
        PowerShellBoundPipelineSymbol[] pipelineSymbols)
    {
        Span = span;
        Provider = provider;
        PipelineSymbols = pipelineSymbols ?? Array.Empty<PowerShellBoundPipelineSymbol>();
    }

    internal SourceSpan Span { get; }
    internal PowerShellCompilationCommandProviderContract Provider { get; }
    internal PowerShellImmutableArray<PowerShellBoundPipelineSymbol> PipelineSymbols { get; }
}

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
    internal PowerShellBoundCommandRegionStatement(
        SourceSpan span,
        string source,
        PowerShellBoundCommandRegionArgument[] arguments,
        PowerShellBoundCommandStage[]? stages = null)
        : base(span, PowerShellSemanticEffect.Host | PowerShellSemanticEffect.SuccessOutput, PowerShellRequiredCapability.CommandRegion)
    {
        Source = source;
        Arguments = arguments ?? Array.Empty<PowerShellBoundCommandRegionArgument>();
        Stages = stages ?? Array.Empty<PowerShellBoundCommandStage>();
    }

    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandStage> Stages { get; }
}

internal sealed class PowerShellBoundCommandCaptureStatement : PowerShellBoundStatement
{
    internal PowerShellBoundCommandCaptureStatement(
        SourceSpan span,
        PowerShellSymbolId target,
        Type targetType,
        string source,
        PowerShellBoundCommandRegionArgument[] arguments,
        PowerShellBoundCommandStage[]? stages = null)
        : base(span, PowerShellSemanticEffect.Host | PowerShellSemanticEffect.Mutation, PowerShellRequiredCapability.CommandRegion)
    {
        Target = target;
        TargetType = targetType;
        Source = source;
        Arguments = arguments ?? Array.Empty<PowerShellBoundCommandRegionArgument>();
        Stages = stages ?? Array.Empty<PowerShellBoundCommandStage>();
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandStage> Stages { get; }
}
