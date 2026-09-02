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
    public void Analyze_AcceptsNullableIntegralRelationalComparison(string operation)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Test-NullableRelation {{ param([System.IO.Stream] $Stream) return $Stream.Position -{operation} 0 }}",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModuleMatchesPowerShellForNullableNumericOrdering(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create(
            "function Test-NullableIntegralOrdering { param([Nullable[long]] $Left, [Nullable[long]] $Right) " +
            "return [string] ($Left -lt $Right) + '|' + [string] ($Left -le $Right) + '|' + [string] ($Left -gt $Right) + '|' + [string] ($Left -ge $Right) }; " +
            "function Test-NullableDecimalOrdering { param([Nullable[decimal]] $Left, [Nullable[decimal]] $Right) " +
            "return [string] ($Left -lt $Right) + '|' + [string] ($Left -le $Right) + '|' + [string] ($Left -gt $Right) + '|' + [string] ($Left -ge $Right) }; " +
            "function Test-NullableUnsignedOrdering { param([Nullable[uint64]] $Left, [Nullable[uint64]] $Right) " +
            "return [string] ($Left -lt $Right) + '|' + [string] ($Left -le $Right) + '|' + [string] ($Left -gt $Right) + '|' + [string] ($Left -ge $Right) }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableNumericOrdering",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string invocation =
            "Test-NullableIntegralOrdering; " +
            "Test-NullableIntegralOrdering -Right ([long] -1); " +
            "Test-NullableIntegralOrdering -Right ([long] 0); " +
            "Test-NullableIntegralOrdering -Left ([long] -1); " +
            "Test-NullableIntegralOrdering -Left ([long] 0); " +
            "Test-NullableIntegralOrdering -Left ([long] -1) -Right ([long] 0); " +
            "Test-NullableDecimalOrdering -Right ([decimal] -1); " +
            "Test-NullableDecimalOrdering -Left ([decimal] 0); " +
            "Test-NullableUnsignedOrdering -Right ([uint64] 0); " +
            "Test-NullableUnsignedOrdering -Left ([uint64] 0)";
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
        Assert.Equal(
            new[]
            {
                "False|True|False|True",
                "False|False|True|True",
                "True|True|False|False",
                "True|True|False|False",
                "False|False|True|True",
                "True|True|False|False",
                "False|False|True|True",
                "False|False|True|True",
                "True|True|False|False",
                "False|False|True|True"
            },
            compiled.StandardOutput.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Build_NullOrderedMemberOperandIsEvaluatedOnceInGeneratedSource()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Position { param([System.IO.Stream] $Stream) return $Stream.Position -lt 0 }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableMemberOrdering",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Equal(1, generated.Split(".Position", StringSplitOptions.None).Length - 1);
        Assert.Contains("pf_null_order_left", generated, StringComparison.Ordinal);
        Assert.Contains("pf_null_order_right", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictExecutableRunsNullableRelationalOrderingOracle()
    {
        const string caseId = "PowerForge.Semantic/nullable-relational-ordering";
        using var fixture = ArtifactFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(caseId));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableRelationalOrderingOracle",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var observed = Run(result.ArtifactPath!, Array.Empty<string>());
        Assert.Equal(0, observed.ExitCode);
        Assert.Equal("True", observed.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(observed.StandardError), observed.StandardError);
    }

    [Theory]
    [InlineData("double", "double")]
    [InlineData("long", "Nullable[int]")]
    public void Analyze_RejectsUnprovenNullableRelationalOrdering(string leftType, string rightType)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Test-NullableRelation {{ param([Nullable[{leftType}]] $Left, [{rightType}] $Right) return $Left -lt $Right }}",
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
