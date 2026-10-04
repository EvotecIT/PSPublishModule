namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostedArgumentAttributes_PreserveModuleScopeEffectsAndRepeatedImports(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            class ScopedPrefix : System.Management.Automation.ArgumentTransformationAttribute {
                [object] Transform([System.Management.Automation.EngineIntrinsics]$Context, [object]$InputData) {
                    $marker = $Context.SessionState.PSVariable.GetValue('script:Marker')
                    $count = [int]$Context.SessionState.PSVariable.GetValue('script:BindCount')
                    $Context.SessionState.PSVariable.Set('script:BindCount', $count + 1)
                    return "$marker/$InputData"
                }
            }
            class ScopedValidation : System.Management.Automation.ValidateArgumentsAttribute {
                [void] Validate([object]$InputData, [System.Management.Automation.EngineIntrinsics]$Context) {
                    if ($InputData -like '*/bad') { throw 'Rejected scoped value' }
                }
            }
            $script:Marker = 'initial'
            $script:BindCount = 0
            function Read-ScopedValue {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][ScopedPrefix()][ScopedValidation()][string]$Value = 'default')
                process { $Value }
            }
            function Set-ScopedMarker { [CmdletBinding()] param([string]$Value); $script:Marker = $Value }
            function Get-ScopedCount { [CmdletBinding()] param(); $script:BindCount }
            Export-ModuleMember -Function Read-ScopedValue,Set-ScopedMarker,Get-ScopedCount
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HostedAttributeScope", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, static unit => unit.Name == "Read-ScopedValue" && unit.EmittedClrMethod);
        const string probe = """
            foreach ($round in 1,2,3) {
                Import-Module $modulePath -Force -ErrorAction Stop
                Set-ScopedMarker "round$round"
                $failure = try { Read-ScopedValue -Value 'bad' -ErrorAction Stop } catch {
                    $_.FullyQualifiedErrorId + '|' + $_.CategoryInfo.Category + '|' + $_.Exception.GetType().FullName
                }
                $records = @(Read-ScopedValue; Read-ScopedValue -Value 'explicit'; 'one','two' | Read-ScopedValue)
                [pscustomobject]@{round=$round;records=$records;count=(Get-ScopedCount);failure=$failure;
                    attributeTypes=@((Get-Command Read-ScopedValue).Parameters['Value'].Attributes | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Compress
                Remove-Module (Get-Module | Where-Object {$_.Path -eq $modulePath})
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, host);
        var generated = RunModuleProof(built.ArtifactPath!,
            "$modulePath='" + EscapeStatementErrorPath(built.ArtifactPath!) + "'; " + probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("/explicit", generated);
        Assert.Contains("ScopedPrefix", generated);
        Assert.Contains("ParameterArgumentValidationError", generated);
    }
}
