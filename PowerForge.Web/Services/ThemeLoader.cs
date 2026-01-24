using System.Text.Json;

namespace PowerForge.Web;

internal sealed class ThemeLoader
{
    public ThemeManifest? Load(string themeRoot)
    {
        if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            return null;

        var manifestPath = Path.Combine(themeRoot, "theme.json");
        if (!File.Exists(manifestPath))
            return new ThemeManifest { Name = Path.GetFileName(themeRoot) };

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, WebJson.Options);
        if (manifest is null)
            return new ThemeManifest { Name = Path.GetFileName(themeRoot) };

        if (string.IsNullOrWhiteSpace(manifest.Name))
            manifest.Name = Path.GetFileName(themeRoot);

        return manifest;
    }

    public string? ResolveLayoutPath(string themeRoot, ThemeManifest? manifest, string layoutName)
    {
        if (string.IsNullOrWhiteSpace(themeRoot)) return null;
        var layoutsDir = manifest?.LayoutsPath ?? "layouts";
        var fileName = layoutName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? layoutName
            : layoutName + ".html";
        var layoutPath = Path.Combine(themeRoot, layoutsDir, fileName);
        return File.Exists(layoutPath) ? layoutPath : null;
    }

    public string? ResolvePartialPath(string themeRoot, ThemeManifest? manifest, string partialName)
    {
        if (string.IsNullOrWhiteSpace(themeRoot)) return null;
        var partialsDir = manifest?.PartialsPath ?? "partials";
        var fileName = partialName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? partialName
            : partialName + ".html";
        var partialPath = Path.Combine(themeRoot, partialsDir, fileName);
        return File.Exists(partialPath) ? partialPath : null;
    }
}
