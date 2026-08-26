using System.Reflection;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Fact]
    public void Build_HybridModuleDoesNotStageCompiledFilesFromManifestFileList()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FileListValue { return 41 }; Export-ModuleMember -Function Get-FileListValue",
            ".psm1");
        var manifest = Path.Combine(fixture.RootPath, "Demo.psd1");
        File.WriteAllText(
            manifest,
            "@{ RootModule = 'Source.psm1'; ModuleVersion = '1.0.0'; " +
            "FunctionsToExport = @('Get-FileListValue'); FileList = @('Source.psm1', 'Demo.psd1') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Demo",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            ModuleManifestPath = manifest
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.DoesNotContain(result.Manifest!.Dependencies, dependency =>
            dependency.Selection == PowerShellCompilationDependencySelection.Required &&
            (dependency.RelativePath.Equals("Source.psm1", StringComparison.OrdinalIgnoreCase) ||
             dependency.RelativePath.Equals("Demo.psd1", StringComparison.OrdinalIgnoreCase)));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-FileListValue");
        Assert.Equal((0, "41", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModulePreservesPreDeclarationWildcardGetCommandDiscovery()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:before = [bool](Get-Command 'Get-PowerForgeLater*' -ErrorAction SilentlyContinue); " +
            "function Get-PowerForgeLater { return 1 }; function Get-Before { return $script:before }; " +
            "Export-ModuleMember -Function Get-PowerForgeLater, Get-Before",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.WildcardDiscovery",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-Before");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsEscapedTopLevelMyInvocation()
    {
        using var fixture = ArtifactFixture.Create("$invocation = $MyInvocation; return $invocation.InvocationName");
        var result = BuildExecutable(fixture, "PowerForge.EscapedInvocation", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("escaped top-level invocation metadata", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("$invocation = Get-Variable -Name MyInvocation -ValueOnly; return $invocation.InvocationName")]
    [InlineData("$invocation = gv MyInv* -ValueOnly; return $invocation.InvocationName")]
    [InlineData("$invocation = (Get-Variable -Scope Local | Where-Object Name -eq MyInvocation).Value; return $invocation.InvocationName")]
    [InlineData("$name = 'MyInvocation'; $invocation = Get-Variable -Name $name -ValueOnly; return $invocation.InvocationName")]
    [InlineData("$invocation = (Get-Item Variable:MyInvocation).Value; return $invocation.InvocationName")]
    [InlineData("$invocation = $ExecutionContext.SessionState.PSVariable.Get('MyInvocation'); return $invocation.InvocationName")]
    [InlineData("$name = 'MyInvocation'; $invocation = $ExecutionContext.SessionState.PSVariable.Get($name); return $invocation.InvocationName")]
    public void Build_PackagedExecutableRejectsIndirectTopLevelMyInvocationRetrieval(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.IndirectInvocation", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("indirect top-level invocation metadata", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModuleRejectsPowerShellBindingExceptionCatchBeforeLocalCallLowering()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Validated { param([ValidateSet('ok')][string] $ZebraText) return $ZebraText }; " +
            "function Get-ValidationRoute { try { Invoke-Validated -ZebraText 'bad' } " +
            "catch [System.Management.Automation.ParameterBindingException] { return 'validation' } " +
            "catch { return 'other' }; return 'unreachable' }; Export-ModuleMember -Function Invoke-Validated, Get-ValidationRoute",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ValidationSubtype",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("outside the generated project reference set", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-ValidationRoute");
        Assert.Equal((0, "validation", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsProgressHostInteraction()
    {
        using var fixture = ArtifactFixture.Create("Write-Progress -Activity 'work' -PercentComplete 50; 'done'");
        var result = BuildExecutable(fixture, "PowerForge.ProgressHost", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("Write-Progress", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("IsWindows")]
    [InlineData("IsLinux")]
    [InlineData("IsMacOS")]
    [InlineData("IsCoreCLR")]
    [InlineData("PSEdition")]
    public void Build_StrictExecutableRejectsReadOnlyAutomaticVariableParameterNames(string name)
    {
        using var fixture = ArtifactFixture.Create($"param([string] ${name}); return ${name}");
        var result = BuildExecutable(fixture, "PowerForge.ReadOnlyParameter", PowerShellCompilationMode.Strict);

        Assert.False(result.Succeeded);
        Assert.Contains("read-only automatic variable", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("IsWindows")]
    [InlineData("IsLinux")]
    [InlineData("IsMacOS")]
    [InlineData("IsCoreCLR")]
    public void Analyze_Net472AllowsParametersThatAreNotAutomaticVariablesOnWindowsPowerShell(string name)
    {
        using var fixture = ArtifactFixture.Create($"function Get-LegacyPlatformValue {{ param([string] ${name}) return ${name} }}", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net472",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.DoesNotContain(plan.Files.SelectMany(static file => file.Units).SelectMany(static unit => unit.Diagnostics), diagnostic =>
            diagnostic.Message.Contains("read-only automatic variable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_HybridModuleQualifiesGeneratedOrchestrationCommands()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-QualifiedOwnerValue { return 42 }; Export-ModuleMember -Function Get-QualifiedOwnerValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.QualifiedOwners",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "function global:Import-Module { throw 'shadow import' }; " +
            "function global:Join-Path { throw 'shadow join' }; " +
            "function global:Export-ModuleMember { throw 'shadow export' }; " +
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-QualifiedOwnerValue");
        Assert.Equal((0, "42", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridModulePreservesPreDeclarationGcmAliasDiscovery()
    {
        using var fixture = ArtifactFixture.Create(
            "$script:before = [bool](gcm Get-PowerForgeAliasLater -ErrorAction SilentlyContinue); " +
            "function Get-PowerForgeAliasLater { return 1 }; function Get-AliasBefore { return $script:before }; " +
            "Export-ModuleMember -Function Get-PowerForgeAliasLater, Get-AliasBefore",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AliasDiscovery",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("availability timing", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Microsoft.PowerShell.Core\\Import-Module -Name '{escapedPath}' -Force; Get-AliasBefore");
        Assert.Equal((0, "False", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_BinaryModuleUsesManifestVersionForAssemblyIdentity()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-VersionedValue { return 1 }; Export-ModuleMember -Function Get-VersionedValue",
            ".psm1");
        var manifest = Path.Combine(fixture.RootPath, "Versioned.psd1");
        File.WriteAllText(
            manifest,
            "@{ RootModule = 'Source.psm1'; ModuleVersion = '2.3.4'; FunctionsToExport = @('Get-VersionedValue') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Versioned",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            ModuleManifestPath = manifest
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assert.Single(result.Manifest!.Files, file => file.Role == "TypedAssembly");
        Assert.Equal(new Version(2, 3, 4, 0), AssemblyName.GetAssemblyName(assembly.Path).Version);
    }

    [Fact]
    public void Build_BinaryModuleRejectsMismatchedAutomaticallyDiscoveredSiblingManifest()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SelectedValue { return 1 }; Export-ModuleMember -Function Get-SelectedValue",
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'Other.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-SelectedValue') }");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.StaleSibling",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid)));

        Assert.Contains("does not own selected compilation source", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Build_BinaryModuleUsesAutomaticallyDiscoveredSiblingManifestForIdentityAndPayload()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SiblingValue { return 1 }; Export-ModuleMember -Function Get-SiblingValue",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Data.txt"), "payload");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'Source.psm1'; ModuleVersion = '4.5.6'; FunctionsToExport = @('Get-SiblingValue'); FileList = @('Data.txt') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SiblingManifest",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = Assert.Single(result.Manifest!.Files, file => file.Role == "TypedAssembly");
        Assert.Equal(new Version(4, 5, 6, 0), AssemblyName.GetAssemblyName(assembly.Path).Version);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(fixture.OutputPath, "PowerForge.SiblingManifest", "Data.txt")));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsMandatoryInteractiveBindingContract()
    {
        using var fixture = ArtifactFixture.Create("param([Parameter(Mandatory)][string] $Name); return $Name");
        var result = BuildExecutable(fixture, "PowerForge.MandatoryPackage", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("interactive prompting", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}
