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
    internal PowerShellLoweredCommandRegionArgument(PowerShellSymbolId symbol, bool isSwitch)
    {
        Symbol = symbol;
        IsSwitch = isSwitch;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal bool IsSwitch { get; }
}

internal sealed class PowerShellLoweredCommandRegionStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredCommandRegionStatement(
        SourceSpan span,
        string source,
        PowerShellLoweredCommandRegionArgument[] arguments,
        PowerShellLoweredCommandStage[]? stages = null, string? nativeSourcePath = null, string? nativeSourceDocument = null, PowerShellCommandRegionSourceSelection? sourceSelection = null)
        : base(span)
    {
        HostedFallbackSource = source;
        Arguments = arguments;
        Stages = stages ?? Array.Empty<PowerShellLoweredCommandStage>();
        NativeSourcePath = nativeSourcePath;
        NativeSourceDocument = nativeSourceDocument;
        SourceSelection = sourceSelection;
    }

    internal string HostedFallbackSource { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
    internal string? NativeSourcePath { get; }
    internal string? NativeSourceDocument { get; }
    internal PowerShellCommandRegionSourceSelection? SourceSelection { get; }
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
        PowerShellLoweredCommandStage[]? stages = null, PowerShellCommandRegionSourceSelection? sourceSelection = null)
        : base(span)
    {
        Target = target;
        TargetType = targetType;
        Declare = declare;
        HostedFallbackSource = source;
        SourceSelection = sourceSelection;
        Arguments = arguments;
        Stages = stages ?? Array.Empty<PowerShellLoweredCommandStage>();
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal bool Declare { get; }
    internal string HostedFallbackSource { get; }
    internal PowerShellCommandRegionSourceSelection? SourceSelection { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandStage> Stages { get; }
}
