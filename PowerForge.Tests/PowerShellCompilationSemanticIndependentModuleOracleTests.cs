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
    public void ConventionalFileNameExportsPreserveTheAuthoredSurfaceOnBothHosts()
    {
        const string source = """
            $public = @(Get-ChildItem -Path "$PSScriptRoot/Read-Public*.ps1" -Exclude @('*.Tests.ps1', 'Read-PublicInternal*') -ErrorAction SilentlyContinue)
            $private = @()
            foreach ($file in @($public + $private)) {
                . $file.FullName
            }
            function Read-PrivateFlavor { return 'private' }
            Export-ModuleMember -Function $public.BaseName
            """;
        const string probe = """
            [ordered]@{
                functions = @((Get-Module ScopedValues).ExportedFunctions.Keys | Sort-Object)
                value = Read-PublicFlavor
                private = $null -ne (Get-Command Read-PrivateFlavor -ErrorAction Ignore)
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-PublicFlavor" }, value => Assert.Equal(
            "{\"functions\":[\"Read-PublicFlavor\"],\"value\":\"vanilla\",\"private\":false}", value),
            assertArtifact: result => Assert.DoesNotContain(result.Manifest!.UnitDispositionLedger!.Entries,
                entry => entry.Name == "Read-PublicInternalFlavor"),
            additionalSources: new Dictionary<string, string>
            {
                ["Read-PublicFlavor.ps1"] = "function Read-PublicFlavor { [CmdletBinding()] param() begin {} end { 'vanilla' } }",
                ["Read-PublicFlavor.Tests.ps1"] = "throw 'Excluded file executed'",
                ["Read-PublicInternalFlavor.ps1"] = "function Read-PublicInternalFlavor { return 'internal' }"
            },
            resolveModuleSources: true);
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
