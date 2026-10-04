using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedLoggingDefaults_PreserveCallerMapAndModuleState(string framework, string host)
    {
        var sources = new[]
        {
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Logging", "Set-LoggingCapabilities.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Objects", "Remove-EmptyValue.ps1")
        };
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedLoggingDefaults",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Set-LoggingCapabilities" && unit.EmittedClrMethod);
        // No LogPath is supplied: this exercises real caller/default-map mutation
        // and the unchanged cleanup helper without entering filesystem rotation.
        const string probe = """
            $module=(Get-Command Set-LoggingCapabilities).Module
            & $module {
                function Get-MapSnapshot($map) {
                    @($map.Keys | Sort-Object | ForEach-Object {
                        $value=$map[$_]
                        [pscustomobject]@{key=$_;value=$value;type=if($null -eq $value){'null'}else{$value.GetType().FullName}}
                    })
                }
                foreach($mode in 'omitted','enabled','disabled') {
                    foreach($format in '', 'HH:mm:ss') {
                        foreach($kind in 'module','hashtable','ordered','empty') {
                            $script:PSDefaultParameterValues=@{'Write-Color:Sentinel'='module';'Write-Color:Empty'=$null;'Write-Color:FalseValue'=$false;'Write-Color:Zero'=0}
                            $priorModule=$script:PSDefaultParameterValues
                            $caller=if($kind -eq 'ordered'){[ordered]@{'Write-Color:Sentinel'='caller'}}else{@{'Write-Color:Sentinel'='caller'}}
                            if($kind -eq 'empty'){$caller=@{}}
                            $alias=$caller
                            $parameters=@{TimeFormat=$format}
                            if($kind -ne 'module'){$parameters.ParameterPSDefaultParameterValues=$caller}
                            if($mode -eq 'enabled'){$parameters.ShowTime=$true}
                            if($mode -eq 'disabled'){$parameters.ShowTime=$false}
                            $first=@(Set-LoggingCapabilities @parameters)
                            $second=@(Set-LoggingCapabilities @parameters)
                            [pscustomobject]@{
                                mode=$mode;format=$format;kind=$kind
                                firstCount=$first.Count;secondCount=$second.Count
                                callerIdentity=[object]::ReferenceEquals($alias,$caller)
                                moduleIdentity=[object]::ReferenceEquals($priorModule,$script:PSDefaultParameterValues)
                                caller=@(Get-MapSnapshot $caller)
                                defaults=@(Get-MapSnapshot $script:PSDefaultParameterValues)
                            }|ConvertTo-Json -Compress -Depth 8
                        }
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(24, generated.Split(Environment.NewLine).Length);
        Assert.Contains("System.Boolean", generated);
        Assert.Contains("\"callerIdentity\":true", generated);
    }
}
