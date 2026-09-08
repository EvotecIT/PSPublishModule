using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeMemberHosts))]
    public void NativeIndexing_PreservesSlicesConstraintsAndErrorContinuation(string framework, string host, int strictVersion)
    {
        using var fixture = ArtifactFixture.Create((strictVersion == 0 ? "Set-StrictMode -Off" : "Set-StrictMode -Version 2") + Environment.NewLine + """
            function Read-NativeIndex { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Index); 'before'; $Value[$Index]; 'after' }
            function Read-NativeSlice { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Index); return ,$Value[$Index,0] }
            function Read-NativeCommaIndex { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Index); return ,$Value[,$Index] }
            function Read-NativeStringIndex { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Index); return $Value[[string]$Index] }
            function Read-NativeObjectIndex { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Index); return $Value[([object]$Index)] }
            function Read-NativeIndexOrder { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Index); $i=0; $Value[$i++,$i++]; return "index=$i" }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeIndexing", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 6, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            Add-Type -TypeDefinition 'public sealed class NativeIndexProbe { public string this[int value] { get { return "int:"+value; } } public string this[object value] { get { return "object:"+value; } } public string this[string value] { get { if(value=="throw") throw new System.InvalidOperationException("index failed"); return "string:"+value; } } }'
            $values=@(@{value=$null},@{value='text'},@{value=7},@{value=@()},@{value=@(10,20,30)},
                @{value=@{one='key';0='zero'}},@{value=[ordered]@{one='first';two='second'}},@{value=(New-Object NativeIndexProbe)})
            $indices=@(@{value=$null},@{value=0},@{value=-1},@{value=99},@{value='one'},@{value='throw'},@{value=@(0,1)})
            foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                foreach ($name in 'Read-NativeIndex','Read-NativeSlice','Read-NativeCommaIndex','Read-NativeStringIndex','Read-NativeObjectIndex','Read-NativeIndexOrder') {
                    for ($valueIndex=0;$valueIndex -lt $values.Count;$valueIndex++) {
                        for ($index=0;$index -lt $indices.Count;$index++) {
                            $Error.Clear(); $caught=$null; $records=@(); $emitted=@()
                            try { $records=@(& $name -Value $values[$valueIndex].value -Index $indices[$index].value -ErrorAction $preference -OutVariable emitted 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                            [pscustomobject]@{name=$name;value=$valueIndex;index=$index;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                                errors=@($Error | ForEach-Object { [pscustomobject]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;message=$_.Exception.Message} })} | ConvertTo-Json -Compress -Depth 12
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-indexing");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-indexing");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(1344, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
