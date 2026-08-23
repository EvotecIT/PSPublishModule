using System;
using System.Diagnostics;
using System.IO;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_RejectsManifestFileReferenceOutsideModuleRoot()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); FormatsToProcess = @('../outside.format.ps1xml') }");
        var result = BuildManifestFixture(fixture, "PowerForge.EscapingManifest");

        Assert.False(result.Succeeded);
        Assert.Contains("escapes the module root", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("C:\\outside\\Helper.dll")]
    [InlineData("\\\\server\\share\\Helper.dll")]
    public void Build_RejectsWindowsRootedManifestFileReferenceOnEveryPlatform(string manifestPath)
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            $"@{{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); RequiredAssemblies = @('{manifestPath}') }}");

        var result = BuildManifestFixture(fixture, "PowerForge.RootedManifestReference");

        Assert.False(result.Succeeded);
        Assert.Contains("must remain relative", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("FormatsToProcess", "missing.format.ps1xml")]
    [InlineData("TypesToProcess", "missing.types.ps1xml")]
    [InlineData("RequiredAssemblies", "lib/missing.dll")]
    public void Build_RejectsMissingRequiredManifestFileReference(string key, string relativePath)
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            $"@{{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); {key} = @('{relativePath}') }}");
        var result = BuildManifestFixture(fixture, "PowerForge.MissingManifestReference");

        Assert.False(result.Succeeded);
        Assert.Contains("Required module manifest file reference", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_CopiesAndImportsContainedRequiredAssembly()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        var dependencyDirectory = Path.Combine(fixture.RootPath, "lib");
        Directory.CreateDirectory(dependencyDirectory);
        File.Copy(
            typeof(System.Management.Automation.PSObject).Assembly.Location,
            Path.Combine(dependencyDirectory, "System.Management.Automation.dll"));
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; GUID = '178592f4-f773-4059-878f-dc366ecaf262'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); RequiredAssemblies = @('lib/System.Management.Automation.dll') }");

        var result = BuildManifestFixture(fixture, "PowerForge.RequiredAssembly");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "lib", "System.Management.Automation.dll")));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = RunPowerShell(
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force -ErrorAction Stop; Get-PublicValue");
        Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        Assert.Equal("1", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Theory]
    [InlineData("FormatsToProcess", ".\\Formats\\Demo.ps1xml", "Formats", "Demo.ps1xml")]
    [InlineData("RequiredAssemblies", "lib\\Helper.dll", "lib", "Helper.dll")]
    public void Build_NormalizesWindowsStyleManifestFileReferences(string key, string manifestPath, string directory, string fileName)
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        var dependencyDirectory = Path.Combine(fixture.RootPath, directory);
        Directory.CreateDirectory(dependencyDirectory);
        File.WriteAllText(Path.Combine(dependencyDirectory, fileName), "dependency");
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            $"@{{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); {key} = @('{manifestPath}') }}");

        var result = BuildManifestFixture(fixture, "PowerForge.PortableManifestPath");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, directory, fileName)));
    }

    [Fact]
    public void Build_PreservesNamedExternalRequiredAssemblyWithoutTreatingItAsAFile()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; GUID = 'da7fd1ed-759a-4168-b5cb-bd1ee3ba68cd'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); RequiredAssemblies = @('System.Xml') }");

        var result = BuildManifestFixture(fixture, "PowerForge.ExternalAssembly");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = File.ReadAllText(Path.ChangeExtension(result.ArtifactPath!, ".psd1"));
        Assert.Contains("System.Xml", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsLinkedManifestFileReferenceBeforePublication()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        var outsidePath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N") + ".format.ps1xml");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        File.WriteAllText(outsidePath, "<Configuration />");
        var linkPath = Path.Combine(fixture.RootPath, "linked.format.ps1xml");
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
            File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
                "@{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); FormatsToProcess = @('linked.format.ps1xml') }");
            var result = BuildManifestFixture(fixture, "PowerForge.LinkedManifestReference");

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
    public void Build_RejectsManifestFileReferenceThatCollidesWithGeneratedAssembly()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        const string artifactName = "PowerForge.CollidingManifest";
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, artifactName + ".dll"), "source dependency");
        File.WriteAllText(Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            $"@{{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); FileList = @('{artifactName}.dll') }}");
        var result = BuildManifestFixture(fixture, artifactName);

        Assert.False(result.Succeeded);
        Assert.Contains("collides with a generated compilation artifact", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    private static PowerShellCompilationBuildResult BuildManifestFixture(ArtifactFixture fixture, string artifactName)
        => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            artifactName,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

    private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShell(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "PowerShell module import did not exit within 60 seconds.");
        return (process.ExitCode, standardOutput, standardError);
    }
}
