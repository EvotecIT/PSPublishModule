namespace PowerForge;

internal sealed class PowerShellLoweredNoteProperty
{
    internal PowerShellLoweredNoteProperty(string name, PowerShellLoweredExpression value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }
    internal PowerShellLoweredExpression Value { get; }
}

internal sealed class PowerShellLoweredPowerShellObjectExpression : PowerShellLoweredExpression
{
    internal PowerShellLoweredPowerShellObjectExpression(SourceSpan span, PowerShellLoweredNoteProperty[] properties, string temporary)
        : base(span, typeof(System.Management.Automation.PSObject))
    {
        Properties = properties;
        Temporary = temporary;
    }

    internal PowerShellLoweredNoteProperty[] Properties { get; }
    internal string Temporary { get; }
}
