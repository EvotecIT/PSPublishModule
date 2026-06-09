namespace PowerForge;

internal sealed class ModuleImportValidationTarget
{
    public string Label { get; }
    public string PowerShellEdition { get; }
    public bool PreferPwsh { get; }

    public ModuleImportValidationTarget(string label, string powerShellEdition, bool preferPwsh)
    {
        Label = label;
        PowerShellEdition = powerShellEdition;
        PreferPwsh = preferPwsh;
    }
}
