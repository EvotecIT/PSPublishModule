namespace PowerForge;

/// <summary>Corrective guidance for the stable compiler diagnostic codes.</summary>
public static class PowerShellCompilationHostDiagnosticCatalog
{
    /// <summary>Gets the suggested action for a compiler finding.</summary>
    public static string GetFix(PowerShellCompilationDiagnosticCode code)
        => code switch
        {
            PowerShellCompilationDiagnosticCode.InputError => "Check input paths and the selected artifact settings.",
            PowerShellCompilationDiagnosticCode.ParseError => "Correct the PowerShell syntax at the reported location.",
            PowerShellCompilationDiagnosticCode.UnsupportedParameterType => "Use a supported type or choose a hosted mode.",
            PowerShellCompilationDiagnosticCode.CommandInvocation => "Use a hosted mode if the command requires PowerShell.",
            PowerShellCompilationDiagnosticCode.DynamicCommandInvocation => "Use a static command reference or choose a hosted mode.",
            PowerShellCompilationDiagnosticCode.ScriptBlock => "Use a hosted mode for runtime script blocks.",
            PowerShellCompilationDiagnosticCode.RuntimeScope => "Use local typed values or choose a hosted mode for runtime-owned state.",
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax => "Use a supported form or choose a hosted mode.",
            PowerShellCompilationDiagnosticCode.UnsupportedOperator => "Use an operator supported by the selected target or choose a hosted mode.",
            PowerShellCompilationDiagnosticCode.ArtifactShaping => "Review the artifact-specific restriction and choose a compatible mode or target.",
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
}
