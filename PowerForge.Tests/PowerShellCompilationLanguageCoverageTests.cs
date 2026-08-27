using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_StrictExecutableLowersConstantConversionWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create("return [int] '12'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConstantConversion",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        var run = Run(result.ArtifactPath!, Array.Empty<string>());
        Assert.Equal((0, "12", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
    }

    [Fact]
    public void Analyze_RuntimeIndependentTargetKeepsDynamicConversionOnFallbackPath()
    {
        using var fixture = ArtifactFixture.Create("function Convert-NumberText { param([string] $Text) return [int] $Text }");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.Conversion);
    }

    [Fact]
    public void Build_StrictBinaryModuleCompilesOutputTypedSelfRecursiveGraph()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Countdown { [OutputType([long])] param([long] $Number) " +
            "if ($Number -le [long] 0) { return $Number }; $Number -= [long] 1; return Get-Countdown -Number $Number }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedRecursion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Get-Countdown -Number 5");
        Assert.Equal((0, "0", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictExecutableCompilesOutputTypedSelfRecursiveGraph()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Countdown { [OutputType([long])] param([long] $Number) " +
            "if ($Number -le [long] 0) { return $Number }; $Number -= [long] 1; return Get-Countdown -Number $Number }; " +
            "return Get-Countdown -Number ([long] 5)");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedRecursionExecutable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        var run = Run(result.ArtifactPath!, Array.Empty<string>());
        Assert.Equal((0, "0", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
    }

    [Fact]
    public void Transpile_OutputTypesDoNotPermitMutuallyRecursiveGraph()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Even { [OutputType([bool])] param([long] $Number) if ($Number -le [long] 0) { return $true }; $Number -= [long] 1; return Get-Odd -Number $Number }; " +
            "function Get-Odd { [OutputType([bool])] param([long] $Number) if ($Number -le [long] 0) { return $false }; $Number -= [long] 1; return Get-Even -Number $Number }",
            ".psm1");

        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.MutualRecursion",
            "CompiledPowerShell",
            "net10.0");

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("recursive local-call cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModuleMatchesPowerShellLanguageOperatorsAndConversions(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create(
            "function Test-TypeOperator { param([object] $Subject) return $Subject -is [string] }; " +
            "function Test-LikeOperator { param([string] $Text, [string] $Rule) return $Text -like $Rule }; " +
            "function Test-CaseLikeOperator { param([string] $Text, [string] $Rule) return $Text -clike $Rule }; " +
            "function Test-NotLikeOperator { param([string] $Text, [string] $Rule) return $Text -notlike $Rule }; " +
            "function Test-MatchOperator { param([string] $Text, [string] $Rule) return $Text -cmatch $Rule }; " +
            "function Test-NotMatchOperator { param([string] $Text, [string] $Rule) return $Text -notmatch $Rule }; " +
            "function Edit-TextOperator { param([string] $Text, [string] $Rule, [string] $Substitute) return $Text -replace $Rule, $Substitute }; " +
            "function Split-TextOperator { param([string] $Text, [string] $Rule) return $Text -split $Rule }; " +
            "function Join-TextOperator { param([string[]] $Names, [string] $Separator) return $Names -join $Separator }; " +
            "function Test-ContainsOperator { param([string[]] $Names, [string] $Target) return $Names -contains $Target }; " +
            "function Test-NotContainsOperator { param([string[]] $Names, [string] $Target) return $Names -cnotcontains $Target }; " +
            "function Test-InOperator { param([string] $Name, [string[]] $Set) return $Name -cin $Set }; " +
            "function Test-NotInOperator { param([string] $Name, [string[]] $Set) return $Name -notin $Set }; " +
            "function Get-AndOperator { param([int] $Left, [int] $Right) return $Left -band $Right }; " +
            "function Get-OrOperator { param([int] $Left, [int] $Right) return $Left -bor $Right }; " +
            "function Get-BitwiseOperator { param([int] $Left, [int] $Right) return $Left -bxor $Right }; " +
            "function Get-ShiftOperator { param([long] $Number, [int] $Bits) return $Number -shr $Bits }; " +
            "function Get-ShiftLeftOperator { param([long] $Number, [int] $Bits) return $Number -shl $Bits }; " +
            "function Get-NegatedOperator { param([byte] $Number) return -bnot $Number }; " +
            "function Convert-NumberText { param([string] $Text) return [int] $Text }; " +
            "function Convert-BooleanText { param([string] $Text) return [bool] $Text }; " +
            "function Convert-IdentifierText { param([string] $Text) return [Guid] $Text }",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.LanguageCoverage",
            "CompiledPowerShell",
            targetFramework);
        Assert.Empty(typed.Diagnostics);
        Assert.Equal(22, typed.Methods.Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LanguageCoverage",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        const string calls =
            "Test-TypeOperator -Subject 'text'; " +
            "Test-LikeOperator -Text 'Alpha' -Rule 'a*'; " +
            "Test-CaseLikeOperator -Text 'Alpha' -Rule 'a*'; Test-NotLikeOperator -Text 'Alpha' -Rule 'z*'; " +
            "Test-MatchOperator -Text 'Alpha' -Rule '^A'; " +
            "Test-NotMatchOperator -Text 'Alpha' -Rule '^z'; " +
            "Edit-TextOperator -Text 'Alpha' -Rule 'a' -Substitute 'x'; " +
            "(Split-TextOperator -Text 'AlphaBeta' -Rule 'a') -join '|'; " +
            "Join-TextOperator -Names @('Alpha','Beta') -Separator '|'; " +
            "Test-ContainsOperator -Names @('Alpha','Beta') -Target 'alpha'; " +
            "Test-NotContainsOperator -Names @('Alpha','Beta') -Target 'alpha'; " +
            "Test-InOperator -Name 'Alpha' -Set @('alpha','beta'); " +
            "Test-NotInOperator -Name 'Alpha' -Set @('beta','gamma'); " +
            "Get-AndOperator -Left 5 -Right 3; Get-OrOperator -Left 5 -Right 2; Get-BitwiseOperator -Left 5 -Right 3; " +
            "Get-ShiftOperator -Number 8 -Bits 2; Get-ShiftLeftOperator -Number 2 -Bits 3; " +
            "Get-NegatedOperator -Number 5; Convert-NumberText -Text '12'; Convert-BooleanText -Text 'false'; " +
            "(Convert-IdentifierText -Text '2f7d93d8-8723-4ced-8f5f-86b155df3a10').ToString()";
        var original = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Build_LanguageOperatorsEvaluateOperandsOnceInSourceOrder()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LeftText { param([Text.StringBuilder] $Trace, [string] $Value) $null = $Trace.Append('L'); return $Value }; " +
            "function Get-RightText { param([Text.StringBuilder] $Trace, [string] $Value) $null = $Trace.Append('R'); return $Value }; " +
            "function Test-OperatorOrder { param([Text.StringBuilder] $Trace, [string[]] $Collection, [string[]] $Names) " +
            "$like = (Get-LeftText -Trace $Trace -Value 'Alpha') -like (Get-RightText -Trace $Trace -Value 'A*'); " +
            "$contains = $Collection -contains (Get-RightText -Trace $Trace -Value 'x'); " +
            "$joined = $Names -join (Get-RightText -Trace $Trace -Value ','); " +
            "$nullable = (Get-RightText -Trace $Trace -Value '1') -is [Nullable[int]]; " +
            "return ([string] $like + '|' + [string] $contains + '|' + $joined + '|' + [string] $nullable + '|' + $Trace.ToString()) }; " +
            "Export-ModuleMember -Function Test-OperatorOrder",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.OperatorOrder", "CompiledPowerShell", "net8.0");
        Assert.True(typed.Diagnostics.Length == 0,
            string.Join(Environment.NewLine, typed.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(
            typed, new[] { "Test-OperatorOrder" }, "net8.0");
        Assert.True(prepared.Diagnostics.Length == 0,
            string.Join(Environment.NewLine, prepared.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.OperatorOrder",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net8.0",
            EmitSource = true
        });

        Assert.True(
            result.Succeeded,
            result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            string.Join(Environment.NewLine, result.Manifest?.Diagnostics.Select(static diagnostic => diagnostic.Message) ?? Array.Empty<string>()));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.True(generated.IndexOf("__pf_wildcard_left", StringComparison.Ordinal) < generated.IndexOf("__pf_wildcard_right", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("__pf_membership_left", StringComparison.Ordinal) < generated.IndexOf("__pf_membership_right", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("__pf_join_left", StringComparison.Ordinal) < generated.IndexOf("__pf_join_right", StringComparison.Ordinal));
        const string calls = "$trace = [Text.StringBuilder]::new(); Test-OperatorOrder -Trace $trace -Collection @() -Names @('a','b')";
        var original = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");
        var compiled = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {calls}");

        Assert.Equal(0, original.ExitCode);
        Assert.Equal((original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
        Assert.Equal("True|False|a,b|False|LRRRR", compiled.StandardOutput.Trim());
    }
}
