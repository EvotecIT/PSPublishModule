namespace PowerForge;

internal sealed class PowerShellBoundNoteProperty
{
    internal PowerShellBoundNoteProperty(string name, PowerShellBoundExpression value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }
    internal PowerShellBoundExpression Value { get; }
}

internal sealed class PowerShellBoundPowerShellObjectExpression : PowerShellBoundExpression
{
    internal PowerShellBoundPowerShellObjectExpression(SourceSpan span, PowerShellBoundNoteProperty[] properties)
        : base(
            span,
            new PowerShellTypeFact(typeof(System.Management.Automation.PSObject), PowerShellTypeFactProvenance.Inferred, "A [pscustomobject] literal binds to one PSObject with literal note properties."),
            PowerShellValueState.Known,
            properties.Aggregate(PowerShellSemanticEffect.None, static (effects, property) => effects | property.Value.Effects),
            properties.Aggregate(PowerShellRequiredCapability.PowerShellHostTypes, static (capabilities, property) => capabilities | property.Value.Capabilities))
    {
        Properties = properties;
    }

    internal PowerShellBoundNoteProperty[] Properties { get; }
}
