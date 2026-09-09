namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeFormat_PreservesObjectCollectionsCallbacksAndFailures(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeFormat {
                [CmdletBinding()] param([ValidateRange(1,2)][int]$Mode=1,[string]$Template,[object]$Value,[object]$Trace)
                $result='prior'; $result="$Template" -f $Value; $result; 'after'
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeFormat", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            function Describe-FormatError($value) {
                $record=if($value -is [Management.Automation.ErrorRecord]) {$value} else {$value.ErrorRecord}
                if($null -eq $record) {return $value.GetType().FullName}
                $chain=@();$exception=$record.Exception
                while($null -ne $exception) {$chain+=($exception.GetType().FullName+':'+$exception.Message);$exception=$exception.InnerException}
                [pscustomobject]@{id=$record.FullyQualifiedErrorId;chain=$chain;line=$record.InvocationInfo.ScriptLineNumber;column=$record.InvocationInfo.OffsetInLine}
            }
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach($shape in 'null','empty','scalar','array','nested','callback','failure') {
                    foreach($template in '{0}','{0:N2}','{0}/{1}','{') {
                        foreach($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                            $trace=[Collections.Generic.List[string]]::new()
                            $value=switch($shape) {
                                'null' {$null}; 'empty' {,@()}; 'scalar' {1234.5}; 'array' {,@(1234.5,'b')}; 'nested' {,@(@(1,2),'b')}
                                default {
                                    $item=[pscustomobject]@{Trace=$trace;Fail=($shape -eq 'failure')}
                                    $item | Add-Member ScriptMethod ToString { $this.Trace.Add('convert:'+ $result); if($this.Fail){throw 'conversion-failure'}; 'converted' } -Force
                                    $item
                                }
                            }
                            $errors=@();$caught=$null;$records=@();$Error.Clear()
                            try {$records=@(Read-NativeFormat -Template $template -Value $value -Trace $trace -ErrorAction $action -ErrorVariable errors 2>$null)}
                            catch {$caught=Describe-FormatError $_}
                            [pscustomobject]@{culture=$culture;shape=$shape;template=$template;action=$action;records=$records;trace=@($trace);
                                caught=$caught;errors=@($errors | ForEach-Object {Describe-FormatError $_});globalErrors=@($Error | ForEach-Object {Describe-FormatError $_})} | ConvertTo-Json -Compress -Depth 12
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-format");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-format");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(224, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
    }
}
