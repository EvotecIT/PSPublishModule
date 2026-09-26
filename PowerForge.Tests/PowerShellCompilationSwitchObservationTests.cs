using System.Management.Automation.Language;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("catch { $_.Exception.Message }", false)]
    [InlineData("catch { $PSItem.Exception.Message }", false)]
    [InlineData("catch { $switch }", true)]
    [InlineData("catch { { $_ } }", true)]
    [InlineData("catch { switch ('inner') { default { $_ } } }", true)]
    [InlineData("catch { $_ }; $_", true)]
    public void SwitchObservation_DistinguishesImmediateCatchErrorFromSwitchItem(string body, bool observed)
    {
        var syntax = Parser.ParseInput("switch ('outer') { default { try { throw 'error' } " + body + " } }",
            out _, out var errors);
        Assert.Empty(errors);
        var statement = Assert.IsType<SwitchStatementAst>(syntax.EndBlock!.Statements[0]);
        Assert.Equal(observed, PowerShellAutomaticVariableObservationPolicy.ObservesSwitchState(statement));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeSwitchCatch_PreservesCaughtErrorAndPostCatchItem(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-SwitchCatch {
                [CmdletBinding()]
                param([string] $Value)
                switch ($Value) {
                    'fail' {
                        try { throw [System.Exception]::new('caught') }
                        catch { "error:$($_.Exception.Message)|$($PSItem.Exception.Message)" }
                    }
                    default { 'ok' }
                }
            }
            function Get-SwitchCatchThenItem {
                [CmdletBinding()]
                param([string] $Value)
                switch ($Value) {
                    default {
                        try { throw [System.Exception]::new('caught') }
                        catch { $_.Exception.Message }
                        "item:$_"
                    }
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.SwitchCatch",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach ($value in @('fail', 'other', '', $null)) {
                'compiled=' + (@(Get-SwitchCatch -Value $value) -join ';')
                'hosted=' + (@(Get-SwitchCatchThenItem -Value $value) -join ';')
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-switch-catch");
        var generated = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "generated-switch-catch");
        Assert.True(original.ExitCode == 0 && generated.ExitCode == 0,
            original.StandardError + generated.StandardError);
        Assert.Equal(original.StandardOutput, generated.StandardOutput);
        Assert.Contains("error:caught|caught", generated.StandardOutput);
        Assert.Contains("item:fail", generated.StandardOutput);
    }
}
