namespace PowerForge.Tests;

public sealed class ModuleBuildPipelineReuseStagingTests
{
    [Fact]
    public void StageToStaging_ReusesExistingPayloadWithoutCopyingSourceAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source");
        var stagingPath = Path.Combine(root, "staging");
        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(stagingPath);
            File.WriteAllText(Path.Combine(sourcePath, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(sourcePath, "source-only.txt"), "must not be copied");
            File.WriteAllText(Path.Combine(stagingPath, "SampleModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(stagingPath, "checkpoint.txt"), "approved output");

            var staged = ModuleBuildPipelineFactory
                .Create(new NullLogger())
                .StageToStaging(new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = sourcePath,
                    StagingPath = stagingPath,
                    Version = "1.0.0",
                    SkipDotNetBuild = true,
                    ReuseStaging = true
                });

            Assert.Equal(Path.GetFullPath(stagingPath), staged.StagingPath);
            Assert.Equal("approved output", File.ReadAllText(Path.Combine(stagingPath, "checkpoint.txt")));
            Assert.False(File.Exists(Path.Combine(stagingPath, "source-only.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
