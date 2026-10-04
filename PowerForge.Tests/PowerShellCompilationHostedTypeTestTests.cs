namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostedTypeTests_PreserveWrappedAndExtendedValues(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-Integer { [CmdletBinding()] param([object]$Value) return $Value -is [int] }
            function Test-NotInteger { [CmdletBinding()] param([object]$Value) return $Value -isnot [int] }
            function Test-Wrapped { [CmdletBinding()] param([object]$Value) return $Value -is [psobject] }
            """, ".psm1");
        const string probe = """
            $extended=7 | Add-Member -NotePropertyName Note -NotePropertyValue 'retained' -PassThru
            $values=@($null,3,[psobject]4,$extended,[pscustomobject]@{Name='Ada'},'text',@(8,9))
            foreach($name in 'Test-Integer','Test-NotInteger','Test-Wrapped') {
                for($index=0;$index -lt $values.Count;$index++) {
                    [pscustomobject]@{name=$name;index=$index;result=(& $name -Value $values[$index])} | ConvertTo-Json -Compress
                }
            }
            $extended.Note
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "hosted-types-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(22, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HostedTypes", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "hosted-types-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }
}
