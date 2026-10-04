using System.Security.Cryptography;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebBlazorPublishFixerStylesheetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-blazor-css-" + Guid.NewGuid().ToString("N"));

    public WebBlazorPublishFixerStylesheetTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void UpgradeRequestsNewStylesheetWhenImportedBundleChanges()
    {
        var site = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(site);
        var css = Path.Combine(site, "App.styles.css");
        File.WriteAllText(Path.Combine(site, "index.html"), "<link rel=\"stylesheet\" href=\"App.styles.css\"><script src=\"_framework/dotnet.hash.js\"></script>");
        File.WriteAllText(css, "@import '_content/Component/old.bundle.scp.css';");
        Apply();
        var before = File.ReadAllText(Path.Combine(site, "index.html"));
        Assert.Contains("App.styles.css?v=" + Hash(css), before);
        File.WriteAllText(css, "@import '_content/Component/new.bundle.scp.css';");
        Apply();
        var after = File.ReadAllText(Path.Combine(site, "index.html"));
        Assert.Contains("App.styles.css?v=" + Hash(css), after);
        Assert.NotEqual(before, after);
        Assert.Contains("src=\"_framework/dotnet.hash.js\"", after);
    }

    [Fact]
    public void RepeatedApplyPreservesQueryFragmentAndStylesheetBytes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "css"));
        var css = Path.Combine(_root, "css", "theme.css");
        File.WriteAllText(css, "body{color:purple}");
        var bytes = File.ReadAllBytes(css);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<LINK href='css/theme.css?language=en&amp;v=old#theme' media='screen' REL='alternate stylesheet'>");
        Apply();
        var once = File.ReadAllText(Path.Combine(_root, "index.html"));
        Apply();
        Assert.Equal(once, File.ReadAllText(Path.Combine(_root, "index.html")));
        Assert.Contains("language=en&amp;v=" + Hash(css) + "#theme", once);
        Assert.Equal(bytes, File.ReadAllBytes(css));
    }

    [Fact]
    public void LeavesExternalMissingAndEscapingReferencesUnchanged()
    {
        var site = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "local.css"), "body{}");
        File.WriteAllText(Path.Combine(_root, "outside.css"), "body{color:red}");
        var html = "<link rel='stylesheet' href='https://example.com/style.css'>"
            + "<link rel='stylesheet' href='/local.css'><link rel='stylesheet' href='//example.com/style.css'>"
            + "<link rel='stylesheet' href='missing.css'><link rel='stylesheet' href='../outside.css'>"
            + "<link rel='stylesheet' href='%2e%2e/outside.css'>"
            + "<link rel='preload' href='local.css'><link data-rel='stylesheet' href='local.css'>"
            + "<link rel='stylesheet' data-href='local.css'>";
        File.WriteAllText(Path.Combine(site, "index.html"), html);
        Apply();
        Assert.Equal(html, File.ReadAllText(Path.Combine(site, "index.html")));
    }

    [Fact]
    public void CacheBustingCanBeDisabled()
    {
        File.WriteAllText(Path.Combine(_root, "local.css"), "body{}");
        const string html = "<link rel='stylesheet' href='local.css'>";
        File.WriteAllText(Path.Combine(_root, "index.html"), html);
        WebBlazorPublishFixer.Apply(new() { PublishRoot = _root, AddCacheBuster = false });
        Assert.Equal(html, File.ReadAllText(Path.Combine(_root, "index.html")));
    }

    private void Apply() => WebBlazorPublishFixer.Apply(new() { PublishRoot = _root });
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
