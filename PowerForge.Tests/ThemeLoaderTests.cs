using System.Text.Json;
using PowerForge.Web;

public class ThemeLoaderTests
{
    [Fact]
    public void Load_MergesTokensAndResolvesBaseLayouts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-theme-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var baseRoot = Path.Combine(root, "base");
            var childRoot = Path.Combine(root, "child");
            Directory.CreateDirectory(Path.Combine(baseRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(childRoot, "layouts"));
            Directory.CreateDirectory(Path.Combine(childRoot, "partials"));

            File.WriteAllText(Path.Combine(baseRoot, "layouts", "page.html"), "<html>{{CONTENT}}</html>");
            File.WriteAllText(Path.Combine(childRoot, "partials", "header.html"), "<header>child</header>");

            File.WriteAllText(Path.Combine(baseRoot, "theme.json"), JsonSerializer.Serialize(new
            {
                name = "base",
                engine = "simple",
                layoutsPath = "layouts",
                partialsPath = "partials",
                scriptsPath = "scripts",
                tokens = new
                {
                    color = new { bg = "#000" },
                    radius = new { baseValue = "12px" }
                }
            }));

            File.WriteAllText(Path.Combine(childRoot, "theme.json"), JsonSerializer.Serialize(new
            {
                name = "child",
                extends = "base",
                layoutsPath = "layouts",
                partialsPath = "partials",
                tokens = new
                {
                    color = new { bg = "#111", accent = "#fff" }
                }
            }));

            var loader = new ThemeLoader();
            var manifest = loader.Load(childRoot, root);

            Assert.NotNull(manifest);
            Assert.Equal("child", manifest!.Name);
            Assert.NotNull(manifest.Base);

            var layout = loader.ResolveLayoutPath(childRoot, manifest, "page");
            Assert.Equal(Path.Combine(baseRoot, "layouts", "page.html"), layout);

            var partial = loader.ResolvePartialPath(childRoot, manifest, "header");
            Assert.Equal(Path.Combine(childRoot, "partials", "header.html"), partial);

            Assert.NotNull(manifest.Tokens);
            var tokens = manifest.Tokens!;
            var color = Assert.IsType<Dictionary<string, object?>>(tokens["color"]);
            Assert.Equal("#111", color["bg"]?.ToString());
            Assert.Equal("#fff", color["accent"]?.ToString());
            Assert.True(tokens.ContainsKey("radius"));
            Assert.Equal("scripts", manifest.ScriptsPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_PrefersContractManifestOverLegacyThemeJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-theme-manifest-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "theme.json"), JsonSerializer.Serialize(new
            {
                name = "legacy",
                engine = "simple"
            }));

            File.WriteAllText(Path.Combine(root, "theme.manifest.json"), JsonSerializer.Serialize(new
            {
                name = "contract",
                engine = "scriban",
                contractVersion = 2
            }));

            var loader = new ThemeLoader();
            var manifest = loader.Load(root);

            Assert.NotNull(manifest);
            Assert.Equal("contract", manifest!.Name);
            Assert.Equal("scriban", manifest.Engine);
            Assert.Equal(2, manifest.ContractVersion);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
