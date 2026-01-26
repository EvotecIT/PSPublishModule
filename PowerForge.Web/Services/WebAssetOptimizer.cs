using System.Text.RegularExpressions;
using HtmlTinkerX;

namespace PowerForge.Web;

public sealed class WebAssetOptimizerOptions
{
    public string SiteRoot { get; set; } = ".";
    public string? CriticalCssPath { get; set; }
    public string CssLinkPattern { get; set; } = "(app|api-docs)\\.css";
    public bool MinifyHtml { get; set; } = false;
    public bool MinifyCss { get; set; } = false;
    public bool MinifyJs { get; set; } = false;
}

public static class WebAssetOptimizer
{
    public static int Optimize(WebAssetOptimizerOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        var siteRoot = Path.GetFullPath(options.SiteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var htmlFiles = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).ToArray();
        var criticalCss = LoadCriticalCss(options.CriticalCssPath);
        var cssPattern = new Regex(options.CssLinkPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var processed = 0;
        foreach (var htmlFile in htmlFiles)
        {
            var content = File.ReadAllText(htmlFile);
            if (string.IsNullOrWhiteSpace(content)) continue;

            if (!string.IsNullOrWhiteSpace(criticalCss) && !content.Contains("<!-- critical-css -->", StringComparison.OrdinalIgnoreCase))
            {
                var updated = InlineCriticalCss(content, criticalCss, cssPattern);
                if (!string.Equals(updated, content, StringComparison.Ordinal))
                {
                    File.WriteAllText(htmlFile, updated);
                    processed++;
                }
            }
        }

        if (options.MinifyHtml)
        {
            foreach (var htmlFile in htmlFiles)
            {
                var html = File.ReadAllText(htmlFile);
                if (string.IsNullOrWhiteSpace(html)) continue;
                string? minified = null;
                try
                {
                    minified = HtmlOptimizer.OptimizeHtml(html, cssDecodeEscapes: true, treatAsDocument: true);
                }
                catch
                {
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(html, minified, StringComparison.Ordinal))
                    File.WriteAllText(htmlFile, minified);
            }
        }

        if (options.MinifyCss)
        {
            foreach (var cssFile in Directory.EnumerateFiles(siteRoot, "*.css", SearchOption.AllDirectories))
            {
                var css = File.ReadAllText(cssFile);
                if (string.IsNullOrWhiteSpace(css)) continue;
                string? minified = null;
                try
                {
                    minified = HtmlOptimizer.OptimizeCss(css);
                }
                catch
                {
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(css, minified, StringComparison.Ordinal))
                    File.WriteAllText(cssFile, minified);
            }
        }

        if (options.MinifyJs)
        {
            foreach (var jsFile in Directory.EnumerateFiles(siteRoot, "*.js", SearchOption.AllDirectories))
            {
                var js = File.ReadAllText(jsFile);
                if (string.IsNullOrWhiteSpace(js)) continue;
                string? minified = null;
                try
                {
                    minified = HtmlOptimizer.OptimizeJavaScript(js);
                }
                catch
                {
                    minified = null;
                }
                if (!string.IsNullOrWhiteSpace(minified) && !string.Equals(js, minified, StringComparison.Ordinal))
                    File.WriteAllText(jsFile, minified);
            }
        }

        return processed;
    }

    private static string InlineCriticalCss(string content, string criticalCss, Regex cssPattern)
    {
        var linkRegex = new Regex("<link\\s+rel=\"stylesheet\"\\s+href=\"([^\"]+)\"\\s*/?>", RegexOptions.IgnoreCase);
        var match = linkRegex.Match(content);
        if (!match.Success) return content;

        var href = match.Groups[1].Value;
        if (!cssPattern.IsMatch(href)) return content;

        var asyncCss = $"<!-- critical-css -->\n<style>{criticalCss}</style>\n<link rel=\"preload\" href=\"{href}\" as=\"style\" onload=\"this.onload=null;this.rel='stylesheet'\">\n<noscript><link rel=\"stylesheet\" href=\"{href}\"></noscript>";
        return content.Replace(match.Value, asyncCss);
    }

    private static string LoadCriticalCss(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        var css = File.ReadAllText(full);
        try
        {
            var optimized = HtmlOptimizer.OptimizeCss(css);
            return string.IsNullOrWhiteSpace(optimized) ? css : optimized;
        }
        catch
        {
            return css;
        }
    }

}
