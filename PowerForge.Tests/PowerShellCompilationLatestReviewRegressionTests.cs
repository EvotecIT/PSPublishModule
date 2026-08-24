using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_PackagedExecutablePreservesPathVariablesInOmittedParameterDefaults()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Config = \"$PSScriptRoot/config.json\", [string] $Command = $PSCommandPath); $Config; $Command");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedDefaultPaths",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal(0, run.ExitCode);
        var output = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, output.Length);
        AssertPathsEqual(Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "config.json"), output[0]);
        AssertPathsEqual(result.ArtifactPath!, output[1]);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_RejectsLinkedOutputDirectoryBeforeReplacingProtectedSource()
    {
        var container = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var physicalOutput = Path.Combine(container, "physical-output");
        var linkedOutput = Path.Combine(container, "linked-output");
        const string artifactName = "PowerForge.LinkedOutput";
        var protectedDirectory = Path.Combine(physicalOutput, artifactName);
        var sourcePath = Path.Combine(protectedDirectory, "input.ps1");
        Directory.CreateDirectory(protectedDirectory);
        File.WriteAllText(sourcePath, "function Get-Value { return 1 }");
        try
        {
            Directory.CreateSymbolicLink(linkedOutput, physicalOutput);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(container, recursive: true);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Directory.Delete(container, recursive: true);
            return;
        }

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                    sourcePath,
                    linkedOutput,
                    artifactName,
                    PowerShellCompilationArtifactKind.Library,
                    PowerShellCompilationMode.Strict)));

            Assert.Contains("symbolic link or junction", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(sourcePath));
            Assert.Equal("function Get-Value { return 1 }", File.ReadAllText(sourcePath));
        }
        finally
        {
            try { Directory.Delete(linkedOutput); } catch { }
            try { Directory.Delete(container, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_PackagedExecutableRejectsDotSourcedDependencyBeforePublishing()
    {
        using var fixture = ArtifactFixture.Create(". $PSScriptRoot/Helper.ps1; Get-HelperValue");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Helper.ps1"), "function Get-HelperValue { return 9 }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedDotSource",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package));

        Assert.False(result.Succeeded);
        Assert.Contains("dot-sourced", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsExplicitNamedBlocksBeforePublishing()
    {
        var sources = new[]
        {
            "dynamicparam { } end { 'done' }",
            "begin { 'begin' } process { 'process' } end { 'end' }",
            "clean { 'clean' } end { 'end' }"
        };
        foreach (var source in sources)
        {
            using var fixture = ArtifactFixture.Create(source);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.PackagedNamedBlock",
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Package));

            Assert.False(result.Succeeded);
            Assert.Contains("named block", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
    }

    [Fact]
    public void Build_PackagedExecutableRejectsInteractiveHostRequirements()
    {
        var sources = new[]
        {
            "Read-Host -Prompt 'Name'",
            "$Host.UI.PromptForChoice('Title', 'Question', @(), 0)",
            "Get-Credential"
        };
        foreach (var source in sources)
        {
            using var fixture = ArtifactFixture.Create(source);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.PackagedInteractiveHost",
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Package));

            Assert.False(result.Succeeded);
            Assert.Contains("interactive", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
    }

    [Fact]
    public void Analyze_RejectsIndexedAndMemberAssignmentInsteadOfCompilingTheContainedVariable()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-Indexed { param([int[]] $Values) $Values[0] = 9; return $Values[0] } " +
            "function Set-Member { param([string] $Value) $Value.Length = 1; return $Value.Length }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var units = Assert.Single(plan.Files).Units;
        Assert.Equal(2, units.Length);
        Assert.All(units, static unit =>
        {
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("direct local-variable assignment", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Analyze_RejectsWritesToReadOnlyAutomaticVariables()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-True { $true = $false; return 1 } " +
            "function Set-Home { $HOME = 'elsewhere'; return 1 } " +
            "function Set-Pid { $PID = 1; return 1 } " +
            "function Set-Edition { $PSEdition = 'Desktop'; return 1 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var units = Assert.Single(plan.Files).Units;
        Assert.Equal(4, units.Length);
        Assert.All(units, static unit =>
        {
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("read-only automatic variable", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Analyze_RejectsConditionallyUnassignedValueTypeLocals()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ConditionalBoolean { if ($value = $true) { }; return $value } " +
            "function Get-ConditionalInteger { if ($value = 9) { }; return $value }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var units = Assert.Single(plan.Files).Units;
        Assert.Equal(2, units.Length);
        Assert.All(units, static unit =>
        {
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("may remain unassigned", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void BuildSpec_DefaultModesAreValidForEveryArtifactKind()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var library = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DefaultLibrary",
            PowerShellCompilationArtifactKind.Library);
        var module = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DefaultModule",
            PowerShellCompilationArtifactKind.BinaryModule);
        var executable = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DefaultExecutable",
            PowerShellCompilationArtifactKind.Executable);

        Assert.Equal(PowerShellCompilationMode.Hybrid, library.Mode);
        Assert.Equal(PowerShellCompilationMode.Strict, module.Mode);
        Assert.Equal(PowerShellCompilationMode.Package, executable.Mode);
        var result = new PowerShellCompilationArtifactBuilder().Build(library);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
    }
}
