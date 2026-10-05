namespace PowerForge.Tests;

public sealed class ModuleBuildPipelineGitMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StageToStaging_ExcludesGitMetadataForCheckoutsAndWorktrees(bool directoryMarker)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source");
        var stagingPath = Path.Combine(root, "staging");
        try
        {
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(Path.Combine(sourcePath, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(sourcePath, "payload.txt"), "module payload");
            File.WriteAllText(Path.Combine(sourcePath, ".gitattributes"), "*.ps1 text");
            var gitPath = Path.Combine(sourcePath, ".git");
            if (directoryMarker)
            {
                Directory.CreateDirectory(gitPath);
                File.WriteAllText(Path.Combine(gitPath, "HEAD"), "ref: refs/heads/main");
            }
            else
            {
                File.WriteAllText(gitPath, "gitdir: ../metadata/worktrees/sample");
            }

            var nestedPath = Directory.CreateDirectory(Path.Combine(sourcePath, "nested")).FullName;
            File.WriteAllText(Path.Combine(nestedPath, ".git"), "gitdir: ../../metadata/modules/nested");
            File.WriteAllText(Path.Combine(nestedPath, "payload.txt"), "nested payload");

            var staged = ModuleBuildPipelineFactory.Create(new NullLogger()).StageToStaging(new ModuleBuildSpec
            {
                Name = "SampleModule",
                SourcePath = sourcePath,
                StagingPath = stagingPath,
                Version = "1.0.0",
                SkipDotNetBuild = true
            });

            Assert.False(Path.Exists(Path.Combine(staged.StagingPath, ".git")));
            Assert.False(Path.Exists(Path.Combine(staged.StagingPath, "nested", ".git")));
            Assert.Equal("module payload", File.ReadAllText(Path.Combine(staged.StagingPath, "payload.txt")));
            Assert.Equal("nested payload", File.ReadAllText(Path.Combine(staged.StagingPath, "nested", "payload.txt")));
            Assert.Equal("*.ps1 text", File.ReadAllText(Path.Combine(staged.StagingPath, ".gitattributes")));
            Assert.True(Path.Exists(gitPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}