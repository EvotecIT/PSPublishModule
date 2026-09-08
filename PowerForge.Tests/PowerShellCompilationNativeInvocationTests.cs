using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeInvocations_PreserveOverloadsCallbacksAndEvaluationOrder(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-NativeMethod { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); 'before'; $Target.Pick($Value); 'after' }
            function Invoke-NativeObjectMethod { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); return $Target.Pick(([object]$Value)) }
            function Invoke-NativeStringMethod { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); return $Target.Pick([string]$Value) }
            function Invoke-NativeConstrainedTarget { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); return ([object]$Target).Pick($Value) }
            function Invoke-NativeMethodOrder { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); $i=0; $Target.Pair($i++,$i++); return "index=$i" }
            function Invoke-NativeConstructor { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); return [System.Text.StringBuilder]::new($Value).ToString() }
            function Invoke-NativeDynamicStatic { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); return $Target::Parse($Value) }
            function Invoke-NativeHexByte { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); return [Byte]::Parse($Value.Substring(0,2),[Globalization.NumberStyles]::HexNumber) }
            function Invoke-NativeBoundNames { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Target,[object]$Value); $PSBoundParameters.ContainsKey('Seed'); $PSBoundParameters.ContainsKey('Value') }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeInvocations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 9, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            Add-Type -TypeDefinition 'public sealed class NativeMethodProbe { public string Pick(int value) { return "int:"+value; } public string Pick(object value) { return "object:"+value; } public string Pick(string value) { if(value=="throw") throw new System.InvalidOperationException("method failed"); return "string:"+value; } public string Pair(int first,int second) { return first+":"+second; } }'
            $scripted=[pscustomobject]@{}
            $scripted | Add-Member -MemberType ScriptMethod -Name Pick -Value { param($value) if ($value -eq 'throw') { throw 'script method failed' }; "script:$value" }
            $targets=@(@{value=$null},@{value='text'},@{value=(New-Object NativeMethodProbe)},@{value=$scripted},@{value=[int]})
            $values=@(@{value=$null},@{value=2},@{value='AB'},@{value='throw'},@{value=@(0,1)})
            foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                foreach ($name in 'Invoke-NativeMethod','Invoke-NativeObjectMethod','Invoke-NativeStringMethod','Invoke-NativeConstrainedTarget','Invoke-NativeMethodOrder','Invoke-NativeConstructor','Invoke-NativeDynamicStatic','Invoke-NativeHexByte','Invoke-NativeBoundNames') {
                    for ($targetIndex=0;$targetIndex -lt $targets.Count;$targetIndex++) {
                        for ($index=0;$index -lt $values.Count;$index++) {
                            $Error.Clear(); $caught=$null; $records=@(); $emitted=@()
                            try { $records=@(& $name -Target $targets[$targetIndex].value -Value $values[$index].value -ErrorAction $preference -OutVariable emitted 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                            [pscustomobject]@{name=$name;target=$targetIndex;value=$index;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                                errors=@($Error | ForEach-Object { [pscustomobject]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber} })} | ConvertTo-Json -Compress -Depth 12
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-invocations");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-invocations");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(900, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
