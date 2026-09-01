using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Analyze_WidensRightNumericOperandToLeftComparisonType()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Length { param([long] $Length) return $Length -gt 0 }; " +
            "function Test-Position { param([System.IO.Stream] $Stream) return $Stream.Position -eq 0 }",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.Equal(2, Assert.Single(plan.Files).Units.Length);
        Assert.All(
            plan.Files[0].Units,
            unit => Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message))));
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModuleMatchesPowerShellForWideningNumericComparisons(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create(
            "function Test-WidenedComparison { param([long] $Left, [int] $Right) " +
            "return [string] ($Left -gt $Right) + '|' + [string] ($Left -eq $Right) }; " +
            "function Test-WidenedNullableComparison { param([System.IO.Stream] $Stream) " +
            "return [string] ($Stream.Position -eq 0) + '|' + [string] ($Stream.Position -ne 0) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.WidenedNumericComparison",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string invocation =
            "Test-WidenedComparison -Left 5 -Right 0; Test-WidenedComparison -Left 5 -Right 5; " +
            "Test-WidenedNullableComparison -Stream $null; " +
            "$stream = [IO.MemoryStream]::new([byte[]] @(1,2)); $stream.Position = 1; Test-WidenedNullableComparison -Stream $stream; $stream.Dispose()";
        var original = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {invocation}");
        var compiled = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {invocation}");

        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Analyze_StillRejectsNarrowingNumericComparison()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Narrowing { param([int] $Left, [long] $Right) return $Left -gt $Right }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("same static CLR type", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RejectsPrecisionChangingNumericPromotion()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Precision { param([single] $Left, [int] $Right) return $Left -eq $Right }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("same static CLR type", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RejectsNullableFloatingEquality()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-NullableFloatingEquality { param([Nullable[single]] $Left, [Nullable[single]] $Right) return $Left -eq $Right }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("no supported static CLR equality operator", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("lt")]
    [InlineData("le")]
    [InlineData("gt")]
    [InlineData("ge")]
    public void Analyze_RejectsNullableRelationalComparison(string operation)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Test-NullableRelation {{ param([System.IO.Stream] $Stream) return $Stream.Position -{operation} 0 }}",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("sign-sensitive null ordering", StringComparison.Ordinal));
    }

    [Fact]
    public void FeatureClassifierDoesNotTreatClrMemberNamesContainingValidationAsParameterMetadata()
    {
        var featureId = PowerShellCompilationFeatureIds.Resolve(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            "CLR member 'System.Net.Http.HttpClientHandler.ServerCertificateCustomValidationCallback' was not found as one target-compatible readable and writable member.",
            explicitFeatureId: null);

        Assert.Equal(PowerShellCompilationFeatureIds.ForSyntax("MemberExpressionAst"), featureId);

        Assert.Equal(
            PowerShellCompilationFeatureIds.AssignmentTarget,
            PowerShellCompilationFeatureIds.Resolve(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                "Typed CLR member mutation requires one direct assignment target.",
                explicitFeatureId: null));
        Assert.Equal(
            PowerShellCompilationFeatureIds.AssignmentTarget,
            PowerShellCompilationFeatureIds.Resolve(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                "CLR member mutation on a potentially null receiver requires runtime semantics.",
                explicitFeatureId: null));
    }
}
