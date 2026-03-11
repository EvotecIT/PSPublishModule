using PowerForge;

public class ModuleScaffoldServiceTests
{
    [Fact]
    public void EnsureScaffold_CreatesHelpAboutSeedTopic()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-module-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var projectRoot = Path.Combine(root, "DemoModule");
        var templateRoot = Path.Combine(root, "Data");
        Directory.CreateDirectory(templateRoot);

        try
        {
            File.WriteAllText(Path.Combine(templateRoot, "Example-Gitignore.txt"), "*");
            File.WriteAllText(Path.Combine(templateRoot, "Example-CHANGELOG.MD"), "# changelog");
            File.WriteAllText(Path.Combine(templateRoot, "Example-README.MD"), "# readme");
            File.WriteAllText(Path.Combine(templateRoot, "Example-LicenseMIT.txt"), "MIT");
            File.WriteAllText(Path.Combine(templateRoot, "Example-ModuleBuilder.txt"), "Build-Module -ModuleName '$ModuleName' {}");
            File.WriteAllText(Path.Combine(templateRoot, "Example-ModulePSM1.txt"), "Export-ModuleMember -Function *");
            File.WriteAllText(Path.Combine(templateRoot, "Example-ModulePSD1.txt"), "@{ GUID = '$GUID' }");

            var service = new ModuleScaffoldService(new NullLogger());
            var result = service.EnsureScaffold(new ModuleScaffoldSpec
            {
                ModuleName = "DemoModule",
                ProjectRoot = projectRoot,
                TemplateRootPath = templateRoot
            });

            Assert.True(result.Created);

            var aboutDir = Path.Combine(projectRoot, "Help", "About");
            Assert.True(Directory.Exists(aboutDir));

            var aboutSeed = Path.Combine(aboutDir, "about_DemoModule_Overview.help.txt");
            Assert.True(File.Exists(aboutSeed));
            var text = File.ReadAllText(aboutSeed);
            Assert.Contains("about_DemoModule_Overview", text);
            Assert.Contains("Overview for DemoModule module.", text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureScaffold_UsingRepoTemplates_SeedsReadmeWithDocumentationGuidance()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-module-scaffold-readme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var projectRoot = Path.Combine(root, "DocsModule");
        try
        {
            var service = new ModuleScaffoldService(new NullLogger());
            var result = service.EnsureScaffold(new ModuleScaffoldSpec
            {
                ModuleName = "DocsModule",
                ProjectRoot = projectRoot
            });

            Assert.True(result.Created);

            var readmePath = Path.Combine(projectRoot, "README.MD");
            Assert.True(File.Exists(readmePath));
            var readme = File.ReadAllText(readmePath);
            Assert.Contains("Documentation Workflow", readme);
            Assert.Contains("Help\\About\\about_*.help.txt", readme);
            Assert.Contains("New-ModuleAboutTopic", readme);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
