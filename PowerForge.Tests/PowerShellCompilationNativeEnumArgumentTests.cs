using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeEnumArguments_TransformedTypeValuesRemainHosted(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function New-DirectEnumType {
                [CmdletBinding()] param([string] $Value)
                $null=$Value -match '.'
                try { [Activator]::CreateInstance(([MidpointRounding]::AwayFromZero).GetType()) }
                catch { $_.FullyQualifiedErrorId }
            }
            function New-HoistedEnumType {
                [CmdletBinding()] param([string] $Value)
                $null=$Value -match '.'
                $type=([MidpointRounding]::AwayFromZero).GetType()
                $alias=$type
                try { [Activator]::CreateInstance($alias) }
                catch { $_.FullyQualifiedErrorId }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TransformedEnumType",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.UnitDispositionLedger!.Entries.Count);
        Assert.All(result.Manifest.UnitDispositionLedger.Entries, unit =>
        {
            Assert.False(unit.EmittedClrMethod);
            Assert.True(unit.RetainedHostedSource);
            Assert.Contains(unit.DiagnosticChain, diagnostic =>
                diagnostic.Message.Contains("caught-error identity", StringComparison.Ordinal));
        });
        const string probe = "New-DirectEnumType -Value A; New-HoistedEnumType -Value B";
        Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host),
            RunModuleProof(result.ArtifactPath!, probe, host));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeEnumArguments_PreserveDirectHoistedAndCaughtMethodRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-DirectEnumRounding {
                [CmdletBinding()] param([object] $Value)
                $null=$Value -match '.'
                try { [math]::Round($Value, 1, [MidpointRounding]::AwayFromZero) }
                catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} }
            }
            function Get-HoistedEnumRounding {
                [CmdletBinding()] param([object] $Value)
                $null=$Value -match '.'
                $mode=[MidpointRounding]::AwayFromZero
                $alias=$mode
                try { [math]::Round($Value, 1, $alias) }
                catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} }
            }
            function Get-VariableEnumRounding {
                [CmdletBinding()] param([object] $Mode)
                $null=$Mode -match '.'
                try { [math]::Round(1.25, 1, $Mode) }
                catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.EnumMethodArguments",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3,
            System.Text.Json.JsonSerializer.Serialize(result.Manifest.UnitDispositionLedger));
        const string probe = """
            foreach($value in 1.25,-1.25,'1.25','invalid') {
                @(Get-DirectEnumRounding -Value $value)|ConvertTo-Json -Compress -Depth 5
                @(Get-HoistedEnumRounding -Value $value)|ConvertTo-Json -Compress -Depth 5
            }
            foreach($mode in 'AwayFromZero','ToEven','invalid',0) {
                @(Get-VariableEnumRounding -Mode $mode)|ConvertTo-Json -Compress -Depth 5
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("System.Management.Automation.MethodException", generated);
    }
}
