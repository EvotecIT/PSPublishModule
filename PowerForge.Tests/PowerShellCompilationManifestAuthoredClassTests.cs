namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ManifestAuthoredClass_PreservesParameterAndOutputIdentity(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ProbeItem {
                [CmdletBinding()]
                [OutputType([FixtureItem[]])]
                param([Parameter(Mandatory,ValueFromPipeline)][FixtureItem[]]$Item)
                process { $Item }
            }
            Export-ModuleMember -Function Get-ProbeItem
            """, ".psm1");
        var hook = Path.Combine(fixture.RootPath, "Classes", "Class.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        File.WriteAllText(hook, "class FixtureItem { [string]$Name }");
        var sourceManifest = Path.ChangeExtension(fixture.ScriptPath, ".psd1");
        File.WriteAllText(sourceManifest,
            "@{ RootModule='input.psm1'; ModuleVersion='1.0.0'; GUID='3b59b9db-aa92-4a03-a54e-4054b2cf8f85'; ScriptsToProcess=@('Classes/Class.ps1'); FunctionsToExport=@('Get-ProbeItem'); CmdletsToExport=@() }");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ManifestAuthoredClass",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(1, built.Manifest!.CompiledMethods);
        const string probe = """
            foreach($round in 1,2) {
                $command=Get-Command Get-ProbeItem
                $arrayType=$command.Parameters['Item'].ParameterType
                $itemType=$arrayType.GetElementType()
                $item=[Activator]::CreateInstance($itemType)
                $item.Name='round'+$round
                $direct=@(Get-ProbeItem -Item @($item))
                $pipeline=@($item|Get-ProbeItem)
                [pscustomobject]@{
                    round=$round
                    parameterType=$arrayType.FullName
                    outputType=$command.OutputType[0].Type.FullName
                    sameType=($arrayType -eq $command.OutputType[0].Type)
                    direct=@($direct|ForEach-Object{$_.Name})
                    pipeline=@($pipeline|ForEach-Object{$_.Name})
                }|ConvertTo-Json -Depth 5 -Compress
                Import-Module -Name $modulePath -Force
            }
            """;
        var original = RunModuleProof(sourceManifest,
            "$modulePath='" + sourceManifest.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe, host);
        var generated = RunModuleProof(built.ArtifactPath!,
            "$modulePath='" + built.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("\"sameType\":true", generated);
        var rounds = generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rounds.Length);
        Assert.Contains("\"direct\":[\"round1\"]", rounds[0]);
        Assert.Contains("\"pipeline\":[\"round1\"]", rounds[0]);
        Assert.Contains("\"direct\":[\"round2\"]", rounds[1]);
        Assert.Contains("\"pipeline\":[\"round2\"]", rounds[1]);
    }
}
