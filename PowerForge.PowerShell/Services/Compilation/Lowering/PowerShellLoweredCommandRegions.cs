namespace PowerForge;

internal sealed class PowerShellLoweredCommandStage
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
        Source = source;
        Arguments = arguments;
        Stages = stages ?? Array.Empty<PowerShellLoweredCommandStage>();
    }

    internal string Source { get; }
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
        Source = source;
        Arguments = arguments;
        Stages = stages ?? Array.Empty<PowerShellLoweredCommandStage>();
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal bool Declare { get; }
    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
}
