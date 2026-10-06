using System;
using System.IO;
using PowerForge.Web;
using PowerForge.Web.Cli;
using Xunit;

public partial class WebPipelineRunnerProjectCatalogProductTests
{
    [Theory]
    [InlineData("alpha", "beta", "/products/shared/", "/products/shared/", true)]
    [InlineData("shared", "shared", "/products/alpha/", "/products/beta/", false)]
    public void Verify_ProductPages_UsesExplicitRoutesForDuplicateDetection(string firstSlug, string secondSlug, string firstRoute, string secondRoute, bool duplicate)
    {
        var root = CreateTestRoot("verify-product-route");
        try
        {
            var contentRoot = Path.Combine(root, "content", "products");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "alpha.md"), $"---\ntitle: Alpha\nslug: {firstSlug}\nmeta:\n  product_page: true\n  product_path: {firstRoute}\n---\n# Alpha\n");
            File.WriteAllText(Path.Combine(contentRoot, "beta.md"), $"---\ntitle: Beta\nslug: {secondSlug}\nmeta:\n  product_page: true\n  product_path: {secondRoute}\n---\n# Beta\n");
            var spec = new SiteSpec { Name = "Example", Collections = new[] { new CollectionSpec { Name = "products", Input = "content/products", Output = "/products" } } };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var result = WebSiteVerifier.Verify(spec, WebSitePlanner.Plan(spec, configPath));
            Assert.Equal(duplicate, System.Linq.Enumerable.Any(result.Errors, error => error.Contains("Duplicate route", StringComparison.Ordinal)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("/products/casaray-desktop/", "products/casaray-desktop/index.html")]
    [InlineData("/catalog/casaray/", "catalog/casaray/index.html")]
    public void RunPipeline_ProjectCatalog_BuildsTheExplicitProductRoute(string route, string output)
    {
        var root = CreateTestRoot("explicit-product-route");
        try
        {
            WriteCatalog(root, PrivateProductCatalog.Replace("\"category\": \"Smart home\",",
                $"\"path\": \"{route}\",\n\"channels\": [{{\"kind\": \"download\", \"url\": \"/downloads/casaray/\"}}],\n\"category\": \"Smart home\",", StringComparison.Ordinal));
            var result = WebPipelineRunner.RunPipeline(WriteProductPagesPipeline(root), logger: null);
            Assert.True(result.Success, result.Steps[0].Message);
            var spec = new SiteSpec
            {
                Name = "Example",
                BaseUrl = "https://example.test",
                Collections = new[] { new CollectionSpec { Name = "products", Input = "content/products", Output = "/products" } },
                StructuredData = new StructuredDataSpec { Enabled = true, SoftwareApplication = true }
            };
            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var outputRoot = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);
            var html = File.ReadAllText(Path.Combine(outputRoot, output.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("https://example.test/downloads/casaray/", html, StringComparison.Ordinal);
            Assert.Contains("downloadUrl", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
