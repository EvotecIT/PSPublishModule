using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("AvaloniaResource")]
    [InlineData("AvaloniaXaml")]
    public void ReadSourceProvenance_TracksDirtyAvaloniaSourceItem(string itemName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            string viewPath = Path.Combine(root, "MainWindow.axaml");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ITEM_NAME Include="MainWindow.axaml" /></ItemGroup>
                </Project>
                """.Replace("ITEM_NAME", itemName, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "Class1.cs"), "public static class Class1 { }");
            File.WriteAllText(viewPath, "<Window />");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(viewPath, "<Window Title=\"Changed\" />");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith(
                    "MainWindow.axaml",
                    StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
