namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> CapturedTransferHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { false, true }
            .Select(native => new object[] { configuration[0], configuration[1], native }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(CapturedTransferHosts))]
    public void ConditionalCapture_PreservesTransfersToEnclosingLoops(string framework, string host, bool native)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-CapturedBreak {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1)
                foreach ($i in 1,2) {
                    $result=if ($i -eq 1) { 'before-break'; break } else { 'value' }
                    'after'
                }
                'done'
            }
            function Read-CapturedContinue {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1)
                foreach ($i in 1,2) {
                    $result=if ($i -eq 1) { 'before-continue'; continue } else { 'value' }
                    'after'; ,$result
                }
                'done'
            }
            """.Replace("[ValidateRange(1,9)]", native ? "[ValidateRange(1,9)]" : "", StringComparison.Ordinal)
                .Replace("foreach ($i in 1,2)", native ? "foreach ($i in 1,2)" : "for ([int]$i=1; $i -le 2; $i++)", StringComparison.Ordinal), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CapturedTransfers", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Read-CapturedBreak", "Read-CapturedContinue" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == name);
            Assert.True(!native == unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.Equal(native, unit.RetainedHostedSource);
            if (native) Assert.Contains(unit.DiagnosticChain, cause => cause.Message.Contains("enclosing-control-flow", StringComparison.Ordinal));
        }
        const string probe = """
            foreach ($name in 'Read-CapturedBreak','Read-CapturedContinue') {
                [pscustomobject]@{name=$name;records=@(& $name)} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-capture-transfer");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-capture-transfer");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
