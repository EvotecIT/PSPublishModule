using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeJoin_PreservesValuesConversionCallbacksAndOperandOrder(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-JoinOperand { param([object]$Value,[object]$Trace,[string]$Name); $Trace.Add($Name); return ,$Value }
            function Read-JoinValue { [CmdletBinding()] param([object]$Value,[object]$Separator,[object]$Trace); $result='prior'; $result=(Read-JoinOperand -Value $Value -Trace $Trace -Name 'left') -join (Read-JoinOperand -Value $Separator -Trace $Trace -Name 'right'); $result; 'after' }
            function Read-UnaryJoinValue { [CmdletBinding()] param([object]$Value,[object]$Separator,[object]$Trace); $result='prior'; $result=-join (Read-JoinOperand -Value $Value -Trace $Trace -Name 'left'); $result; 'after' }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeJoinValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            function New-JoinText($trace,$name,$fail) {
                $value=[pscustomobject]@{Trace=$trace;Name=$name;Fail=$fail}
                $value | Add-Member ScriptMethod ToString { $this.Trace.Add('convert-'+$this.Name); if($this.Fail) { throw ('failure-'+$this.Name) }; $this.Name } -Force
                return $value
            }
            function Describe-JoinError($errorRecord) {
                $record=if($errorRecord -is [Management.Automation.ErrorRecord]) {$errorRecord} else {$errorRecord.ErrorRecord}
                $chain=@(); $exception=$record.Exception
                while($null -ne $exception) { $chain+=($exception.GetType().FullName+':'+$exception.Message); $exception=$exception.InnerException }
                [pscustomobject]@{id=$record.FullyQualifiedErrorId;chain=$chain;line=$record.InvocationInfo.ScriptLineNumber;column=$record.InvocationInfo.OffsetInLine}
            }
            foreach($name in 'Read-JoinValue','Read-UnaryJoinValue') {
                foreach($shape in 'null','empty','one','many','nested','characters','scalar','callback','callback-failure') {
                    foreach($separatorShape in 'null','text','number','callback','callback-failure') {
                        foreach($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                            $trace=[Collections.Generic.List[string]]::new()
                            $value=switch($shape) {
                                'null' {$null}; 'empty' {,@()}; 'one' {,@('a')}; 'many' {,@('a',$null,4)}
                                'nested' {,@('a',@(1,2),'b')}; 'characters' {,[char[]]'abc'}; 'scalar' {42}
                                'callback' {,@((New-JoinText $trace 'item1' $false),(New-JoinText $trace 'item2' $false))}
                                'callback-failure' {,@((New-JoinText $trace 'item1' $true),(New-JoinText $trace 'item2' $false))}
                            }
                            $separator=switch($separatorShape) {
                                'null' {$null}; 'text' {'|'}; 'number' {7}
                                'callback' {New-JoinText $trace 'separator' $false}; 'callback-failure' {New-JoinText $trace 'separator' $true}
                            }
                            $Error.Clear(); $records=@(); $faults=@(); $caught=$null
                            try { $records=@(& $name -Value $value -Separator $separator -Trace $trace -ErrorAction $action -ErrorVariable faults 2>$null) }
                            catch { $caught=Describe-JoinError $_ }
                            [pscustomobject]@{name=$name;shape=$shape;separator=$separatorShape;action=$action;records=$records;
                                types=@($records | ForEach-Object {$_.GetType().FullName});trace=@($trace);caught=$caught;
                                faults=@($faults | ForEach-Object {Describe-JoinError $_});errors=@($Error | ForEach-Object {Describe-JoinError $_})} | ConvertTo-Json -Compress -Depth 12
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-join");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-join");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(360, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeJoin_PreservesEnumerationFailuresAndContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-BinaryJoin { [CmdletBinding()] param([object]$Value); 'before'; $result='prior'; $result=$Value -join ','; $result; 'after' }
            function Read-UnaryJoin { [CmdletBinding()] param([object]$Value); 'before'; $result='prior'; $result=-join $Value; $result; 'after' }
            function Read-CaughtJoin { [CmdletBinding()] param([object]$Value); try { $Value -join '|'; 'try-tail' } catch { 'caught' } finally { 'finally' }; 'after' }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeJoinFaults", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        VerifyEnumerationFaults(fixture, result.ArtifactPath!, host, new[] { "Read-BinaryJoin", "Read-UnaryJoin", "Read-CaughtJoin" });
    }
}
