using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Analyze_PreservesTargetTypedLiteralDefaults()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LiteralDefaults { param([int] $Count = 7, [string] $Name = $null, " +
            "[int[]] $Values = @(2, 3), [DayOfWeek] $Day = 'Friday') return $Count }",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Collection(
            unit.Parameters,
            parameter =>
            {
                Assert.Equal(PowerShellCompilationLiteralKind.SignedInteger, parameter.DefaultValue?.Kind);
                Assert.Equal("7", parameter.DefaultValue?.Value);
            },
            parameter =>
            {
                Assert.Equal(PowerShellCompilationLiteralKind.String, parameter.DefaultValue?.Kind);
                Assert.Equal(string.Empty, parameter.DefaultValue?.Value);
            },
            parameter =>
            {
                Assert.Equal(PowerShellCompilationLiteralKind.Array, parameter.DefaultValue?.Kind);
                Assert.Equal(new[] { "2", "3" }, parameter.DefaultValue?.Elements.Select(static element => element.Value));
            },
            parameter =>
            {
                Assert.Equal(PowerShellCompilationLiteralKind.Enum, parameter.DefaultValue?.Kind);
                Assert.Equal(((int)DayOfWeek.Friday).ToString(), parameter.DefaultValue?.Value);
            });
        Assert.DoesNotContain(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterDefault);
    }

    [Fact]
    public void Analyze_LeavesRuntimeEvaluatedDefaultOnFallbackPath()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DynamicDefault { param([DateTime] $When = (Get-Date)) return $When }",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Null(Assert.Single(unit.Parameters).DefaultValue);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterDefault);
    }

    [Fact]
    public void Build_StrictExecutableDistinguishesOmittedDefaultFromExplicitZero()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Count = 7); return $Count");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LiteralDefaultExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var omitted = RunProcess(result.ArtifactPath!);
        var bound = RunProcess(result.ArtifactPath!, "--Count=0");
        Assert.Equal((0, "7"), (omitted.ExitCode, omitted.StandardOutput.Trim()));
        Assert.Equal((0, "0"), (bound.ExitCode, bound.StandardOutput.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesScalarAndArrayDefaultsWithoutOverwritingExplicitValues()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-DefaultName { param([string] $Name = 'fallback') return $Name }; " +
            "function Get-DefaultTotal { param([int[]] $Numbers = @(2, 3)) [int] $total = 0; " +
            "foreach ($number in $Numbers) { $total += $number }; return $total }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LiteralDefaultModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "'<'+(Get-DefaultName)+'>'; '<'+(Get-DefaultName -Name '')+'>'; " +
            "Get-DefaultTotal; Get-DefaultTotal -Numbers @()");
        Assert.Equal(new[] { "<fallback>", "<>", "5", "0" }, output.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictBinaryModulePropagatesBoundStateThroughTypedLocalDefaultCall()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-InnerDefault { param([int] $Number = 7) return $Number }; " +
            "function Get-OuterDefault { return Get-InnerDefault }; " +
            "function Get-ExplicitDefault { return Get-InnerDefault -Number 0 }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LocalLiteralDefault",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(result.ArtifactPath!, "Get-OuterDefault; Get-ExplicitDefault");
        Assert.Equal(new[] { "7", "0" }, output.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictBinaryModuleEmitsPortableScalarLiteralFamilies()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TokenDefault { param([Guid] $Token = '2f7d93d8-8723-4ced-8f5f-86b155df3a10') return $Token }; " +
            "function Get-MomentDefault { param([DateTime] $Moment = '2020-01-02T03:04:05.0000000') return $Moment }; " +
            "function Get-RecordedDefault { param([DateTimeOffset] $Recorded = '2020-01-02T03:04:05.0000000+02:00') return $Recorded }; " +
            "function Get-SpanDefault { param([TimeSpan] $Span = '01:02:03') return $Span }; " +
            "function Get-LinkDefault { param([Uri] $Link = 'relative/path') return $Link }; " +
            "function Get-BuildDefault { param([Version] $Build = '1.2.3.4') return $Build }; " +
            "function Get-NameDefault { param([DayOfWeek] $Name = 'Friday') return $Name }; " +
            "function Get-AmountDefault { param([decimal] $Amount = 1.25) return $Amount }; " +
            "function Get-LetterDefault { param([char] $Letter = 'x') return $Letter }; " +
            "function Get-NullableDefault { param([Nullable[int]] $Number = 7) return $Number }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PortableLiteralFamilies",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "(Get-TokenDefault).ToString(); (Get-MomentDefault).ToString('O'); " +
            "(Get-RecordedDefault).ToString('O'); (Get-SpanDefault).ToString('c'); " +
            "(Get-LinkDefault).OriginalString; (Get-BuildDefault).ToString(); " +
            "(Get-NameDefault).ToString(); (Get-AmountDefault).ToString([Globalization.CultureInfo]::InvariantCulture); " +
            "(Get-LetterDefault).ToString(); (Get-NullableDefault).ToString()");
        Assert.Equal(
            new[]
            {
                "2f7d93d8-8723-4ced-8f5f-86b155df3a10",
                "2020-01-02T03:04:05.0000000",
                "2020-01-02T03:04:05.0000000+02:00",
                "01:02:03",
                "relative/path",
                "1.2.3.4",
                "Friday",
                "1.25",
                "x",
                "7"
            },
            output.Split(Environment.NewLine));
    }
}
