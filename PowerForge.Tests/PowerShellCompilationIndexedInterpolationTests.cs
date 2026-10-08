using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void IndexedInterpolation_PreservesNativeKeysAndOfflineFileMetadata(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Get-FileMetaData.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + Environment.NewLine + """
            function Write-IndexedInterpolation {
                [CmdletBinding()]param([object]$Map,[object]$Key,[object]$Value,[string]$Mode)
                $marker='local'
                try {
                    if($Mode -eq 'compound') { $Map["$($Key.Names[0][1])"] += $Value.GetValue() }
                    elseif($Mode -eq 'increment') { $result=$Map["$($Key.Names[0][1])"]++; $result }
                    else { $Map["$($Key.Names[0][1])"]=$Value.GetValue() }
                } catch { 'error:'+ $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.CategoryInfo.Category+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.IndexedInterpolation", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Get-FileMetaData", "Write-IndexedInterpolation" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding);
        var probe = File.ReadAllText(FindCompleteConversionWorkflow("..", "m29a-indexed-interpolation-probe.ps1"));
        var command = "$owned='" + EscapeStatementErrorPath(fixture.RootPath) + "'; " + probe;
        var original = RunModuleProof(fixture.ScriptPath, command, host);
        var generated = RunModuleProof(built.ArtifactPath!, command, host);
        Assert.Equal(original, generated);
        var records = original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(29, records.Length);
        Assert.Contains("\"Flag\",\"type\":\"System.Boolean\",\"value\":true", original, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"rhs-replacement\"", original, StringComparison.Ordinal);
    }
}
