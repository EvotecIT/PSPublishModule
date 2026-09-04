using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerBundleExecutablePermissionsTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void BuildBundle_PatchesEveryUnixExecutableAndScopesProvenanceToIncludedTargets()
    {
        string root = CreateTempRoot();
        try
        {
            string appOutput = Directory.CreateDirectory(Path.Combine(root, "publish", "app")).FullName;
            string helperOutput = Directory.CreateDirectory(Path.Combine(root, "publish", "helper")).FullName;
            string appExecutable = Path.Combine(appOutput, "app");
            string helperExecutable = Path.Combine(helperOutput, "helper");
            File.WriteAllText(appExecutable, "app");
            File.WriteAllText(helperExecutable, "helper");
            string bundleOutput = Path.Combine(root, "bundle");
            string bundleZip = Path.Combine(root, "bundle.zip");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "app",
                        ExecutableIdentities = ["app"],
                        Publish = new DotNetPublishPublishOptions()
                    },
                    new DotNetPublishTargetPlan
                    {
                        Name = "helper",
                        ExecutableIdentities = ["helper"],
                        Publish = new DotNetPublishPublishOptions()
                    }
                ],
                Bundles =
                [
                    new DotNetPublishBundlePlan
                    {
                        Id = "portable",
                        PrepareFromTarget = "app",
                        PrimarySubdirectory = "app",
                        Zip = true,
                        Includes =
                        [
                            new DotNetPublishBundleIncludePlan
                            {
                                Target = "helper",
                                Subdirectory = "tools",
                                Required = true
                            }
                        ]
                    }
                ]
            };
            DotNetPublishArtefactResult[] artefacts =
            [
                new()
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "app",
                    Framework = "net10.0",
                    Runtime = "linux-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputDir = appOutput,
                    PublishDir = appOutput,
                    ExePath = appExecutable
                },
                new()
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "helper",
                    Framework = "net10.0",
                    Runtime = "linux-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputDir = helperOutput,
                    PublishDir = helperOutput,
                    ExePath = helperExecutable
                }
            ];
            var step = new DotNetPublishStep
            {
                Key = "bundle:portable:app:net10.0:linux-x64:PortableCompat",
                Kind = DotNetPublishStepKind.Bundle,
                BundleId = "portable",
                TargetName = "app",
                Framework = "net10.0",
                Runtime = "linux-x64",
                Style = DotNetPublishStyle.PortableCompat,
                BundleOutputPath = bundleOutput,
                BundleZipPath = bundleZip
            };

            DotNetPublishStep[] provenanceSteps = DotNetPublishPipelineRunner.ResolveBundleProvenanceSteps(
                plan,
                artefacts,
                plan.Bundles[0],
                artefacts[0],
                "net10.0",
                "linux-x64",
                DotNetPublishStyle.PortableCompat);
            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            DotNetPublishArtefactResult result = runner.BuildBundle(plan, artefacts, step);

            Assert.Equal(new[] { "app", "helper" }, provenanceSteps.Select(entry => entry.TargetName));
            Assert.Equal(bundleZip, result.ZipPath);
            using ZipArchive archive = ZipFile.OpenRead(bundleZip);
            Assert.Equal(unchecked((int)0x81ED0000u), archive.GetEntry("app/app")!.ExternalAttributes);
            Assert.Equal(unchecked((int)0x81ED0000u), archive.GetEntry("tools/helper")!.ExternalAttributes);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
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
