using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("$Receiver.Value=$Value", false)]
    [InlineData("$Receiver.Nested[$Key]=$Value", true)]
    [InlineData("$Receiver[$Key.Name]=$Value", true)]
    [InlineData("$Receiver[$Key].Value+=$Value", true)]
    public void NativeTargetAssignments_DescribeReceiverAndIndexDependencies(string body, bool readsKey)
    {
        using var fixture = ArtifactFixture.Create(
            "function Write-Target { param([ValidateRange(1,9)][int]$Seed=1,$Receiver,$Key,$Value) " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.NativeTargetGraph", "CompiledPowerShell", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var graph = Assert.IsType<PowerShellCompilationRegionGraph>(Assert.Single(typed.Methods).RegionGraph);
        var region = Assert.Single(graph.Regions);
        Assert.Contains("PowerShellSessionVariable:RECEIVER", region.Inputs);
        Assert.Equal(readsKey, region.Inputs.Contains("PowerShellSessionVariable:KEY", StringComparer.Ordinal));
        Assert.Contains("PowerShellSessionVariable:RECEIVER.*", region.Mutations);
        Assert.Contains("PowerShellSessionState:*", region.Mutations);
        Assert.DoesNotContain("PowerShellSessionVariable:RECEIVER", region.Mutations);
        Assert.DoesNotContain("Success", region.Streams);
    }

    [Theory]
    [InlineData("$Receiver[$Key.Get()]=$Value")]
    [InlineData("$Receiver[$Key.$Property]=$Value")]
    [InlineData("$Receiver[${Key}?.Name]=$Value")]
    public void NativeTargetAssignments_KeepComputedIndexesHosted(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Write-Target { param([ValidateRange(1,9)][int]$Seed=1,$Receiver,$Key,$Property,$Value) " + body + " }", ".psm1");
        var hybrid = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.NativeTargetGraph", "CompiledPowerShell", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(hybrid.Methods);
    }

    [Fact]
    public void NativeTargetAssignments_DirectMemberIndexRemainsHybridOnlyForUntypedReceiver()
    {
        using var fixture = ArtifactFixture.Create(
            "function Write-Target { param([ValidateRange(1,9)][int]$Seed=1,[object]$Receiver,[object]$Key,$Value) $Receiver[$Key.Name]=$Value }", ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();
        var hybrid = transpiler.TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "PowerForge.NativeTargetGraph", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        var strict = transpiler.TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "PowerForge.NativeTargetGraph", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.BinaryModule);
        Assert.Single(hybrid.Methods);
        Assert.Empty(strict.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeTargetAssignments_PreserveAliasesConversionsAndErrors(string framework, string host)
    {
        var assignments = new[] {
            ("NestedIndex", "$inner=@{Value=1}; $box=@{Nested=$inner}; $key='Value'; $box.Nested[$key]=$Limit; $observed=$inner.Value; \"value=$observed\""),
            ("NestedMember", "$inner=[pscustomobject]@{Value=1}; $box=@{Nested=$inner}; $box['Nested'].Value+=$Limit; $observed=$inner.Value; \"value=$observed\""),
            ("ReceiverReplacement", "$box=@{Value=1}; $alias=$box; $box.Value=$($box=@{Value=9}; $Limit); $before=$alias.Value; $after=$box.Value; \"before=$before;after=$after\""),
            ("IndexReplacement", "$box=@{Value=1;Other=9}; $key='Value'; $box[$key]=$($key='Other'; $Limit); $before=$box.Value; $after=$box.Other; \"before=$before;after=$after\""),
            ("MemberIndexReplacement", "$box=@{Value=1;Other=9}; $key=[pscustomobject]@{Name='Value'}; $box[$key.Name]=$($key=[pscustomobject]@{Name='Other'}; $Limit); $before=$box.Value; $after=$box.Other; \"before=$before;after=$after\""),
            ("MemberIndexNull", "$box=@{Value=1}; $key=[pscustomobject]@{Name=$null}; $box[$key.Name]=$Limit; 'after'"),
            ("CompoundReplacement", "$box=@{Value=1}; $alias=$box; $box.Value+=$($box=@{Value=9}; $Limit); $before=$alias.Value; $after=$box.Value; \"before=$before;after=$after\""),
            ("MemberWrite", "$box=[pscustomobject]@{Value='old'}; $alias=$box; $box.Value=$Limit; $observed=$alias.Value; \"value=$observed\""),
            ("MemberCompound", "$box=[pscustomobject]@{Value=1}; $alias=$box; $box.Value+=$Limit; $observed=$alias.Value; \"value=$observed\""),
            ("MemberMissing", "$box=[pscustomobject]@{Other=1}; $box.Value=$Limit; $observed=$box.Other; \"other=$observed\""),
            ("MemberNull", "$box=$null; $box.Value=$Limit; 'after'"),
            ("MemberDictionary", "$box=@{Value=1}; $alias=$box; $box.Value+=$Limit; $observed=$alias.Value; \"value=$observed\""),
            ("IndexWrite", "$box=@{Value=1}; $alias=$box; $box['Value']=$Limit; $observed=$alias.Value; \"value=$observed\""),
            ("IndexCompound", "$box=@{Value=1}; $key='Value'; $box[$key]+=$Limit; $observed=$box.Value; \"value=$observed\""),
            ("IndexArray", "$box=[int[]](1,2,3); $box[1]=$Limit; \"value=$box\""),
            ("IndexBounds", "$box=[int[]](1,2,3); $key=99; $box[$key]=$Limit; \"value=$box\""),
            ("IndexNull", "$box=$null; $box[0]=$Limit; 'after'") };
        var source = string.Join(Environment.NewLine, assignments.Select(assignment =>
            "function Read-NativeTarget" + assignment.Item1 +
            " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Limit=3,[object]$Zero=0); " +
            assignment.Item2 + "; return \"end=$i;status=$?\" }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeTargets", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(assignments.Length == result.Manifest!.CompiledMethods, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $cases=@(0,1,3,'3',$null,'bad')
            foreach ($command in (Get-Command -Module MODULE_NAME -Name 'Read-NativeTarget*' | Sort-Object Name).Name) {
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
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-target-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-target-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(assignments.Length * 6 * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
