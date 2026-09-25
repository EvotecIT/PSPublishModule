using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void NormalSignedPublish_UsesWorkingTreeWithoutControlledMsBuildReconstruction()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            string sourcePath = Path.Combine(projectDirectory, "Program.cs");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(sourcePath, "class Program { static void Main() { } }");
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
                "<Project><PropertyGroup><IsRunner>$([System.String]::Copy('$(MSBuildProjectFullPath)').Contains('/runner/'))</IsRunner></PropertyGroup></Project>");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"normal publish source\"");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = RunGit(root, "rev-parse HEAD").Trim(),
                Targets = new[] { new DotNetPublishTargetPlan { ProjectPath = projectPath } }
            };

            DotNetPublishPipelineRunner.SourceProvenance clean =
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan);
            Assert.False(clean.Dirty);

            File.AppendAllText(sourcePath, "// changed");
            InvalidOperationException dirty = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan));
            Assert.Contains("source changed", dirty.Message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(sourcePath, "class Program { static void Main() { } }");
            string siblingDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string siblingPath = Path.Combine(siblingDirectory, "Library.cs");
            File.WriteAllText(siblingPath, "class Library { }");
            InvalidOperationException siblingDirty = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan));
            Assert.Contains("Library/Library.cs", siblingDirty.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

}
