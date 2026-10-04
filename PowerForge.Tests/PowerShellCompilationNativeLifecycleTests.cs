namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> PowerShell7LifecycleHosts()
        => StatementErrorHosts().Where(static host => (string)host[0] != "net472");

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(PowerShell7LifecycleHosts))]
    public void NativeLifecycle_PreservesCleanOnlyAndEmptyClauseScheduling(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-CleanOnly { [CmdletBinding()] param([object]$Trace,[Parameter(ValueFromPipeline)][object]$Value) clean { $Trace.Add('clean'); 'hidden' } }
            function Invoke-EmptyProcessClean { [CmdletBinding()] param([object]$Trace,[Parameter(ValueFromPipeline)][object]$Value) process { } clean { $Trace.Add('clean'); 'hidden' } }
            function Invoke-BeginClean { [CmdletBinding()] param([object]$Trace,[Parameter(ValueFromPipeline)][object]$Value) begin { $Trace.Add('begin') } clean { $Trace.Add('clean'); 'hidden' } }
            function Invoke-EndClean { [CmdletBinding()] param([object]$Trace,[Parameter(ValueFromPipeline)][object]$Value) end { $Trace.Add('end') } clean { $Trace.Add('clean'); 'hidden' } }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCleanScheduling", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            foreach($name in 'Invoke-CleanOnly','Invoke-EmptyProcessClean','Invoke-BeginClean','Invoke-EndClean') {
                foreach($style in 'direct','empty','many') {
                    $trace=[Collections.Generic.List[string]]::new()
                    $records=if($style -eq 'direct') {@(& $name -Trace $trace)} elseif($style -eq 'empty') {@(@() | & $name -Trace $trace)} else {@(1,2 | & $name -Trace $trace)}
                    [pscustomobject]@{name=$name;style=$style;records=@($records);trace=@($trace)} | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, fixture.RootPath, "original-clean-scheduling");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe, fixture.RootPath, "compiled-clean-scheduling");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(12, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeFunctionAndScriptBlockHosts))]
    public void NativeLifecycle_PreservesFailuresReturnsBindingAndClean(string framework, string host, bool scriptBlock)
    {
        var clean = framework == "net472" ? "" : "clean { $Trace.Add('clean:'+ $count); 'discarded-clean-output' }";
        var source = """
            function Read-LifecycleFault {
                [CmdletBinding(DefaultParameterSetName='Value')] param(
                    [Parameter(ValueFromPipeline,ParameterSetName='Value')][AllowNull()][string]$Value,
                    [Parameter(ParameterSetName='Other')][switch]$Other,
                    [object]$Trace, [string]$Fault='none', [string]$Phase='process', [switch]$Return)
                begin {
                    $count=0; $Trace.Add('begin:'+ $PSCmdlet.ParameterSetName)
                    if ($Phase -eq 'begin') { if ($Fault -eq 'throw') { throw 'failure' }; if ($Fault -eq 'error') { Write-Error 'failure' }; if ($Return) { return 'return-begin' } }
                    'begin'
                }
                process {
                    $count+=1; $Trace.Add('process:'+ $Value)
                    'before:'+ $Value
                    if ($Phase -eq 'process') { if ($Fault -eq 'throw') { throw 'failure' }; if ($Fault -eq 'error') { Write-Error 'failure' }; if ($Return) { return 'return-process' } }
                    'after:'+ $Value
                }
                end {
                    $Trace.Add('end:'+ $count)
                    if ($Phase -eq 'end') { if ($Fault -eq 'throw') { throw 'failure' }; if ($Fault -eq 'error') { Write-Error 'failure' }; if ($Return) { return 'return-end' } }
                    'end:'+ $count
                }
            CLEAN_CLAUSE
            }
            """.Replace("CLEAN_CLAUSE", clean);
        if (scriptBlock)
            source = source.Replace("function Read-LifecycleFault {", "function Get-LifecycleBlock { $block={") + "\n$block\n}";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLifecycleFailures", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 1, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        if (scriptBlock) Assert.Single(Assert.Single(result.Manifest.UnitDispositionLedger.Entries).RegionGraph!.ScriptBlocks);
        else
        {
            var lifecycle = Assert.Single(result.Manifest.Lifecycles);
            Assert.Equal(PowerShellCompilationLifecycleExecution.CompiledNativeCallbacks, lifecycle.Execution);
            Assert.Equal(framework != "net472", lifecycle.HasClean);
            Assert.Equal(64, lifecycle.SourceSha256.Length);
        }
        const string probe = """
            function Describe-LifecycleError($value) {
                $record=if($value -is [Management.Automation.ErrorRecord]) {$value} else {$value.ErrorRecord}
                if($null -eq $record) { return $value.GetType().FullName }
                return $record.FullyQualifiedErrorId+':'+$record.Exception.GetType().FullName
            }
            foreach($phase in 'begin','process','end') {
                foreach($fault in 'none','error','throw') {
                    foreach($action in 'Continue','SilentlyContinue','Stop') {
                        foreach($return in $false,$true) {
                            foreach($stop in $false,$true) {
                                $trace=[Collections.Generic.List[string]]::new(); $errors=@(); $caught=$null
                                $records=[Collections.Generic.List[string]]::new()
                                try {
                                    if($stop) { 'a','b' | Read-LifecycleFault -Trace $trace -Fault $fault -Phase $phase -Return:$return -ErrorAction $action -ErrorVariable errors 2>$null | Select-Object -First 1 | ForEach-Object {$records.Add($_)} }
                                    else { 'a','b' | Read-LifecycleFault -Trace $trace -Fault $fault -Phase $phase -Return:$return -ErrorAction $action -ErrorVariable errors 2>$null | ForEach-Object {$records.Add($_)} }
                                } catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName }
                                [pscustomobject]@{phase=$phase;fault=$fault;action=$action;return=$return;stop=$stop;records=@($records);trace=@($trace);
                                    errors=@($errors | ForEach-Object {Describe-LifecycleError $_});caught=$caught} | ConvertTo-Json -Compress
                            }
                        }
                    }
                }
            }
            """;
        var invocationProbe = (scriptBlock ? "$command=Get-LifecycleBlock; " : "$command='Read-LifecycleFault'; ") +
            probe.Replace("| Read-LifecycleFault", "| & $command");
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + invocationProbe, fixture.RootPath, "original-lifecycle-fault");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + invocationProbe, fixture.RootPath, "compiled-lifecycle-fault");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(108, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLifecycle_PreservesClauseOrderLiveStorageAndPipelineBinding(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Lifecycle {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][string]$Value, [object]$Trace)
                begin { $count=0; $Trace.Add('begin'); 'begin' }
                process { $count+=1; $Trace.Add('process:'+ $Value); 'item:'+ $Value; 'count:'+ $count }
                end { $Trace.Add('end:'+ $count); 'end:'+ $count }
            }
            function Read-ProcessOnly {
                [CmdletBinding()] param([Parameter(ValueFromPipelineByPropertyName)][string]$Value, [object]$Trace)
                process { $copy=$Value; $Trace.Add('process:'+ $copy); $copy }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLifecycle", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 2, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            foreach($name in 'Read-Lifecycle','Read-ProcessOnly') {
                foreach($shape in 'zero','one','many','null') {
                    foreach($stop in $false,$true) {
                        $trace=[Collections.Generic.List[string]]::new()
                        $inputValues=switch($shape) { 'zero' {,@()} 'one' {,@('a')} 'many' {,@('a','b','c')} 'null' {,@($null)} }
                        if($name -eq 'Read-ProcessOnly') { $inputValues=@($inputValues | ForEach-Object { [pscustomobject]@{Value=$_} }) }
                        $records=if($stop) { @($inputValues | & $name -Trace $trace | Select-Object -First 1) } else { @($inputValues | & $name -Trace $trace) }
                        [pscustomobject]@{name=$name;shape=$shape;stop=$stop;records=@($records);trace=@($trace)} | ConvertTo-Json -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-lifecycle");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-lifecycle");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardError, compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(16, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
    }
}
