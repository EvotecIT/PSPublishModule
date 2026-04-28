namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerBundleHardeningTests
{
    [Fact]
    public void BuildBundle_RejectsGeneratedScriptTemplateOutsideProjectRoot()
    {
        var root = CreateTempRoot();
        var outsideRoot = Path.GetFullPath(Path.Combine(root, "..", Path.GetFileName(root) + "-outside"));
        try
        {
            Directory.CreateDirectory(outsideRoot);
            File.WriteAllText(Path.Combine(outsideRoot, "Install.ps1.template"), "{{CommandName}}");

            var publishDir = Directory.CreateDirectory(Path.Combine(root, "publish", "app")).FullName;
            File.WriteAllText(Path.Combine(publishDir, "App.exe"), "app");

            var plan = CreatePlan(
                root,
                new DotNetPublishBundlePlan
                {
                    Id = "package",
                    PrepareFromTarget = "app",
                    GeneratedScripts = new[]
                    {
                        new DotNetPublishBundleGeneratedScriptPlan
                        {
                            TemplatePath = Path.Combine("..", Path.GetFileName(outsideRoot), "Install.ps1.template"),
                            OutputPath = "Install.ps1",
                            Tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["CommandName"] = "Install-App"
                            }
                        }
                    }
                });

            var ex = Assert.Throws<InvalidOperationException>(() => BuildBundle(plan, publishDir));
            Assert.Contains("outside ProjectRoot", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
            TryDelete(outsideRoot);
        }
    }

    [Fact]
    public void BuildBundle_CopyItemThrowsWhenDestinationExistsAndClearDestinationDisabled()
    {
        var root = CreateTempRoot();
        try
        {
            var publishDir = Directory.CreateDirectory(Path.Combine(root, "publish", "app")).FullName;
            File.WriteAllText(Path.Combine(publishDir, "App.exe"), "app");

            var readmePath = Path.Combine(root, "Build", "README.package.md");
            Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);
            File.WriteAllText(readmePath, "# Package");

            var outputDir = Directory.CreateDirectory(Path.Combine(root, "Artifacts", "Bundles", "package")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "README.md"), "# Existing");

            var plan = CreatePlan(
                root,
                new DotNetPublishBundlePlan
                {
                    Id = "package",
                    PrepareFromTarget = "app",
                    ClearOutput = false,
                    CopyItems = new[]
                    {
                        new DotNetPublishBundleCopyItemPlan
                        {
                            SourcePath = "Build/README.package.md",
                            DestinationPath = "README.md",
                            ClearDestination = false
                        }
                    }
                });

            Assert.Throws<IOException>(() => BuildBundle(plan, publishDir, outputDir));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishPlan CreatePlan(string root, DotNetPublishBundlePlan bundle)
    {
        return new DotNetPublishPlan
        {
            ProjectRoot = root,
            Bundles = new[] { bundle }
        };
    }

    private static DotNetPublishArtefactResult BuildBundle(
        DotNetPublishPlan plan,
        string publishDir,
        string? outputDir = null)
    {
        outputDir ??= Path.Combine(plan.ProjectRoot, "Artifacts", "Bundles", "package");
        var artefacts = new[]
        {
            new DotNetPublishArtefactResult
            {
                Category = DotNetPublishArtefactCategory.Publish,
                Target = "app",
                Framework = "net10.0",
                Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat,
                OutputDir = publishDir,
                PublishDir = publishDir
            }
        };
        var step = new DotNetPublishStep
        {
            Key = "bundle:package:app:net10.0:win-x64:PortableCompat",
            Kind = DotNetPublishStepKind.Bundle,
            BundleId = "package",
            TargetName = "app",
            Framework = "net10.0",
            Runtime = "win-x64",
            Style = DotNetPublishStyle.PortableCompat,
            BundleOutputPath = outputDir
        };

        return new DotNetPublishPipelineRunner(new NullLogger()).BuildBundle(plan, artefacts, step);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
