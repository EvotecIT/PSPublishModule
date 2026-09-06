using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string FunctionHeaderSource = """
        function Get-HeaderValue([Parameter()][Alias('n')][int]$Value = 4, [switch]$Double) {
            if ($Double) { $Value += $Value }
            return $Value
        }
        function Test-HeaderPresence([Parameter()][int]$Value = 4) {
            return $PSBoundParameters.ContainsKey('Value')
        }
        """;

    [Fact]
    public void Transpile_FunctionHeaderPreservesParameterTypesMetadataAndPresence()
    {
        using var fixture = ArtifactFixture.Create(FunctionHeaderSource, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "HeaderMethods", "net10.0");

        Assert.Empty(typed.Diagnostics);
        Assert.Equal(2, typed.Methods.Length);
        var value = Assert.Single(typed.Methods, method => method.SourceName == "Get-HeaderValue");
        Assert.True(value.IsAdvancedFunction);
        Assert.Equal(new[] { "Value", "Double" }, value.Parameters.Select(parameter => parameter.Name));
        Assert.Equal("System.Int32", value.Parameters[0].TypeName);
        Assert.Contains("n", value.Parameters[0].Aliases);
        Assert.True(value.Parameters[0].HasDefaultValue);
        Assert.True(value.Parameters[1].IsSwitch);
        Assert.True(Assert.Single(typed.Methods, method => method.SourceName == "Test-HeaderPresence").RequiresPowerShellBoundParameters);
    }

    [Fact]
    public void Build_FunctionHeaderBinaryModuleMatchesPowerShell7Binding()
        => VerifyFunctionHeaderModule("net10.0", "pwsh");

    [WindowsFact]
    public void Build_FunctionHeaderBinaryModuleMatchesWindowsPowerShell51Binding()
        => VerifyFunctionHeaderModule("net472", "powershell.exe");

    [Fact]
    public void Build_FunctionHeaderLocalCallsPreserveAliasesAndDefaultsInStrictExecutable()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HeaderNumber([Alias('n')][int]$Number = 7) { return $Number }; " +
            "[int]$First = Get-HeaderNumber; [int]$Second = Get-HeaderNumber -n 11; return [int[]]@($First, $Second)");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.FunctionHeaderExecutable",
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = RunProcess(result.ArtifactPath!);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "7", "11" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(run.StandardError);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
    }

    [Fact]
    public void Build_FunctionHeaderHostedLifecyclePreservesPipelineBinding()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-HeaderTotal([Parameter(ValueFromPipeline)][int]$Number) {
                begin { [int]$Total = 0 }
                process { $Total += $Number }
                end { return $Total }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.FunctionHeaderLifecycle",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var original = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; 1,2,3 | Get-HeaderTotal");
        var compiled = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; 1,2,3 | Get-HeaderTotal");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal("6", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [InlineData("[Parameter()][Alias('vb')][int]$Number")]
    [InlineData("[Parameter()][int]$Verbose")]
    public void Transpile_FunctionHeaderRejectsAdvancedCommonParameterCollisions(string parameters)
    {
        using var fixture = ArtifactFixture.Create("function Get-HeaderInvalid(" + parameters + ") { return 1 }");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "HeaderMethods", "net10.0");
        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("common parameter", StringComparison.OrdinalIgnoreCase));
    }

    private static void VerifyFunctionHeaderModule(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(FunctionHeaderSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.FunctionHeader" + (framework == "net472" ? "Desktop" : "Core"),
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = "@((Get-HeaderValue), (Get-HeaderValue -n 6 -Double), (Test-HeaderPresence), (Test-HeaderPresence -Value 4)) -join '|'";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal("4|12|False|True", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }
}
