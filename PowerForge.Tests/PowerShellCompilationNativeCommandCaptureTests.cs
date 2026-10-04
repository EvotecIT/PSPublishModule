using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandRegions_PreserveCapturedValuesPartialOutputAndCollectors(string framework, string host)
    {
        const string pipeline = "$Values | ForEach-Object { $_; if($Fail) { throw 'capture failure' } }";
        var bodies = new[]
        {
            "$copy='prior'; $copy=" + pipeline + "; 'after'; return ,$copy",
            "$copy='prior'; $copy=(" + pipeline + "); 'after'; return ,$copy",
            "(" + pipeline + "); 'after'",
            "return (" + pipeline + ")",
            "$copy=@((" + pipeline + ")); 'after'; return ,$copy",
            "$copy=@('prefix'; (" + pipeline + "); 'suffix'); 'after'; return ,$copy",
            "$copy=[object](" + pipeline + "); 'after'; return ,$copy",
            "$copy=(" + pipeline + ").Count; 'after'; return ,$copy",
            "$copy=(" + pipeline + ")[0]; 'after'; return ,$copy",
            "$copy=@(" + pipeline + "); 'after'; return ,$copy"
        };
        var source = string.Join(Environment.NewLine, bodies.Select((body, index) =>
            "function Read-NativePipelineCapture" + index + " { [CmdletBinding()] param([int]$Seed=1,[object]$Values,[switch]$Fail); " + body + " }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativePipelineCaptures", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == bodies.Length, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(item => item.DiagnosticChain.Select(cause => item.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
            Assert.Equal(1, unit.RuntimeCommandRegions);
        });
        const string probe = """
            $values=@(@{value=$null},@{value=@()},@{value=@(3)},@{value=@(1,2)},@{value=@(,@(1,2))})
            for($shape=0;$shape -lt 10;$shape++) {
                foreach($value in $values) {
                    foreach($fail in $false,$true) {
                        foreach($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                            $Error.Clear(); $records=@(); $emitted=@(); $caught=$null; $name='Read-NativePipelineCapture'+$shape
                            try { $records=@(& $name -Values $value.value -Fail:$fail -ErrorAction $preference -OutVariable emitted 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                            [pscustomobject]@{shape=$shape;value=$value.value;fail=$fail;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                                errors=@($Error | ForEach-Object { [pscustomobject]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine} })} | ConvertTo-Json -Compress -Depth 12
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-capture-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-capture-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(400, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(10).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
