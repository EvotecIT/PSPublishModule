using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void Build_HybridLifecyclePreservesUnrepresentedValidationMetadata(string targetFramework)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows())
            return;
        using var fixture = ArtifactFixture.Create(
            """
            function Test-NumericSet {
                [CmdletBinding()] param([ValidateSet(0,1,5)][int]$Value)
                begin { $null = 1 } process { $Value }
            }
            function Test-MixedSet {
                [CmdletBinding()] param([ValidateSet('2',4)][int]$Value)
                begin { $null = 1 } process { $Value }
            }
            function Test-CaseSensitiveSet {
                [CmdletBinding()] param([ValidateSet('A','B',IgnoreCase=$false)][object]$Value)
                begin { $null = 1 } process { $Value }
            }
            function Test-ScriptValidation {
                [CmdletBinding()] param([ValidateScript({ $_ % 2 -eq 0 })][int]$Value)
                begin { $null = 1 } process { $Value }
            }
            Export-ModuleMember -Function Test-NumericSet,Test-MixedSet,Test-CaseSensitiveSet,Test-ScriptValidation
            """,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.Lifecycle.Metadata",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        const string command = """
            foreach ($name in 'Test-NumericSet','Test-MixedSet','Test-CaseSensitiveSet','Test-ScriptValidation') {
                $command = Get-Command $name
                "$name|$($command.CommandType)|$($command.Parameters.ContainsKey('Value'))"
            }
            0,1,5 | ForEach-Object { Test-NumericSet -Value $_ }
            2,4 | ForEach-Object { Test-MixedSet -Value $_ }
            Test-CaseSensitiveSet -Value 'A'
            Test-ScriptValidation -Value 2
            foreach ($case in @(
                @{Name='Test-NumericSet';Value=2}, @{Name='Test-MixedSet';Value=3},
                @{Name='Test-CaseSensitiveSet';Value='a'}, @{Name='Test-ScriptValidation';Value=3}
            )) {
                try { & $case.Name -Value $case.Value -ErrorAction Stop; 'accepted-invalid' }
                catch { "rejected:$($case.Name)" }
            }
            """;
        var expected = targetFramework == "net472"
            ? RunWindowsPowerShellModuleProof(fixture.ScriptPath, command)
            : RunModuleProof(fixture.ScriptPath, command);
        var actual = targetFramework == "net472"
            ? RunWindowsPowerShellModuleProof(result.ArtifactPath!, command)
            : RunModuleProof(result.ArtifactPath!, command);
        Assert.Equal(expected, actual);
        Assert.Contains("rejected:Test-NumericSet", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("accepted-invalid", actual, StringComparison.Ordinal);
    }
}
