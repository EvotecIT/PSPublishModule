using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void OptimizeDetailed_DoesNotOverwriteProtectedStoriesWithAuxiliaryOutputs()
    {
        var bundleRoot = WebVisualStoryStagerTests.CreateBundle();
        var siteRoot = Path.Combine(Path.GetTempPath(), "pf-web-opt-story-outputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siteRoot);

        try
        {
            var storyRoot = Path.Combine(siteRoot, "stories", "demo");
            var staged = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(bundleRoot, "source", "story.json"),
                OutputPath = storyRoot
            });
            var bundle = WebVisualStoryStager.Load(staged.ManifestPath);
            var protectedPaths = bundle.Artifacts
                .Select(artifact => Path.Combine(storyRoot, artifact.Path.Replace('/', Path.DirectorySeparatorChar)))
                .Prepend(staged.ManifestPath)
                .ToArray();
            var originalBytes = protectedPaths.ToDictionary(
                path => path,
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(siteRoot, "app.css"), "body { color: blue; }");

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = siteRoot,
                HashAssets = true,
                HashExtensions = [".css"],
                ReportPath = Path.GetRelativePath(siteRoot, staged.ManifestPath),
                HashManifestPath = Path.GetRelativePath(siteRoot, protectedPaths[1]),
                AssetPolicy = new AssetPolicySpec
                {
                    CacheHeaders = new CacheHeadersSpec
                    {
                        Enabled = true,
                        OutputPath = Path.GetRelativePath(siteRoot, protectedPaths[2])
                    }
                }
            });

            Assert.Null(result.ReportPath);
            Assert.Null(result.HashManifestPath);
            Assert.False(result.CacheHeadersWritten);
            foreach (var path in protectedPaths)
                Assert.Equal(originalBytes[path], File.ReadAllBytes(path));
            Assert.Equal(bundle.Artifacts.Length, WebVisualStoryStager.Load(staged.ManifestPath).Artifacts.Length);
        }
        finally
        {
            Directory.Delete(bundleRoot, true);
            Directory.Delete(siteRoot, true);
        }
    }
}
