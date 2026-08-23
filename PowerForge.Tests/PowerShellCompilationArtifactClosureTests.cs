using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_PackagedExecutableDoesNotFailForNonterminatingErrorRecords()
    {
        using var fixture = ArtifactFixture.Create("Write-Error 'reported'; 'completed'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NonterminatingError",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("completed", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reported", run.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_BinaryModuleRejectsParameterNameThatClrMetadataCannotPreserve()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PathValue { param([string] ${output-path}); return ${output-path} }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ParameterIdentity",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("preserve its PowerShell name", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_BinaryModuleEnumeratesPowerShellEnumerableReturnValues()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Items { return [System.Collections.ArrayList]::new() }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CollectionEnumeration",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; (Get-Items | Measure-Object).Count");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleStagesTransitiveContainedDotSourcedDependencies()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/Private/Outer.ps1\"; function Get-TypedValue { return 1 }; function Get-DependencyValue { return Get-InnerValue }; Export-ModuleMember -Function @('Get-TypedValue', 'Get-DependencyValue')",
            ".psm1");
        var privateDirectory = Path.Combine(fixture.RootPath, "Private");
        var nestedDirectory = Path.Combine(privateDirectory, "Nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(privateDirectory, "Outer.ps1"), ". \"$PSScriptRoot/Nested/Inner.ps1\"");
        File.WriteAllText(Path.Combine(nestedDirectory, "Inner.ps1"), "function Get-InnerValue { return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DotSourceDependencies",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Files, file => file.Role == "ModuleDependency" && file.Path.EndsWith(Path.Combine("Private", "Outer.ps1"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Manifest.Files, file => file.Role == "ModuleDependency" && file.Path.EndsWith(Path.Combine("Private", "Nested", "Inner.ps1"), StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-TypedValue; Get-DependencyValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "1", "42" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleRejectsDynamicDotSourceBeforePublication()
    {
        using var fixture = ArtifactFixture.Create(
            ". (Join-Path $PSScriptRoot 'Private/Helpers.ps1'); function Get-Value { return 1 }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DynamicDotSource",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.False(result.Succeeded);
        Assert.Contains("Dot-source expression", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModuleRejectsWorkingDirectoryRelativeDotSourceBeforePublication()
    {
        using var fixture = ArtifactFixture.Create(
            ". 'Private/Helpers.ps1'; function Get-Value { return 1 }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RelativeDotSource",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.False(result.Succeeded);
        Assert.Contains("portable hybrid staging", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridManifestPreservesAliasPolicyWhenAliasesToExportIsOmitted()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedValue { return 1 }; function Get-AliasTarget { return Write-Output 7 }; New-Alias -Name pfalias -Value Get-AliasTarget; Export-ModuleMember -Function Get-TypedValue, Get-AliasTarget -Alias pfalias",
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; GUID = '2ac7e348-9f58-4690-8867-051910e848a4'; FunctionsToExport = @('Get-TypedValue', 'Get-AliasTarget'); CmdletsToExport = @(); VariablesToExport = @() }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.OmittedAliasPolicy",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.DoesNotContain("AliasesToExport", File.ReadAllText(result.ArtifactPath!), StringComparison.OrdinalIgnoreCase);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command pfalias -ErrorAction SilentlyContinue); pfalias");
        Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        Assert.Equal(new[] { "True", "7" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_Net472RejectsMemberUnavailableToRequestedTargetBeforeDotNetCompilation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-Contains { param([string] $Value); return $Value.Contains('x', [System.StringComparison]::Ordinal) }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TargetMemberSurface",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net472"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("No exact CLR overload", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("function Get-ProcessId { return [System.Environment]::ProcessId }")]
    [InlineData("function Get-TrimEntries { return [System.StringSplitOptions]::TrimEntries }")]
    public void Build_Net472RejectsStaticMemberUnavailableToRequestedTargetBeforeDotNetCompilation(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TargetStaticMemberSurface",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net472"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("readable field or property", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}
