using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictMultiFileExecutableCompilesLocalFunctionCallsWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int] $Count); . \"$PSScriptRoot/Private/Get-Result.ps1\"; [long] $total = 0; " +
            "for ([int] $i = 0; $i -lt $Count; $i++) { $total = Get-Result -Val $total }; return $total");
        var privateDirectory = Path.Combine(fixture.RootPath, "Private");
        Directory.CreateDirectory(privateDirectory);
        var dependencyPath = Path.Combine(privateDirectory, "Get-Result.ps1");
        File.WriteAllText(
            dependencyPath,
            "function Add-One { param([long] $Value) [long] $result = $Value; $result += 1; return $result }; " +
            "function Get-Result { param([Alias('ValueAlias')] [long] $Value) return Add-One -Value $Value }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            new[] { fixture.ScriptPath, dependencyPath },
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            fixture.ScriptPath);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            fixture.OutputPath,
            "PowerForge.TypedLocalCallProof",
            resolved.Kind,
            resolved.Mode)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(3, result.Manifest.CompiledMethods);
        Assert.Equal(100d, result.Manifest.CompilationCoveragePercentage);
        var process = RunProcess(result.ArtifactPath!, "--Count=42");
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("42", process.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(process.StandardError), process.StandardError);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellScript.cs"));
        Assert.Contains("Get_Result(", generated, StringComparison.Ordinal);
        Assert.Contains("Add_One(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Management.Automation", generated, StringComparison.Ordinal);
        using var sourceMap = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "source-map.json")));
        var mappedMethods = sourceMap.RootElement.GetProperty("methods").EnumerateArray().ToArray();
        Assert.Equal(3, mappedMethods.Length);
        Assert.Contains(mappedMethods, method => method.GetProperty("powershellName").GetString() == "<script>");
        Assert.Contains(mappedMethods, method => method.GetProperty("powershellName").GetString() == "Get-Result" &&
            method.GetProperty("sourceFile").GetString() == "Private/Get-Result.ps1");
        Assert.Contains(mappedMethods, method => method.GetProperty("powershellName").GetString() == "Add-One" &&
            method.GetProperty("sourceFile").GetString() == "Private/Get-Result.ps1");
    }

    [Fact]
    public void Build_StrictTypedExecutableRejectsBuilderLevelUnreachableSourceInjection()
    {
        using var fixture = ArtifactFixture.Create("return Get-Injected");
        var injectedPath = Path.Combine(fixture.RootPath, "Injected.ps1");
        File.WriteAllText(injectedPath, "function Get-Injected { return 42 }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedInjection",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, injectedPath }
        });

        Assert.False(result.Succeeded);
        Assert.Contains("unreachable source", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictTypedExecutableAccountsForNestedDependencyLoaderDirectives()
    {
        using var fixture = ArtifactFixture.Create(". \"$PSScriptRoot/First.ps1\"; return Get-First");
        var firstPath = Path.Combine(fixture.RootPath, "First.ps1");
        var secondPath = Path.Combine(fixture.RootPath, "Second.ps1");
        File.WriteAllText(firstPath, ". \"$PSScriptRoot/Second.ps1\"; function Get-First { return Get-Second }");
        File.WriteAllText(secondPath, "function Get-Second { return 42 }");
        var closure = PowerShellHybridDependencyResolver.DiscoverDependencies(fixture.ScriptPath);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedNestedClosure",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            CompilationSourcePaths = closure
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.Equal(100d, result.Manifest.CompilationCoveragePercentage);
        var process = RunProcess(result.ArtifactPath!);
        Assert.Equal((0, "42", string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictTypedExecutableRejectsRecursiveLocalFunctionCycle()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-First { return Invoke-Second }; function Invoke-Second { return Invoke-First }; return Invoke-First");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedLocalCycle",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("cycle", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictTypedExecutableRejectsUnenforcedLocalFunctionValidationMetadata()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { param([ValidateRange(1, 5)] [int] $Value) return $Value }; return Get-Value -Value 3");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedLocalValidation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("validation metadata", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictTypedExecutableCompilesCatchAllTryStatement()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Text); try { return [int]::Parse($Text) } catch { return -1 } finally { [GC]::KeepAlive($Text) }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedTryCatch",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var valid = RunProcess(result.ArtifactPath!, "--Text=42");
        var invalid = RunProcess(result.ArtifactPath!, "--Text=nope");
        Assert.Equal((0, "42", string.Empty), (valid.ExitCode, valid.StandardOutput.Trim(), valid.StandardError.Trim()));
        Assert.Equal((0, "-1", string.Empty), (invalid.ExitCode, invalid.StandardOutput.Trim(), invalid.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictTypedExecutablePreservesOrderedStringDictionaryLookup()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Key); $lookup = [ordered]@{ Alpha = 'one'; Beta = 'two' }; return $lookup[$Key]");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedOrderedDictionary",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!, "--Key=BETA");
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("two", process.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(process.StandardError), process.StandardError);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellScript.cs"));
        Assert.Contains("System.Collections.Specialized.OrderedDictionary", generated, StringComparison.Ordinal);
    }
}
