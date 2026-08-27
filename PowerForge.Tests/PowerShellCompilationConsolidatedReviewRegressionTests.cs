using System.Reflection;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Fact]
    public void Build_HybridModulePreservesPSCustomObjectIdentityWhenItsTypeIsObserved()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ObjectType { $value = [pscustomobject]@{ A = 1 }; return $value.GetType().FullName }; " +
            "Export-ModuleMember -Function Get-ObjectType",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PSCustomObjectIdentity",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("adapted-object identity", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-ObjectType");
        Assert.Equal((0, "System.Management.Automation.PSCustomObject", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Theory]
    [InlineData("Read-Host -Prompt 'Name'")]
    [InlineData("Get-Credential")]
    [InlineData("$Host.UI.WriteLine('value')")]
    [InlineData("[CmdletBinding(SupportsShouldProcess = $true)] param(); 'value'")]
    public void Build_PackagedExecutableRejectsInteractiveEmbeddedDependencies(string dependencySource)
    {
        using var fixture = ArtifactFixture.Create(". $PSScriptRoot/Helper.ps1; 'done'");
        var dependency = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(dependency, dependencySource);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InteractiveDependency",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, dependency }
        });

        Assert.False(result.Succeeded);
        var error = result.Error ?? string.Empty;
        Assert.True(
            error.Contains("interactive", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("confirmation", StringComparison.OrdinalIgnoreCase),
            error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictLibraryNormalizesStringArrayNullElementsBeforeValidationAndExecution()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-StringElement { param([ValidateNotNull()][string[]] $Values) return $Values[1] }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StringArrayBinding",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetTypes()
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(static candidate => candidate.Name == "Get_StringElement");
        Assert.Equal(string.Empty, method.Invoke(null, new object?[] { new string?[] { "ok", null } }));
    }

    [Fact]
    public void Build_HybridModuleKeepsNullableReferenceMethodExceptionRoutingOnPowerShellPath()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NullCallResult { [CmdletBinding()] param([System.Collections.IDictionary] $Map) " +
            "try { $Map.Clear(); return 'ok' } catch [System.NullReferenceException] { return 'clr' } catch { return 'ps' } }; " +
            "Export-ModuleMember -Function Get-NullCallResult",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableMethodRouting",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("System.Management.Automation.RuntimeException", generated, StringComparison.Ordinal);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-NullCallResult");
        Assert.Equal((0, "ps", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictLibraryRejectsNullableReferenceMethodWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Clear { param([System.Collections.IDictionary] $Map) $Map.Clear() }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableMethodIndependent",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("runtime-error identity", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}
