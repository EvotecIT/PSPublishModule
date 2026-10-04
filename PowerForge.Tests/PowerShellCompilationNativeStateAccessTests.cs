using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeAccess_PreservesLiveAutomaticCollections(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeStateIndex {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value)
                $Error.Add($Value)
                $PSBoundParameters['Value']; $PSVersionTable['PSEdition']; $PSVersionTable['PSVersion'].Major
                $Error[0].FullyQualifiedErrorId
            }
            function Read-NativeStateMutation {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value)
                $Error.Clear(); $PSBoundParameters.Remove('Value'); $PSBoundParameters.ContainsKey('Value'); $Error.Count
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeStateAccess", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 2, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $Error.Clear()
            $record=[Management.Automation.ErrorRecord]::new([InvalidOperationException]::new('seed'), 'NativeStateSeed', [Management.Automation.ErrorCategory]::InvalidOperation, $null)
            @(Read-NativeStateIndex -Value $record | ForEach-Object { if ($_ -is [Management.Automation.ErrorRecord]) { $_.FullyQualifiedErrorId } else { $_ } }) | ConvertTo-Json -Compress
            @(Read-NativeStateMutation -Value 'actual') | ConvertTo-Json -Compress
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-state-access");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-state-access");
        Assert.True(original.ExitCode == 0 && compiled.ExitCode == 0, original.StandardError + compiled.StandardError);
        Assert.Contains("NativeStateSeed", original.StandardOutput);
        Assert.Contains("[true,false,0]", original.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
