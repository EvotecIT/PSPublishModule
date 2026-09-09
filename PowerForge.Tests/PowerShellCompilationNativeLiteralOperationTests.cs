using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLiteralOperations_PreservePromotionFilteringAndErrors(string framework, string host)
    {
        var expressions = new[] { "1/0", "5/2", "4/2", "2147483647+1", "9223372036854775807+1",
            "1.0/0.0", "3%0", "1 -shl 33", "'A' -ceq 'a'", "'10' -eq 10", "@('A','a') -eq 'a'" };
        var source = string.Join(Environment.NewLine, expressions.Select((expression, index) =>
            "function Read-LiteralOperation" + index + " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2);\n" +
            "$value='prior'; $value=" + expression + "; \"status=$?\";\n" +
            "Get-Variable value | ForEach-Object { $_.Value.GetType().FullName; $_.Value }; 'outside'\n}"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLiteralOperations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == expressions.Length, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            foreach ($name in (Get-Command -Module MODULE_NAME -Name 'Read-LiteralOperation*' | Sort-Object Name).Name) {
                foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                    $Error.Clear(); $records=@(); $caught=$null
                    if ($preference -eq 'Stop') {
                        try { $records=@(& $name -ErrorAction $preference 2>$null) }
                        catch { $caught=$_.FullyQualifiedErrorId }
                    } else { $records=@(& $name -ErrorAction $preference 2>$null) }
                    [pscustomobject]@{name=$name;preference=$preference;records=$records;caught=$caught;
                        errors=@($Error | ForEach-Object { [ordered]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line;position=$_.InvocationInfo.PositionMessage} })} | ConvertTo-Json -Depth 12 -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-literal-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-literal-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(expressions.Length * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
