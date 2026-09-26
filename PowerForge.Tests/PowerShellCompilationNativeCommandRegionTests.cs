using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandResultSwitchPreservesIterationAndFailure(string framework, string host)
    {
        const string source = """
            function Get-SwitchInput {
                [CmdletBinding()] param([string]$Mode)
                if ($Mode -eq 'Zero') { return }
                if ($Mode -eq 'Null') { $null; return }
                if ($Mode -eq 'Many') { 'a'; 'b'; 'a'; return }
                if ($Mode -eq 'Array') { ,@('a','b'); return }
                if ($Mode -eq 'Numeric') { 1; return }
                if ($Mode -eq 'Upper') { 'A'; return }
                if ($Mode -eq 'PartialFail') { 'a'; throw 'stopped' }
                'a'
            }
            function Get-SwitchResult {
                [CmdletBinding()] param([string]$Mode)
                switch (Get-SwitchInput -Mode $Mode) {
                    'a' { 'first' }
                    'a' { 'second' }
                    'b' { 'bee'; break }
                    '1' { 'number' }
                    '' { 'null-match' }
                    default { 'other' }
                }
            }
            Export-ModuleMember -Function Get-SwitchResult
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeStreamSwitch", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == "Get-SwitchResult");
        Assert.True(unit.EmittedClrMethod, string.Join(" | ", unit.DiagnosticChain.Select(cause => cause.Message)));
        Assert.Equal(1, unit.RuntimeCommandRegions);
        const string probe = """
            foreach($mode in 'Zero','Null','One','Many','Array','Numeric','Upper','PartialFail') {
                $values=@(); $caught=$null
                try { $values=@(Get-SwitchResult -Mode $mode) } catch { $caught=$_.FullyQualifiedErrorId }
                [pscustomobject]@{mode=$mode;values=@($values);caught=$caught} | ConvertTo-Json -Compress -Depth 5
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "stream-switch-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "stream-switch-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Fact]
    public void NativeLiteralCommandValuesDoNotWidenStrictAdmission()
    {
        const string direct = "function Get-Literal { [pscustomobject]@{ Result = Get-Date } }";
        var document = PowerShellSourceParser.Parse(direct, Path.Combine(Path.GetTempPath(), "literal-command-strict.ps1"));
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(strict.Emitted.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLiteralCommandValuesPreserveCaptureAndObjectIdentity(string framework, string host)
    {
        const string source = """
            function Get-LiteralRecords {
                [CmdletBinding()] param([string]$Mode)
                if ($Mode -eq 'Zero') { return }
                if ($Mode -eq 'One') { 17; return }
                if ($Mode -eq 'Many') { 17; 23; return }
                if ($Mode -eq 'PartialFail') { 17; throw 'stopped after output' }
                throw 'stopped'
            }
            function Get-LiteralMap {
                [CmdletBinding()] param([string]$Mode)
                $value = @{ Before = 'ready'; Result = Get-LiteralRecords -Mode $Mode; After = 'done' }
                return $value['Result']
            }
            function Get-LiteralObject {
                [CmdletBinding()] param([string]$Mode)
                [pscustomobject]@{ Before = 'ready'; Result = Get-LiteralRecords -Mode $Mode; After = 'done' }
            }
            Export-ModuleMember -Function Get-LiteralMap, Get-LiteralObject
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLiteralValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Get-LiteralMap", "Get-LiteralObject" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == name);
            Assert.True(unit.EmittedClrMethod, name + ": " + string.Join(" | ", unit.DiagnosticChain.Select(cause => cause.Message)));
            Assert.False(unit.RetainedHostedSource);
            Assert.Equal(1, unit.RuntimeCommandRegions);
        }
        const string probe = """
            foreach($name in 'Get-LiteralMap','Get-LiteralObject') {
                foreach($mode in 'Zero','One','Many','Fail','PartialFail') {
                    $caught=$null; $records=@()
                    try { $records=@(& $name -Mode $mode) } catch { $caught=$_.FullyQualifiedErrorId }
                    $described=@(foreach($record in $records) {
                        if($record -is [pscustomobject]) {
                            [pscustomobject]@{kind='object';type=$record.GetType().FullName;before=$record.Before;after=$record.After;resultType=if($null -ne $record.Result){$record.Result.GetType().FullName}else{$null};result=@($record.Result)}
                        } else {
                            [pscustomobject]@{kind='value';type=if($null -ne $record){$record.GetType().FullName}else{$null};value=$record}
                        }
                    })
                    [pscustomobject]@{name=$name;mode=$mode;records=$described;caught=$caught} | ConvertTo-Json -Compress -Depth 8
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "literal-command-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "literal-command-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandRegions_CompileInterleavedPipelinesAndPrivateLocalCalls(string framework, string host)
    {
        const string source = """
            function Invoke-NativeRegionWorkflow {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Values)
                $Seen='before'
                Write-Output "begin:$Seed"
                $Seen='middle'
                for($index=0;$index -lt $Values.Length;$index++) {
                    Read-NativeRegionHelper -Number $Values[$index]
                }
                Write-Output "end:$Seen"
                "state:$Seen"
            }
            function Read-NativeRegionHelper {
                param([int]$Number)
                "helper:$Seen"
                Read-NativeRegionLeaf -Number $Number
            }
            function Read-NativeRegionLeaf {
                param([int]$Number)
                "leaf:$Seen"
                return $Number * 2
            }
            Export-ModuleMember -Function Invoke-NativeRegionWorkflow
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCommandRegions", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(item => item.DiagnosticChain.Select(cause => item.Name + ": " + cause.Message))));
        var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, item => item.Name == "Invoke-NativeRegionWorkflow");
        Assert.True(unit.EmittedClrMethod);
        Assert.False(unit.RetainedHostedSource);
        Assert.Equal(3, unit.RuntimeCommandRegions);
        var helper = Assert.Single(result.Manifest.UnitDispositionLedger.Entries, item => item.Name == "Read-NativeRegionHelper");
        Assert.True(helper.EmittedClrMethod);
        Assert.False(helper.RetainedHostedSource);
        var leaf = Assert.Single(result.Manifest.UnitDispositionLedger.Entries, item => item.Name == "Read-NativeRegionLeaf");
        Assert.True(leaf.EmittedClrMethod);
        Assert.False(leaf.RetainedHostedSource);
        const string probe = """
            foreach($values in @(@{value=@()},@{value=@(3)},@{value=@(1,2,3)},@{value=@(1,'bad',3)})) {
                foreach($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                    foreach($first in $false,$true) {
                        $Error.Clear(); $records=@(); $emitted=@(); $caught=$null
                        try {
                            if($first) { $records=@(Invoke-NativeRegionWorkflow -Values $values.value -ErrorAction $preference -OutVariable emitted 2>$null | Select-Object -First 2) }
                            else { $records=@(Invoke-NativeRegionWorkflow -Values $values.value -ErrorAction $preference -OutVariable emitted 2>$null) }
                        } catch { $caught=$_.FullyQualifiedErrorId }
                        [pscustomobject]@{records=$records;emitted=@($emitted);caught=$caught;errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId })} | ConvertTo-Json -Compress -Depth 12
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-region-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-region-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(32, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(6).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
