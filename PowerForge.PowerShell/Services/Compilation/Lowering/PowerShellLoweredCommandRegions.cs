namespace PowerForge;

internal abstract class PowerShellLoweredCommandStage
{
    internal PowerShellLoweredCommandStage(
        SourceSpan span,
        PowerShellCompilationCommandProviderContract provider,
        PowerShellSymbolId[] pipelineSymbols)
    {
        Span = span;
        Provider = provider;
        PipelineSymbols = pipelineSymbols ?? Array.Empty<PowerShellSymbolId>();
    }

    internal SourceSpan Span { get; }
    internal PowerShellCompilationCommandProviderContract Provider { get; }
    internal PowerShellImmutableArray<PowerShellSymbolId> PipelineSymbols { get; }
}

internal sealed class PowerShellLoweredProjectionCommandStage : PowerShellLoweredCommandStage
{
    internal PowerShellLoweredProjectionCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellSymbolId[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellLoweredFilteringCommandStage : PowerShellLoweredCommandStage
{
    internal PowerShellLoweredFilteringCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellSymbolId[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellLoweredMappingCommandStage : PowerShellLoweredCommandStage
{
    internal PowerShellLoweredMappingCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellSymbolId[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellLoweredSortingCommandStage : PowerShellLoweredCommandStage
{
    internal PowerShellLoweredSortingCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellSymbolId[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellLoweredHostedCommandStage : PowerShellLoweredCommandStage
{
    internal PowerShellLoweredHostedCommandStage(SourceSpan span, PowerShellCompilationCommandProviderContract provider, PowerShellSymbolId[] symbols) : base(span, provider, symbols) { }
}

internal sealed class PowerShellLoweredCommandRegionArgument
{
    internal PowerShellLoweredCommandRegionArgument(PowerShellSymbolId symbol)
        => Symbol = symbol;

    internal PowerShellSymbolId Symbol { get; }
}

internal sealed class PowerShellLoweredCommandRegionStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredCommandRegionStatement(
        SourceSpan span,
        string source,
        PowerShellLoweredCommandRegionArgument[] arguments,
        PowerShellLoweredCommandStage[]? stages = null)
        : base(span)
    {
        HostedFallbackSource = source;
        Arguments = arguments;
        Stages = stages ?? Array.Empty<PowerShellLoweredCommandStage>();
    }

    internal string HostedFallbackSource { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
}

internal sealed class PowerShellLoweredCommandCaptureStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredCommandCaptureStatement(
        SourceSpan span,
        PowerShellSymbolId target,
        Type targetType,
        bool declare,
        string source,
        PowerShellLoweredCommandRegionArgument[] arguments,
        PowerShellLoweredCommandStage[]? stages = null)
        : base(span)
    {
        Target = target;
        TargetType = targetType;
        Declare = declare;
        HostedFallbackSource = source;
        Arguments = arguments;
        Stages = stages ?? Array.Empty<PowerShellLoweredCommandStage>();
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal bool Declare { get; }
    internal string HostedFallbackSource { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
}
