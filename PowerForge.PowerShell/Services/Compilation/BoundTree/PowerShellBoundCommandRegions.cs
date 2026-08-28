namespace PowerForge;

internal sealed class PowerShellBoundPipelineSymbol
{
    internal PowerShellBoundPipelineSymbol(PowerShellSymbolId symbol) => Symbol = symbol;
    internal PowerShellSymbolId Symbol { get; }
}

internal abstract class PowerShellBoundCommandStage
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

internal sealed class PowerShellBoundProjectionCommandStage : PowerShellBoundCommandStage
{
    internal PowerShellBoundProjectionCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellBoundPipelineSymbol[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellBoundFilteringCommandStage : PowerShellBoundCommandStage
{
    internal PowerShellBoundFilteringCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellBoundPipelineSymbol[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellBoundMappingCommandStage : PowerShellBoundCommandStage
{
    internal PowerShellBoundMappingCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellBoundPipelineSymbol[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellBoundSortingCommandStage : PowerShellBoundCommandStage
{
    internal PowerShellBoundSortingCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellBoundPipelineSymbol[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellBoundHostedCommandStage : PowerShellBoundCommandStage
{
    internal PowerShellBoundHostedCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellBoundPipelineSymbol[] symbols) : base(span, provider, symbols) { }
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
        PowerShellBoundCommandStage[]? stages = null,
        int statementCount = 1)
        : base(span, PowerShellSemanticEffect.Host | PowerShellSemanticEffect.SuccessOutput, PowerShellRequiredCapability.CommandRegion)
    {
        HostedFallbackSource = source;
        Arguments = arguments ?? Array.Empty<PowerShellBoundCommandRegionArgument>();
        Stages = stages ?? Array.Empty<PowerShellBoundCommandStage>();
        StatementCount = Math.Max(1, statementCount);
    }

    internal string HostedFallbackSource { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandStage> Stages { get; }
    internal int StatementCount { get; }
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
        HostedFallbackSource = source;
        Arguments = arguments ?? Array.Empty<PowerShellBoundCommandRegionArgument>();
        Stages = stages ?? Array.Empty<PowerShellBoundCommandStage>();
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal string HostedFallbackSource { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellBoundCommandStage> Stages { get; }
}
