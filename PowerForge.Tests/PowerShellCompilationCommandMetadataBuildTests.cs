using System.Xml.Linq;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CommandMetadataBuild_IndependentRebuildAndIncrementalBuildPreserveCommandIdentity()
    {
        using var fixture = ArtifactFixture.Create("function Get-RebuiltValue { [CmdletBinding()] param([int]$Value) return $Value }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RebuiltIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        { TargetFramework = "net10.0", KeepBuildWorkspace = true });
        try
        {
            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.NotNull(result.BuildWorkspace);
            var project = Assert.Single(Directory.GetFiles(result.BuildWorkspace!, "*.csproj"));
            var references = XDocument.Load(project).Descendants("PackageReference")
                .Where(item => PowerShellCommandMetadataBuildSupport.PackageIds.Contains((string)item.Attribute("Include")!)).ToArray();
            Assert.Equal(2, references.Length);
            Assert.All(references, item => Assert.Equal("none", (string?)item.Attribute("IncludeAssets")));
            var assembly = Path.Combine(result.BuildWorkspace!, "bin", "Release", "net10.0", "Generated.RebuiltIdentity.dll");
            foreach (var target in new[] { "Rebuild", "Build" })
            {
                var build = RunProcess("dotnet", "msbuild", project, "-t:" + target, "-p:Configuration=Release", "-nologo", "-v:minimal");
                Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
                Assert.True(File.Exists(assembly), build.StandardOutput);
                var probe = RunStatementErrorProbe("pwsh", "Import-Module '" + EscapeStatementErrorPath(assembly) +
                    "'; (Get-Command Get-RebuiltValue).ImplementingType.FullName; Get-RebuiltValue 42; try { Get-RebuiltValue bad } catch { $_.FullyQualifiedErrorId }",
                    fixture.RootPath, "rebuilt-" + target);
                Assert.True(probe.ExitCode == 0, probe.StandardOutput + probe.StandardError);
                Assert.Contains("Get-RebuiltValue", probe.StandardOutput, StringComparison.Ordinal);
                Assert.Contains("42", probe.StandardOutput, StringComparison.Ordinal);
                Assert.Contains("ParameterArgumentTransformationError,Get-RebuiltValue", probe.StandardOutput, StringComparison.Ordinal);
            }
            var clean = RunProcess("dotnet", "msbuild", project, "-t:Clean", "-p:Configuration=Release", "-nologo", "-v:q");
            Assert.True(clean.ExitCode == 0, clean.StandardOutput + clean.StandardError);
            Assert.Empty(Directory.EnumerateFiles(result.BuildWorkspace!, "PowerForge.CommandIdentities.stamp", SearchOption.AllDirectories));
        }
        finally
        {
            if (result.BuildWorkspace is { } workspace && Directory.Exists(workspace))
            {
                var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge", "powershell-compilation"));
                Assert.Equal(root, Path.GetDirectoryName(Path.GetFullPath(workspace)), ignoreCase: OperatingSystem.IsWindows());
                Assert.True(File.Exists(Path.Combine(workspace, ".powerforge-compiler-workspace")));
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void CommandMetadataBuild_PreservesLiteralNamesThroughMsBuildEvaluation()
    {
        using var fixture = ArtifactFixture.Create("'unused'");
        const string name = "Get-$(MSBuildProjectName);%28literal%29@(Compile)'?";
        var projectPath = Path.Combine(fixture.RootPath, "literal.proj");
        var outputPath = Path.Combine(fixture.RootPath, "evaluated.txt");
        var project = new XElement("Project",
            new XElement("ItemGroup", new XElement("Command", new XAttribute("Include", "Generated.Command"),
                new XElement("CommandName", PowerShellCommandMetadataBuildSupport.EscapeProjectLiteral(name)))),
            new XElement("Target", new XAttribute("Name", "Probe"),
                new XElement("WriteLinesToFile", new XAttribute("File", outputPath),
                    new XAttribute("Lines", "@(Command->'%(CommandName)')"), new XAttribute("Overwrite", "true"))));
        new XDocument(project).Save(projectPath);
        var result = RunProcess("dotnet", "msbuild", projectPath, "-t:Probe", "-nologo", "-v:q");
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(name, File.ReadAllText(outputPath).TrimEnd('\r', '\n'));
    }
}
