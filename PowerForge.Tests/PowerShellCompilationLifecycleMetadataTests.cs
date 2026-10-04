using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("ValidateSet(0,1,5)", false)]
    [InlineData("ValidateSet('2',4)", false)]
    [InlineData("ValidateSet('A','B',IgnoreCase=$false)", false)]
    [InlineData("ValidateScript({ $_ % 2 -eq 0 })", false)]
    [InlineData("ValidateSet('0','1','5')", true)]
    public void Plan_HostedLifecyclePromotesOnlyRepresentableValidationMetadata(string validation, bool expectedPromotion)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Test-Validation {{ [CmdletBinding()] param([{validation}][object]$Value) begin {{ $null = 1 }} process {{ $Value }} }}",
            ".psm1");
        var document = PowerShellSourceParser.ParseFile(fixture.ScriptPath);
        var sources = PowerShellLifecycleSourceBinder.Bind(document, "net10.0");
        var fallback = new PowerShellTypedCompilationResult(
            fixture.ScriptPath, "Generated", "Validation", string.Empty,
            Array.Empty<PowerShellCompiledMethod>(), Array.Empty<PowerShellCompilationDiagnostic>(),
            new[] { fixture.ScriptPath }, sources, optimization: null);

        var planned = PowerShellAdvancedFunctionLifecyclePlanner.AddHostedLifecycleMethods(fallback, "net10.0");

        Assert.Equal(expectedPromotion ? 1 : 0, planned.Methods.Length);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void Build_HybridLifecyclePreservesUnrepresentedValidationMetadata(string targetFramework)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows())
            return;
        using var fixture = ArtifactFixture.Create(
            """
            function Test-NumericSet {
                [CmdletBinding()] param([ValidateSet(0,1,5)][int]$Value, [object[]]$Items)
                begin { $null = 1 } process { $Value; foreach ($item in $Items) { $item.Name } }
            }
            function Test-MixedSet {
                [CmdletBinding()] param([ValidateSet('2',4)][int]$Value, [object[]]$Items)
                begin { $null = 1 } process { $Value; foreach ($item in $Items) { $item.Name } }
            }
            function Test-CaseSensitiveSet {
                [CmdletBinding()] param([ValidateSet('A','B',IgnoreCase=$false)][object]$Value, [object[]]$Items)
                begin { $null = 1 } process { $Value; foreach ($item in $Items) { $item.Name } }
            }
            function Test-ScriptValidation {
                [CmdletBinding()] param([ValidateScript({ $_ % 2 -eq 0 })][int]$Value, [object[]]$Items)
                begin { $null = 1 } process { $Value; foreach ($item in $Items) { $item.Name } }
            }
            Export-ModuleMember -Function Test-NumericSet,Test-MixedSet,Test-CaseSensitiveSet,Test-ScriptValidation
            """,
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.Lifecycle.Metadata",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            UseBuildCache = false
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        const string command = """
            foreach ($name in 'Test-NumericSet','Test-MixedSet','Test-CaseSensitiveSet','Test-ScriptValidation') {
                $command = Get-Command $name
                "$name|$($command.CommandType)|$($command.Parameters.ContainsKey('Value'))"
            }
            $items = @([pscustomobject]@{ Name='fixture' })
            0,1,5 | ForEach-Object { Test-NumericSet -Value $_ -Items $items }
            2,4 | ForEach-Object { Test-MixedSet -Value $_ -Items $items }
            Test-CaseSensitiveSet -Value 'A' -Items $items
            Test-ScriptValidation -Value 2 -Items $items
            foreach ($case in @(
                @{Name='Test-NumericSet';Value=2}, @{Name='Test-MixedSet';Value=3},
                @{Name='Test-CaseSensitiveSet';Value='a'}, @{Name='Test-ScriptValidation';Value=3}
            )) {
                try { & $case.Name -Value $case.Value -Items $items -ErrorAction Stop; 'accepted-invalid' }
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
