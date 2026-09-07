namespace PowerForge;

/// <summary>Conversion selected for one CLR argument after all authored arguments have been evaluated.</summary>
internal enum PowerShellClrArgumentConversionKind
{
    None,
    Int32OrDoubleToInt32
}

/// <summary>Preserves the selected parameter's conversion and native error identity through lowering.</summary>
internal readonly struct PowerShellClrArgumentConversion
{
    private readonly string? _parameterName;

    internal PowerShellClrArgumentConversion(PowerShellClrArgumentConversionKind kind, string parameterName)
    {
        Kind = kind;
        _parameterName = parameterName;
    }

    internal PowerShellClrArgumentConversionKind Kind { get; }
    internal string ParameterName => _parameterName ?? string.Empty;
}
