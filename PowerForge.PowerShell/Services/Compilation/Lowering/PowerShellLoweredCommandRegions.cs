namespace PowerForge;

internal sealed class PowerShellLoweredCommandRegionArgument
{
    internal PowerShellLoweredCommandRegionArgument(PowerShellSymbolId symbol)
        => Symbol = symbol;

    internal PowerShellSymbolId Symbol { get; }
}

internal sealed class PowerShellLoweredCommandRegionStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredCommandRegionStatement(SourceSpan span, string source, PowerShellLoweredCommandRegionArgument[] arguments)
        : base(span)
    {
        Source = source;
        Arguments = arguments;
    }

    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
}

internal sealed class PowerShellLoweredCommandCaptureStatement : PowerShellLoweredStatement
{
    internal PowerShellLoweredCommandCaptureStatement(
        SourceSpan span,
        PowerShellSymbolId target,
        Type targetType,
        bool declare,
        string source,
        PowerShellLoweredCommandRegionArgument[] arguments)
        : base(span)
    {
        Target = target;
        TargetType = targetType;
        Declare = declare;
        Source = source;
        Arguments = arguments;
    }

    internal PowerShellSymbolId Target { get; }
    internal Type TargetType { get; }
    internal bool Declare { get; }
    internal string Source { get; }
    internal PowerShellImmutableArray<PowerShellLoweredCommandRegionArgument> Arguments { get; }
}
