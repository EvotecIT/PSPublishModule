using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_HybridBinaryModuleScopesCommandRegionsPerRunspace()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:Value = 'initial'; function Get-FrontierRunspaceValue { " +
            "Get-Variable -Name Value -Scope Script -ValueOnly }; " +
            "Export-ModuleMember -Function Get-FrontierRunspaceValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RunspaceRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var firstState = InitialSessionState.CreateDefault();
        var secondState = InitialSessionState.CreateDefault();
        if (OperatingSystem.IsWindows())
        {
            firstState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
            secondState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
        }
        using var first = RunspaceFactory.CreateRunspace(firstState);
        using var second = RunspaceFactory.CreateRunspace(secondState);
        first.Open();
        second.Open();
        var quotedPath = result.ArtifactPath!.Replace("'", "''");
        Assert.Equal("one", InvokeInRunspace(first, $"Import-Module -Name '{quotedPath}'; & (Get-Module PowerForge.RunspaceRegion) {{ $script:Value = 'one' }}; Get-FrontierRunspaceValue"));
        Assert.Equal("two", InvokeInRunspace(second, $"Import-Module -Name '{quotedPath}'; & (Get-Module PowerForge.RunspaceRegion) {{ $script:Value = 'two' }}; Get-FrontierRunspaceValue"));
        Assert.Equal("one", InvokeInRunspace(first, "Get-FrontierRunspaceValue"));
        Assert.Equal(string.Empty, InvokeInRunspace(second, "Remove-Module PowerForge.RunspaceRegion"));
        Assert.Equal("one", InvokeInRunspace(first, "Get-FrontierRunspaceValue"));
    }

    [Fact]
    public void Build_StrictExecutableRejectsSideEffectingIndexExpression()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int[]] $Values); return $Values[[System.Random]::Shared.Next(0, $Values.Length)]");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.IndexEvaluationBoundary",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("side-effect-free", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableRejectsAmbiguousParameterAliases()
    {
        using var fixture = ArtifactFixture.Create(
            "param([Alias('x')][int] $One, [Alias('x')][int] $Two); return $One + $Two");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AmbiguousAliases",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableSkipsOptionalValidationWhenLocalArgumentIsOmitted()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierValidated { param([ValidateSet('A','B')][string] $Mode) " +
            "if ($PSBoundParameters.ContainsKey('Mode')) { return $Mode }; return 'default' }; " +
            "return Get-FrontierValidated");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.OptionalValidation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!);
        Assert.Equal((0, "default", string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryModuleKeepsPreDeclarationInvocationTimingOnFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:Before = Get-FrontierLate; function Get-FrontierLate { return 42 }; " +
            "Export-ModuleMember -Function Get-FrontierLate",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DeclarationTiming",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("availability timing", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("PowerForge.Value.")]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("COM1")]
    public void Build_RejectsWindowsNormalizedOrDeviceArtifactNames(string artifactName)
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");

        var exception = Assert.Throws<ArgumentException>(() =>
            new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                artifactName,
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)));

        Assert.Contains("Windows", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictBinaryModuleUsesCurrentCultureForValidatePattern()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-FrontierTurkishPattern { [CmdletBinding()] param([ValidatePattern('^i$')][string] $Value) return $Value }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CulturePattern",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "$previous = [System.Globalization.CultureInfo]::CurrentCulture; try { " +
            "[System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'); " +
            "$null = Test-FrontierTurkishPattern -Value ([string][char]0x130); 'accepted' } finally { " +
            "[System.Globalization.CultureInfo]::CurrentCulture = $previous }");
        Assert.Equal("accepted", output);
    }

    [Fact]
    public void Build_StrictBinaryModuleBracesCapturedCommandRegionVariableNames()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierBracedRegion { [CmdletBinding()] param(); ${a-b} = 1; Write-Output ${a-b}; return 2 }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BracedRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "1", "2" }, RunModuleProof(result.ArtifactPath!, "Get-FrontierBracedRegion").Split(Environment.NewLine));
        Assert.Contains("param(${a-b})", File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictExecutableRejectsTypeChangeInInferredLocal()
    {
        using var fixture = ArtifactFixture.Create("$value = 1L; $value = 2; return $value");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InferredTypeBoundary",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("inferred local", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    private static string InvokeInRunspace(Runspace runspace, string script)
    {
        using var powerShell = PowerShell.Create(runspace);
        var output = powerShell.AddScript(script).Invoke();
        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error));
        return string.Join(Environment.NewLine, output.Select(static value => value?.ToString() ?? string.Empty));
    }
}
