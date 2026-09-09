namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeThrow_PreservesArbitraryValuesCallbacksAndCleanup(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-NativeThrow {
                [CmdletBinding()] param([ValidateRange(1,2)][int]$Mode=1,[object]$Value,[object]$Trace)
                $marker='body'; 'before'; try { throw $Value } finally { $Trace.Add('finally') }; 'after'
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeThrow", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            function Describe-ThrownValue($value) {
                $record=if($value -is [Management.Automation.ErrorRecord]) {$value} else {$value.ErrorRecord}
                if($null -eq $record) {return $value.GetType().FullName}
                $chain=@();$exception=$record.Exception
                while($null -ne $exception) {$chain+=($exception.GetType().FullName+':'+$exception.Message);$exception=$exception.InnerException}
                [pscustomobject]@{id=$record.FullyQualifiedErrorId;chain=$chain;line=$record.InvocationInfo.ScriptLineNumber;column=$record.InvocationInfo.OffsetInLine}
            }
            foreach($shape in 'null','integer','boolean','string','exception','record','callback','failure') {
                foreach($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    $trace=[Collections.Generic.List[string]]::new();$errors=@();$caught=$null;$records=[Collections.Generic.List[string]]::new();$Error.Clear()
                    $value=switch($shape) {
                        'null' {$null}; 'integer' {42}; 'boolean' {$true}; 'string' {'failure'}; 'exception' {[InvalidOperationException]::new('failure')}
                        'record' {[Management.Automation.ErrorRecord]::new([InvalidOperationException]::new('failure'),'custom-id',[Management.Automation.ErrorCategory]::InvalidData,'target')}
                        default {$item=[pscustomobject]@{Trace=$trace;Fail=($shape -eq 'failure')}; $item | Add-Member ScriptMethod ToString {$this.Trace.Add('convert:'+ $marker); if($this.Fail){throw 'callback-failed'}; 'converted'} -Force; $item}
                    }
                    try {Invoke-NativeThrow -Value $value -Trace $trace -ErrorAction $action -ErrorVariable errors 2>$null | ForEach-Object {$records.Add($_)}}
                    catch {$caught=Describe-ThrownValue $_}
                    [pscustomobject]@{shape=$shape;action=$action;records=@($records);trace=@($trace);caught=$caught;
                        errors=@($errors | ForEach-Object {Describe-ThrownValue $_});globalErrors=@($Error | ForEach-Object {Describe-ThrownValue $_})} | ConvertTo-Json -Compress -Depth 12
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-throw");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-throw");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(32, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
    }
}
