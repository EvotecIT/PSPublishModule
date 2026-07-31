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

    [Fact]
    public void OptimizeDetailed_DoesNotOverwriteProtectedStoriesWithGeneratedImageVariants()
    {
        var bundleRoot = WebVisualStoryStagerTests.CreateBundle();
        var siteRoot = Path.Combine(Path.GetTempPath(), "pf-web-opt-story-variants-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siteRoot);

        try
        {
            var sourceRoot = Path.Combine(bundleRoot, "source");
            var sourceManifest = Path.Combine(sourceRoot, "story.json");
            var bundle = System.Text.Json.JsonSerializer.Deserialize<WebVisualStoryBundle>(
                File.ReadAllText(sourceManifest),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            File.Copy(Path.Combine(sourceRoot, "demo.png"), Path.Combine(sourceRoot, "hero.w640.png"));
            bundle.Artifacts = bundle.Artifacts
                .Append(new WebVisualStoryArtifact { Role = "source", Format = "png", Path = "hero.w640.png" })
                .ToArray();
            File.WriteAllText(
                sourceManifest,
                System.Text.Json.JsonSerializer.Serialize(
                    bundle,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            var staged = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = siteRoot
            });
            var protectedVariant = Path.Combine(siteRoot, "hero.w640.png");
            var originalProtectedBytes = File.ReadAllBytes(protectedVariant);
            using (var sourceImage = new ImageMagick.MagickImage(ImageMagick.MagickColors.CornflowerBlue, 1200, 800))
            {
                sourceImage.Write(Path.Combine(siteRoot, "hero.png"), ImageMagick.MagickFormat.Png);
            }

            var result = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = siteRoot,
                ResponsiveImageWidths = [640]
            });

            Assert.Equal(originalProtectedBytes, File.ReadAllBytes(protectedVariant));
            Assert.DoesNotContain(result.GeneratedImageVariants, variant =>
                string.Equals(variant.VariantPath, "hero.w640.png", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(bundle.Artifacts.Length, WebVisualStoryStager.Load(staged.ManifestPath).Artifacts.Length);
        }
        finally
        {
            Directory.Delete(bundleRoot, true);
            Directory.Delete(siteRoot, true);
        }
    }
}
