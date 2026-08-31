using System.Reflection;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_TypedLibraryPreservesScalarSwitchMultipleMatchBreakAndDefaultSemantics()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-SwitchValue {
                param([string] $Value)
                [int] $result = 0
                switch ($Value) {
                    'One' { $result += 1 }
                    'ONE' { $result += 10; break }
                    default { $result = -1 }
                }
                return $result
            }
            """);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        using var stream = File.OpenRead(result.ArtifactPath!);
        var context = new AssemblyLoadContext("PowerForgeSwitchProof", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var method = assembly.GetType("PowerForge.Compiled.PowerForge_SwitchProofMethods", throwOnError: true)!
                .GetMethod("Get_SwitchValue", BindingFlags.Public | BindingFlags.Static)!;
            Assert.Equal(11, method.Invoke(null, new object[] { "one" }));
            Assert.Equal(-1, method.Invoke(null, new object[] { "missing" }));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Analyze_RejectsWildcardSwitchAsRuntimeMatching()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SwitchValue { param([string] $Value) switch -Wildcard ($Value) { 'A*' { return 1 } default { return 0 } } }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("switch flags", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_TypedLibraryPreservesScalarRegexSwitchMatchingAndCaseSensitivity()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RegexSwitchValue { param([string] $Value) [int] $result = 0; switch -Regex ($Value) { '^forty' { $result += 2 } 'TWO$' { $result += 40 } default { $result = -1 } }; return $result } " +
            "function Get-CaseSensitiveRegexSwitchValue { param([string] $Value) switch -Regex -CaseSensitive ($Value) { '^forty' { return 42 } default { return -1 } } }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RegexSwitchProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_RegexSwitchProofMethods", throwOnError: true)!;
        Assert.Equal(42, type.GetMethod("Get_RegexSwitchValue")!.Invoke(null, new object[] { "Forty-Two" }));
        Assert.Equal(-1, type.GetMethod("Get_RegexSwitchValue")!.Invoke(null, new object[] { "missing" }));
        Assert.Equal(42, type.GetMethod("Get_CaseSensitiveRegexSwitchValue")!.Invoke(null, new object[] { "forty-two" }));
        Assert.Equal(-1, type.GetMethod("Get_CaseSensitiveRegexSwitchValue")!.Invoke(null, new object[] { "Forty-Two" }));
    }

    [Fact]
    public void Analyze_AcceptsBoundedScalarRegexSwitchThroughCanonicalSemanticBinder()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RegexSwitchValue { param([string] $Value) switch -Regex ($Value) { '^forty' { return 42 } default { return -1 } } }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Empty(unit.Diagnostics);
    }

    [Fact]
    public void Analyze_RejectsRegexSwitchWithNonStringCondition()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RegexSwitchValue { param([int] $Value) switch -Regex ($Value) { '^4' { return 1 } default { return 0 } } }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("regex switch requires a String condition", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RejectsRegexSwitchWhenMatchesAutomaticVariableIsObserved()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RegexCapture { $Matches = @{'1' = 'old'}; switch -Regex ('forty-two') { '^(forty)' { $matched = $true } }; return $Matches.1 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("$Matches", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RejectsDynamicAndMalformedRegexSwitchPatterns()
    {
        using var dynamicFixture = ArtifactFixture.Create(
            "function Get-DynamicRegexSwitch { param([string] $Pattern) switch -Regex ('value') { $Pattern { return 1 } default { return 0 } } }");
        using var malformedFixture = ArtifactFixture.Create(
            "function Get-MalformedRegexSwitch { switch -Regex ('value') { '[' { return 1 } default { return 0 } } }");

        var dynamicUnit = Assert.Single(Assert.Single(
            new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(dynamicFixture.ScriptPath)).Files).Units);
        var malformedUnit = Assert.Single(Assert.Single(
            new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(malformedFixture.ScriptPath)).Files).Units);

        Assert.False(dynamicUnit.IsCompilable);
        Assert.Contains(dynamicUnit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time literal String patterns", StringComparison.Ordinal));
        Assert.False(malformedUnit.IsCompilable);
        Assert.Contains(malformedUnit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("not a valid regular expression", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("_")]
    [InlineData("PSItem")]
    [InlineData("switch")]
    public void Analyze_RejectsScalarSwitchWhenCurrentItemAutomaticStateIsObserved(string variableName)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-SwitchItem {{ [string] $seen = ''; switch ('new') {{ 'new' {{ $seen = [string] ${variableName} }} }}; return $seen }}");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("automatic-variable state", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_TypedLibraryUsesPowerShellCultureSemanticsForUnicodeSwitch()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-UnicodeSwitch { param([string] $Value) [int] $result = 0; switch ($Value) { 'e\u0301' { $result = 1 } }; return $result }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.UnicodeSwitch",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assembly.LoadFile(result.ArtifactPath!);
        var method = assembly.GetType("PowerForge.Compiled.PowerForge_UnicodeSwitchMethods", throwOnError: true)!
            .GetMethod("Get_UnicodeSwitch")!;
        Assert.Equal(1, method.Invoke(null, new object[] { "\u00e9" }));
    }

    [Fact]
    public void Build_TypedLibraryAcceptsExhaustiveReturningSwitch()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SwitchValue { param([int] $Value) switch ($Value) { 1 { return 10 } 2 { return 20 } default { return -1 } } }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ReturningSwitch",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assembly.LoadFile(result.ArtifactPath!);
        var method = assembly.GetType("PowerForge.Compiled.PowerForge_ReturningSwitchMethods", throwOnError: true)!
            .GetMethod("Get_SwitchValue")!;
        Assert.Equal(20, method.Invoke(null, new object[] { 2 }));
        Assert.Equal(-1, method.Invoke(null, new object[] { 9 }));
    }

    [Fact]
    public void Build_ScalarSwitchContinuePreservesPowerShellSwitchScopeInsideAndOutsideLoops()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TopLevelContinue { [int] $Value = 0; switch (1) { 1 { $Value = 4; continue; $Value = 9 } }; return $Value } " +
            "function Get-NestedContinue { [int] $Sum = 0; for ([int] $Index = 0; $Index -lt 3; $Index++) { switch ($Index) { 1 { continue } }; $Sum += 10; $Sum += $Index }; return $Sum }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));
        var diagnostics = plan.Files.SelectMany(static file => file.Units).SelectMany(static unit => unit.Diagnostics).ToArray();
        Assert.True(plan.CompilableUnits == 2, string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Empty(diagnostics);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchContinueProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_SwitchContinueProofMethods", throwOnError: true)!;
        Assert.Equal(4, type.GetMethod("Get_TopLevelContinue")!.Invoke(null, null));
        Assert.Equal(33, type.GetMethod("Get_NestedContinue")!.Invoke(null, null));
    }
}
