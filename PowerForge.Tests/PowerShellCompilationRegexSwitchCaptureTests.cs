using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeRegexSwitch_PreservesCapturesEnumerationAndAutomaticState(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-RegexSwitch {
                param([object] $Value)
                $Matches = @{ seed = 'before' }
                $_ = 'outer-item'
                $switch = 'outer-switch'
                switch -Regex ($Value) {
                    '^(?<word>[a-z]+)-([0-9]+)$' {
                        "hit:$($_):$($Matches.word):$($Matches[2])"
                        switch -Regex ('inner') { '^(in)(ner)$' { "nested:$($Matches[1]):$($Matches[2])" } }
                        "after-nested:$($Matches[0]):$_"
                    }
                    '^stop$' { 'stop'; break }
                    '^skip$' { 'skip'; continue }
                    default { "miss:$_" }
                }
                "after:$($_):$($switch):$($Matches[0]):$($Matches.seed)"
            }
            function Read-CaseRegexSwitch {
                param([string] $Value)
                switch -Regex -CaseSensitive ($Value) {
                    '^(?<name>i)$' { $Matches.name }
                    default { 'miss' }
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RegexSwitchCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, entry => Assert.False(entry.RetainedHostedSource));
        const string probe = """
            foreach ($culture in 'en-US','tr-TR') {
                [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
                $cohorts = [object[]]::new(4)
                $cohorts[0] = @('AB-12','missing','skip','xy-3')
                $cohorts[1] = @('stop','unreached')
                $cohorts[2] = @()
                $cohorts[3] = @($null)
                foreach ($values in $cohorts) {
                    [ordered]@{ culture = $culture; output = @(Read-RegexSwitch -Value $values) } |
                        ConvertTo-Json -Depth 5 -Compress
                }
                'case=' + (@(Read-CaseRegexSwitch 'i'; Read-CaseRegexSwitch 'I') -join '|')
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-regex-switch");
        var generated = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "generated-regex-switch");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, generated.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(generated.StandardError), generated.StandardError);
        Assert.Equal(original.StandardOutput, generated.StandardOutput);
        Assert.Contains("nested:in:ner", generated.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("after:outer-item:outer-switch", generated.StandardOutput, StringComparison.Ordinal);
    }
}
