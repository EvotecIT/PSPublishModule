using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeFunctionSlots_PreserveCallbackMutationAndInterpolationOrder(string framework, string host)
    {
        const string source = """
            function Read-NativeSlots {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1, [object]$Token)
                $copy='before'
                return "$Token|$copy"
            }
            function New-NativeSlotToken {
                $token=[pscustomobject]@{}
                Add-Member -InputObject $token -MemberType ScriptMethod -Name ToString -Force -Value {
                    $before=(Get-Variable -Scope 1 -Name copy).Value
                    Set-Variable -Scope 1 -Name copy -Value 'changed'
                    return "seen=$before"
                }
                $token
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeSlotCallback", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        const string probe = "Read-NativeSlots -Token (New-NativeSlotToken); Read-NativeSlots -Token plain";
        foreach (var modulePath in new[] { fixture.ScriptPath, result.ArtifactPath! })
        {
            var observed = RunStatementErrorProbe(host,
                "Import-Module '" + EscapeStatementErrorPath(modulePath) + "'; " + probe,
                fixture.RootPath, Path.GetFileName(modulePath) + "-native-slot-callback");
            Assert.True(observed.ExitCode == 0 && string.IsNullOrWhiteSpace(observed.StandardError),
                observed.StandardOutput + observed.StandardError);
            Assert.Equal(new[] { "seen=before|changed", "plain|before" },
                observed.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
