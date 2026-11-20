using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void LiteralModuleExportSplatPreservesFunctionsAliasesAndVariablesOnBothHosts()
    {
        const string source = """
            $Flavor = 'vanilla'
            function Read-Flavor { return 'vanilla' }
            function Read-PrivateFlavor { 'private' }
            Set-Alias -Name Get-Flavor -Value Read-Flavor
            $exports = @{
                Function = 'Read-Flavor'
                Alias = 'Get-Flavor'
                Variable = 'Flavor'
            }
            Export-ModuleMember @exports
            """;
        const string probe = """
            $module = Get-Module ScopedValues
            [ordered]@{
                functions = @($module.ExportedFunctions.Keys)
                aliases = @($module.ExportedAliases.Keys)
                variables = @($module.ExportedVariables.Keys)
                variableValue = $module.ExportedVariables['Flavor'].Value
                value = Get-Flavor
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-Flavor" }, value => Assert.Equal(
            "{\"functions\":[\"Read-Flavor\"],\"aliases\":[\"Get-Flavor\"],\"variables\":[\"Flavor\"],\"variableValue\":\"vanilla\",\"value\":\"vanilla\"}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ActionPreferenceComparisonPreservesInformationBindingOnBothHosts()
    {
        const string source = """
            function Read-InformationPreference {
                [CmdletBinding()] param()
                if ($InformationPreference -eq [System.Management.Automation.ActionPreference]::Ignore) {
                    return 'ignored'
                }

                Write-Information -MessageData 'authored information' -Tags 'preference'
                return $InformationPreference.ToString()
            }
            Export-ModuleMember -Function Read-InformationPreference
            """;
        const string probe = """
            [ordered]@{
                default = Read-InformationPreference
                ignored = Read-InformationPreference -InformationAction Ignore
                continued = Read-InformationPreference -InformationAction Continue
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-InformationPreference" }, value =>
        {
            Assert.Contains("\"default\":", value, StringComparison.Ordinal);
            Assert.Contains("\"ignored\":\"ignored\"", value, StringComparison.Ordinal);
            Assert.Contains("\"continued\":\"Continue\"", value, StringComparison.Ordinal);
        });
    }
}
