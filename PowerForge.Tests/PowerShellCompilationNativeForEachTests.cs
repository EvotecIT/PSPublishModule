using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeForEach_PreservesIteratorStorageNestedLoopsAndTransfers(string framework, string host)
    {
        var bodies = new[]
        {
            "foreach ($item in $Value) { $current=$foreach.Current; \"item=$item;current=$current\"; if ($item -eq 'continue') { continue }; if ($item -eq 'break') { break }; Write-Output 'tail' }; \"item-after=$item\"",
            "foreach ($item in $Value) { $outer=$foreach; foreach ($part in @('a','b')) { \"$item/$part\" }; $same=[object]::ReferenceEquals($outer,$foreach); \"restored=$same\" }",
            "$foreach='outer'; foreach ($item in $Value) { \"item=$item\"; $foreach='changed' }; \"restored=$foreach\"",
            "foreach ($item in $Value) { foreach ($part in $foreach.Current) { \"part=$part\" }; $current=$foreach.Current; \"outer=$current\" }",
            "try { foreach ($item in $Value) { \"item=$item\"; $bad=1/0 } } catch { $cleared=$null -eq $foreach; \"caught=$cleared\" } finally { $cleared=$null -eq $foreach; \"finally=$cleared\" }",
            "[ValidateRange(1,3)][int]$item=1; foreach ($item in $Value) { \"item=$item\" }; \"item-after=$item\"",
            "foreach ($true in $Value) { 'readonly-body' }",
            "$script:LoopItem='prior'; foreach ($script:LoopItem in $Value) { \"item=$script:LoopItem\" }; \"item-after=$script:LoopItem\"",
            "Set-Variable -Name foreach -Value 'locked' -Option ReadOnly; foreach ($item in $Value) { \"item=$item\" }; \"iterator=$foreach\""
        };
        var source = string.Join(Environment.NewLine, bodies.Select((body, index) =>
            "function Read-NativeForEach" + index +
            " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value);\n" + body +
            ";\n$cleared=$null -eq $foreach; \"iterator-after=$cleared\"; Write-Output 'outside'\n}"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeForEach", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == bodies.Length, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            $cases=@(@{value=$null},@{value=@()},@{value=2},@{value=@(1,2)},@{value=@('bad',2)},@{value=@('continue','break',3)},@{value=@(,@(1,2))})
            foreach ($name in (Get-Command -Module MODULE_NAME -Name 'Read-NativeForEach*' | Sort-Object Name).Name) {
                for ($index=0;$index -lt $cases.Count;$index++) {
                    foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                        $Error.Clear(); $records=@(); $emitted=@(); $caught=$null
                        if ($preference -eq 'Stop') {
                            try { $records=@(& $name -Value $cases[$index].value -ErrorAction $preference -OutVariable emitted 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                        } else {
                            $records=@(& $name -Value $cases[$index].value -ErrorAction $preference -OutVariable emitted 2>$null)
                        }
                        [pscustomobject]@{name=$name;case=$index;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                            errors=@($Error | ForEach-Object { [ordered]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line;position=$_.InvocationInfo.PositionMessage} })} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-foreach-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-foreach-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(bodies.Length * 7 * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
