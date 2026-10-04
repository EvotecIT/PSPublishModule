using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConditionalMemberCapture_PreservesRecordsAndFailureRollback(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-ConditionalMember {
                [CmdletBinding()]
                param([string] $Mode)
                $state = @{ Value = 'before' }
                try {
                    $state.Value = if ($Mode -eq 'none') { }
                        elseif ($Mode -eq 'one') { 'one' }
                        elseif ($Mode -eq 'many') { 'one'; $null; 'three' }
                        elseif ($Mode -eq 'fail') { 'partial'; throw 'failed' }
                        else { $null }
                } catch { }
                return ,$state.Value
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ConditionalMemberCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);

        const string probe = """
            foreach ($mode in 'none','one','many','fail','null') {
                $value = Get-ConditionalMember -Mode $mode
                [pscustomobject]@{
                    Mode = $mode
                    Type = if ($null -eq $value) { '<null>' } else { $value.GetType().FullName }
                    Values = if ($null -eq $value) { @() } else { @($value | ForEach-Object { if ($null -eq $_) { '<null>' } else { [string] $_ } }) }
                } | ConvertTo-Json -Compress -Depth 5
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-conditional-member-capture");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-conditional-member-capture");
        Assert.True(original.ExitCode == 0 && compiled.ExitCode == 0,
            original.StandardError + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeDirectForEachArrayCapture_PreservesZeroOneAndManyRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-DirectCapture {
                [CmdletBinding()]
                param([object[]] $Items)
                [Array] $Output = foreach ($Item in $Items) { $Item }
                return ,$Output
            }
            function Get-DirectCaptureFailure {
                [CmdletBinding()]
                param([object[]] $Items)
                [Array] $Output = @('before')
                try {
                    [Array] $Output = foreach ($Item in $Items) {
                        if ($Item -eq 'bad') { throw 'failed' }
                        $Item
                    }
                } catch { }
                return ,$Output
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DirectForEachCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);

        const string probe = """
            foreach ($case in @(@(), @('one'), @('one', $null, 'three'))) {
                $result = Get-DirectCapture -Items $case
                [pscustomobject]@{
                    Type = if ($null -eq $result) { '<null>' } else { $result.GetType().FullName }
                    Count = if ($null -eq $result) { -1 } else { $result.Count }
                    Values = @($result | ForEach-Object { if ($null -eq $_) { '<null>' } else { [string] $_ } })
                } | ConvertTo-Json -Compress -Depth 5
            }
            foreach ($case in @(@('one', 'two'), @('one', 'bad', 'three'))) {
                $result = Get-DirectCaptureFailure -Items $case
                [pscustomobject]@{FailureCase = $case -join '|'; Values = @($result)} |
                    ConvertTo-Json -Compress -Depth 5
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-direct-foreach-capture");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-direct-foreach-capture");
        Assert.True(original.ExitCode == 0 && compiled.ExitCode == 0,
            original.StandardError + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeOutputCapture_PreservesRecordsAndAssignmentFailures(string framework, string host)
    {
        var loops = new[] {
            ("ConditionalInnerBreak", "$result=if ($Limit) { foreach ($i in 1,2,3) { if ($i -eq 2) { break }; $i } }; \"captured=$result\""),
            ("ConditionalInnerContinue", "$result=if ($Limit) { foreach ($i in 1,2,3) { if ($i -eq 2) { continue }; $i } }; \"captured=$result\""),
            ("Conditional", "$result=if ($Limit) { 'first'; 'second' } else { 'empty' }; \"captured=$result\""),
            ("ConditionalEmpty", "$result=if ($Limit) { $discard=1 }; \"captured=$result\""),
            ("ConditionalNull", "$result=if ($Limit) { ,$null } else { $null }; \"captured=$result\""),
            ("ConditionalFailure", "$result='before'; $result=if (1 / $Zero) { 'first' } else { 'second' }; \"captured=$result\""),
            ("ConditionalPipeline", "$result=if ($Limit) { Write-Output 'first'; Write-Output 'second' }; \"captured=$result\"; Write-Output 'outside'"),
            ("ConditionalTyped", "$Seed=if ($Limit) { '2'; '3' } else { '2' }; \"seed=$Seed\""),
            ("For", "$result=for ($i=0; $i -lt $Limit; $i++) { \"$i\" }; \"captured=$result\""),
            ("While", "$i=0; $result=while ($i -lt $Limit) { \"$i\"; $i++ }; \"captured=$result\""),
            ("DoWhile", "$i=0; $result=do { \"$i\"; $i++ } while ($i -lt $Limit); \"captured=$result\""),
            ("DoUntil", "$i=0; $result=do { \"$i\"; $i++ } until ($i -ge $Limit); \"captured=$result\""),
            ("Empty", "$result=for ($i=0; $i -lt $Limit; $i++) { $discard=$i }; \"captured=$result\""),
            ("Nested", "$result=for ($i=0; $i -lt $Limit; $i++) { $inner=for ($j=0; $j -lt 2; $j++) { \"$j\" }; \"inner=$inner\" }; \"captured=$result\""),
            ("BodyFailure", "$result='before'; $result=for ($i=0; $i -lt $Limit; $i++) { \"$i\"; $bad=1 / $Zero; 'after' }; \"captured=$result\""),
            ("TypedDestination", "$Seed=for ($i=0; $i -lt $Limit; $i++) { '2' }; \"seed=$Seed\""),
            ("Pipeline", "$result=for ($i=0; $i -lt $Limit; $i++) { Write-Output $i }; \"captured=$result\"; Write-Output 'outside'"),
            ("NestedPipeline", "$result=for ($i=0; $i -lt $Limit; $i++) { $inner=for ($j=0; $j -lt 2; $j++) { Write-Output $j }; Write-Output \"inner=$inner\" }; \"captured=$result\"; Write-Output 'outside'"),
            ("PipelineFailure", "$result='before'; $result=for ($i=0; $i -lt $Limit; $i++) { Write-Output $i; Write-Error 'body failure'; Write-Output 'after' }; \"captured=$result\"; Write-Output 'outside'"),
            ("PipelineVariables", "$seen=@(); $result=for ($i=0; $i -lt $Limit; $i++) { Write-Output $i -OutVariable +seen | ForEach-Object { \"value=$_\" }; \"seen=$seen\" }; \"captured=$result;seen=$seen\"; Write-Output 'outside'"),
            ("PipelineMergedError", "$result=for ($i=0; $i -lt $Limit; $i++) { Write-Output $i; Write-Error 'merged failure' 2>&1; Write-Output 'after' }; \"captured=$result\"; Write-Output 'outside'"),
            ("PipelineTransfers", "$result=for ($i=0; $i -lt $Limit; $i++) { Write-Output $i; if ($i -eq 0) { continue }; if ($i -eq 1) { break }; Write-Output 'after' }; \"captured=$result\"; Write-Output 'outside'"),
            ("PipelineTypedDestination", "$Seed=for ($i=0; $i -lt $Limit; $i++) { Write-Output '2' }; \"seed=$Seed\"; Write-Output 'outside'") };
        var source = string.Join(Environment.NewLine, loops.Select(loop =>
            "function Read-NativeCapture" + loop.Item1 +
            " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Limit=3,[object]$Zero=0); " +
            loop.Item2 + "; return \"end=$i;status=$?\" }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCaptures", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(loops.Length == result.Manifest!.CompiledMethods, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $cases=@(0,1,3,'3',$null,'bad')
            foreach ($command in (Get-Command -Module MODULE_NAME -Name 'Read-NativeCapture*' | Sort-Object Name).Name) {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    for ($index=0;$index -lt $cases.Count;$index++) {
                        $Error.Clear(); $faults=@(); $records=@(); $emitted=@(); $caught=$null
                        if ($action -eq 'Stop') {
                            try { $records=@(& $command -Limit $cases[$index] -ErrorAction $action -ErrorVariable faults -OutVariable emitted 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine }
                        } else {
                            $records=@(& $command -Limit $cases[$index] -ErrorAction $action -ErrorVariable faults -OutVariable emitted 2>$null)
                        }
                        [pscustomobject]@{command=$command;case=$index;action=$action;records=$records;emitted=@($emitted);caught=$caught;
                            faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine });
                            errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine })} | ConvertTo-Json -Compress -Depth 6
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-capture-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-capture-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(loops.Length * 6 * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
