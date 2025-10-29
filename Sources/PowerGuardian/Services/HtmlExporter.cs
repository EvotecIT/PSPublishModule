using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HtmlForgeX;
using HtmlForgeX.Markdown;
using HtmlForgeX.Extensions;

namespace PowerGuardian;

internal sealed class HtmlExporter
{
    public string Export(string moduleTitle, IEnumerable<DocumentItem> items, string? destinationPath, bool open)
    {
        var list = items?.ToList() ?? new List<DocumentItem>();
        if (list.Count == 0) throw new InvalidOperationException("No documents to export.");

        // Build a simple document with Tabler tabs
        var doc = new Document();
        doc.ThemeMode = ThemeMode.System;
        // use TablerTabs directly in body
        var tabs = new TablerTabs();
        doc.Body.Add(tabs);

        int idx = 0;
        foreach (var it in list)
        {
            var title = string.IsNullOrWhiteSpace(it.Title) ? it.Kind : it.Title;
            tabs.AddTab(title, panel =>
            {
                var md = it.Content ?? string.Empty;
                var options = new MarkdownOptions
                {
                    HeadingsBaseLevel = 2,
                    TableMode = MarkdownTableMode.Plain,
                    OpenLinksInNewTab = true,
                    Sanitize = true,
                    AutolinkBareUrls = true,
                    AllowRelativeLinks = true,
                };
                panel.Markdown(md, options);
            });
        }
        var html = doc.ToString();
        string path = destinationPath ?? Path.Combine(Path.GetTempPath(), $"{Sanitize(moduleTitle)}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        // If a directory was provided, create a name inside it
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, $"{Sanitize(moduleTitle)}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, html);

        if (open)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        return path;
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Document" : s;
    }
}
