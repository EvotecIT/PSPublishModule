using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArtifactPublisherSerializesConcurrentFlatAndDirectoryReplacements(bool directoryArtifact)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        const string artifactName = "PowerForge.ConcurrentArtifact";
        using var gate = new ManualResetEventSlim(initialState: false);
        try
        {
            var tasks = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            {
                var marker = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var staging = PowerShellArtifactSetPublisher.CreateStagingDirectory(outputDirectory, artifactName);
                if (directoryArtifact)
                {
                    var artifactDirectory = Path.Combine(staging, artifactName);
                    Directory.CreateDirectory(artifactDirectory);
                    File.WriteAllText(Path.Combine(artifactDirectory, "marker.txt"), marker);
                }
                else
                {
                    File.WriteAllText(Path.Combine(staging, artifactName + ".dll"), marker);
                }
                File.WriteAllText(Path.Combine(staging, artifactName + ".powerforge-compilation.json"), marker);
                gate.Wait();
                PowerShellArtifactSetPublisher.Commit(staging, outputDirectory, artifactName);
            })).ToArray();

            gate.Set();
            await Task.WhenAll(tasks);

            var artifactMarker = directoryArtifact
                ? File.ReadAllText(Path.Combine(outputDirectory, artifactName, "marker.txt"))
                : File.ReadAllText(Path.Combine(outputDirectory, artifactName + ".dll"));
            Assert.Equal(artifactMarker, File.ReadAllText(Path.Combine(outputDirectory, artifactName + ".powerforge-compilation.json")));
            Assert.Empty(Directory.EnumerateDirectories(outputDirectory, ".*.artifact-*", SearchOption.TopDirectoryOnly));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "." + artifactName + ".artifact-publish.lock")));
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_PackagedExecutableDoesNotFailForNonterminatingErrorRecords()
    {
        using var fixture = ArtifactFixture.Create("param([switch] $Terminate); if ($Terminate) { throw 'stopped' }; Write-Error 'reported'; 'completed'");
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

        var terminatingRun = Run(result.ArtifactPath!, "--Terminate");
        Assert.Equal(1, terminatingRun.ExitCode);
        Assert.Contains("stopped", terminatingRun.StandardError, StringComparison.OrdinalIgnoreCase);
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
            "function Get-Items { return [System.Collections.ArrayList]::new() }; function Get-Map { return [System.Collections.Hashtable]::new() }");
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
            $"Import-Module -Name '{escapedPath}' -Force; (Get-Command Get-Items).OutputType[0].Type.FullName; (Get-Items | Measure-Object).Count; (Get-Command Get-Map).OutputType[0].Type.FullName; (Get-Map | Measure-Object).Count");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "System.Object", "0", "System.Collections.Hashtable", "1" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_BinaryModuleAdvertisesArrayElementOutputType()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Numbers { param([int[]] $class); return $class }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ArrayOutputMetadata",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; (Get-Command Get-Numbers).OutputType[0].Type.FullName; (Get-Command Get-Numbers).Parameters.ContainsKey('class'); Get-Numbers -class 1,2");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "System.Int32", "True", "1", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
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
    public void Build_HybridModuleStagesTransitiveManifestHookDependencies()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedValue { return 1 }; Export-ModuleMember -Function Get-TypedValue",
            ".psm1");
        var hooksDirectory = Path.Combine(fixture.RootPath, "Hooks");
        Directory.CreateDirectory(hooksDirectory);
        File.WriteAllText(Path.Combine(hooksDirectory, "Initialize.ps1"), ". \"$PSScriptRoot/Private.ps1\"");
        File.WriteAllText(Path.Combine(hooksDirectory, "Private.ps1"), "$global:PowerForgeCompilationHookValue = 42");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; GUID = '3b59b9db-aa92-4a03-a54e-4054b2cf8f85'; ScriptsToProcess = @('Hooks/Initialize.ps1'); FunctionsToExport = @('Get-TypedValue') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ManifestHookDependencies",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Files, file => file.Role == "ModuleDependency" && file.Path.EndsWith(Path.Combine("Hooks", "Private.ps1"), StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; $global:PowerForgeCompilationHookValue; Get-TypedValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "42", "1" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesNamedExternalNestedModule()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedValue { return 1 }; Export-ModuleMember -Function Get-TypedValue",
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; GUID = 'b02be75a-f6a9-421e-94fb-02d51178eeba'; NestedModules = @('Microsoft.PowerShell.Utility'); FunctionsToExport = @('Get-TypedValue') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExternalNestedModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-TypedValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("1", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleRejectsLinkedDotSourceDependencyBeforePublication()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/Private/Linked.ps1\"; function Get-TypedValue { return 1 }",
            ".psm1");
        var privateDirectory = Path.Combine(fixture.RootPath, "Private");
        Directory.CreateDirectory(privateDirectory);
        var outsidePath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N") + ".ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        File.WriteAllText(outsidePath, "$global:PowerForgeShouldNotBePackaged = $true");
        var linkPath = Path.Combine(privateDirectory, "Linked.ps1");
        try
        {
            File.CreateSymbolicLink(linkPath, outsidePath);
        }
        catch (UnauthorizedAccessException)
        {
            File.Delete(outsidePath);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            File.Delete(outsidePath);
            return;
        }

        try
        {
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.LinkedDependency",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid));

            Assert.False(result.Succeeded);
            Assert.Contains("symbolic link or junction", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
        finally
        {
            File.Delete(outsidePath);
        }
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
    [InlineData("net8.0", false)]
    [InlineData("net10.0", true)]
    public void Build_GuidVersion7MethodFollowsRequestedTargetSurface(string targetFramework, bool succeeds)
    {
        using var fixture = ArtifactFixture.Create("function New-Version7Guid { return [System.Guid]::CreateVersion7() }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TargetMethodMatrix",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = targetFramework
        });

        Assert.Equal(succeeds, result.Succeeded);
        if (succeeds)
            Assert.NotNull(result.ArtifactPath);
        else
        {
            Assert.Contains("No exact CLR overload", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
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
