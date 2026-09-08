using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> InterpolationObserverHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { "\"value=$Value\"", "[string]$Value", "Expand-Leaf -Text $Value" }
            .Select(expression => new object[] { configuration[0], configuration[1], expression }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(InterpolationObserverHosts))]
    public void Interpolation_RetainsScriptMethodCallerObservations(string framework, string host, string expression)
    {
        const string template = """
            function Expand-Observer {
                [CmdletBinding()] param([object]$Value, [string]$Label)
                $localText='local-text'
                $text=EXPRESSION
                return "after=$localText;$text"
            }
            function Expand-Leaf { [CmdletBinding()] param([string]$Text) return "leaf=$Text" }
            function New-Observer {
                [CmdletBinding()] param([string]$Mode)
                $item=[pscustomobject]@{Mode=$Mode}
                Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Force -Value {
                    if($this.Mode -eq 'Write') { Set-Variable -Scope 1 -Name localText -Value 'changed' }
                    if($this.Mode -eq 'TypeChange') { Set-Variable -Scope 1 -Name localText -Value 42 }
                    $leaf=Expand-Leaf -Text $Label
                    "label=$Label;local=$localText;$leaf"
                }
                $item
            }
            """;
        const string probe = """
            foreach($mode in 'Read','Write','TypeChange') {
                $item=New-Observer -Mode $mode
                Expand-Observer -Value $item -Label 'parameter-text'
            }
            """;
        using var fixture = ArtifactFixture.Create(template.Replace("EXPRESSION", expression, StringComparison.Ordinal), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.InterpolationObserver", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.RuntimeFallbackUnits > 0);
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-interpolation-module-observer");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-interpolation-module-observer");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Empty(compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(1, result.Manifest.CompiledMethods);
    }
}
