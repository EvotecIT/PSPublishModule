using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("$script:Counter++", false, true)]
    [InlineData("--$script:Counter", false, true)]
    [InlineData("(++$script:Counter)", true, true)]
    [InlineData("($script:Counter=2)", true, false)]
    [InlineData("($script:Counter+=2)", true, true)]
    [InlineData("$script:Counter=2", false, false)]
    [InlineData("[void]($script:Counter++)", false, true)]
    public void NativeMutations_DescribeSessionStorageAndActualOutput(string body, bool emits, bool reads)
    {
        using var fixture = ArtifactFixture.Create(
            "function Read-MutationGraph { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1); " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.NativeMutationGraph", "CompiledPowerShell", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var graph = Assert.IsType<PowerShellCompilationRegionGraph>(Assert.Single(typed.Methods).RegionGraph);
        var region = Assert.Single(graph.Regions);
        Assert.Contains("PowerShellSessionVariable:SCRIPT:COUNTER", region.Mutations);
        Assert.Contains("PowerShellSessionState:*", region.Mutations);
        Assert.Equal(reads, region.Inputs.Contains("PowerShellSessionVariable:SCRIPT:COUNTER", StringComparer.Ordinal));
        Assert.Contains("PowerShellSessionStateError", region.Errors);
        Assert.Equal(emits, region.Streams.Contains("Success", StringComparer.Ordinal));
        Assert.DoesNotContain("Local:SCRIPT:COUNTER", region.Mutations);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeMutations_PreserveImplicitLocalsAndAutomaticTargets(string framework, string host)
    {
        var targets = new[] { "Counter", "local:Counter", "private:Counter", "null", "true" };
        var functions = string.Join(Environment.NewLine, targets.SelectMany((target, index) =>
            new[] { "++TARGET", "--TARGET", "TARGET++", "TARGET--" }.Select((mutation, operation) =>
                "function Read-Implicit" + index + operation +
                " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[int]$StrictMode); if($StrictMode -eq 0) { Set-StrictMode -Off } else { Set-StrictMode -Version $StrictMode }; (" +
                mutation.Replace("TARGET", "$" + target, StringComparison.Ordinal) + "); 'continued' }")));
        using var fixture = ArtifactFixture.Create(functions, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeImplicitMutations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(20, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            foreach($command in (Get-Command -Module $m.Name -Name 'Read-Implicit*' | Sort-Object Name).Name) {
                foreach($strict in 0,2) {
                    $faults=@(); $Error.Clear()
                    $records=@(& $command -StrictMode $strict -ErrorVariable faults 2>$null)
                    [pscustomobject]@{command=$command;strict=$strict;records=$records;
                        errors=@($faults | ForEach-Object {$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName})} | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe, fixture.RootPath, "native-implicit-mutation");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe, fixture.RootPath, "native-implicit-mutation");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(40, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeMutations_PreserveScopedStorageWithoutLocalAssignments(string framework, string host)
    {
        var mutations = new[] { "++TARGET", "--TARGET", "TARGET++", "TARGET--", "TARGET=2", "TARGET+=2" };
        var functions = string.Join(Environment.NewLine, new[] { "script", "global" }.SelectMany(scope =>
            mutations.SelectMany((mutation, index) => new[] { false, true }.Select(valueResult =>
                "function Read-Scoped" + scope + index + (valueResult ? "Value" : "Statement") +
                " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1); " +
                (valueResult ? "(" : "") + mutation.Replace("TARGET", "$" + scope + ":Counter", StringComparison.Ordinal) +
                (valueResult ? ")" : "") + "; 'continued'; $" + scope + ":Counter }"))));
        using var fixture = ArtifactFixture.Create(functions, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeScopedMutations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(24, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            foreach ($command in (Get-Command -Module $m.Name -Name 'Read-Scoped*' | Sort-Object Name).Name) {
                $scope=if($command -like '*script*') {'Script'} else {'Global'}
                foreach($case in 'missing','null','intmax','longmax','uintmax','single','text','array','constrained','readonly') {
                    & $m {
                        param($scope,$case)
                        Remove-Variable Counter -Scope $scope -Force -ErrorAction Ignore
                        switch($case) {
                            null { Set-Variable Counter -Scope $scope -Value $null }
                            intmax { Set-Variable Counter -Scope $scope -Value ([int]::MaxValue) }
                            longmax { Set-Variable Counter -Scope $scope -Value ([long]::MaxValue) }
                            uintmax { Set-Variable Counter -Scope $scope -Value ([uint64]::MaxValue) }
                            single { Set-Variable Counter -Scope $scope -Value ([single]0.1) }
                            text { Set-Variable Counter -Scope $scope -Value 'bad' }
                            array { Set-Variable Counter -Scope $scope -Value @(1,2) }
                            constrained {
                                if($scope -eq 'Script') { [ValidateRange(1,9)][int]$script:Counter=9 }
                                else { [ValidateRange(1,9)][int]$global:Counter=9 }
                            }
                            readonly { Set-Variable Counter -Scope $scope -Value 3 -Option ReadOnly }
                        }
                    } $scope $case
                    $Error.Clear(); $faults=@()
                    $records=@(& $command -ErrorAction Continue -ErrorVariable faults 2>$null)
                    [pscustomobject]@{command=$command;case=$case;records=@($records | ForEach-Object {
                        if($null -eq $_) {'null'} else {$_.GetType().FullName+':'+[string]$_}
                    });faults=@($faults | ForEach-Object {$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName})} | ConvertTo-Json -Compress -Depth 6
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe, fixture.RootPath, "native-scoped-mutation");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe, fixture.RootPath, "native-scoped-mutation");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(240, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
