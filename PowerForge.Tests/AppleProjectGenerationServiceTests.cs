namespace PowerForge.Tests;

public sealed class AppleProjectGenerationServiceTests
{
    [Fact]
    public void Generate_CreatesMissingConfiguredXcodeProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.XcodeGen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.yml"), "name: GeneratedApp");
        var projectPath = Path.Combine(root, "GeneratedApp.xcodeproj");
        try
        {
            var service = new AppleProjectGenerationService((startInfo, _) =>
            {
                Assert.Equal(root, startInfo.WorkingDirectory);
                Assert.Equal("generate", startInfo.Arguments);
                Directory.CreateDirectory(projectPath);
                File.WriteAllText(Path.Combine(projectPath, "project.pbxproj"), "// generated");
                return new AppleProjectGenerationProcessResult();
            });

            var generated = service.Generate(new PowerForgeAppleAppReleaseTargetPlan
            {
                Name = "Generated App",
                ProjectPath = projectPath,
                GenerateProjectIfMissing = true
            });

            Assert.True(generated);
            Assert.True(Directory.Exists(projectPath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
