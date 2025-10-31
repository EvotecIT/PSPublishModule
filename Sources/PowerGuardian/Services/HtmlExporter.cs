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
    public string Export(ModuleInfoModel module, IEnumerable<DocumentItem> items, string? destinationPath, bool open, Action<string>? log = null)
    {
        var list = items?.ToList() ?? new List<DocumentItem>();

        // Build document page per HtmlForgeX examples
        var doc = new Document { ThemeMode = ThemeMode.System, LibraryMode = LibraryMode.Online };
        doc.Body.Page(page => {
            page.Layout = TablerLayout.Fluid;

            // Top: Module information
            log?.Invoke("Adding module information card...");
            page.Row(row => {
                row.Column(TablerColumnNumber.Twelve, col => {
                    col.Card(card => {
                        card.Header(h => h.Title("Module Information"));
                        card.DataGrid(grid => {
                            grid.AddItem("Name", module.Name);
                            grid.AddItem("Version", new TablerBadgeSpan(module.Version ?? string.Empty, TablerColor.Green, TablerBadgeStyle.Pill, TablerColor.White));
                            if (!string.IsNullOrWhiteSpace(module.Description)) grid.AddItem("Description", module.Description!);
                            if (!string.IsNullOrWhiteSpace(module.Author)) grid.AddItem("Author", module.Author!);
                            if (!string.IsNullOrWhiteSpace(module.PowerShellVersion)) grid.AddItem("Min PSVersion", module.PowerShellVersion!);
                            if (!string.IsNullOrWhiteSpace(module.ProjectUri)) grid.AddItem("Project Uri", new HtmlForgeX.Tags.Anchor(module.ProjectUri!, module.ProjectUri!));
                            if (module.RequireLicenseAcceptance.HasValue) grid.AddItem("Requires License Acceptance", module.RequireLicenseAcceptance.Value ? new TablerBadgeSpan("Yes", TablerColor.Red) : new TablerBadgeSpan("No", TablerColor.Green));
                        });
                    });
                });
            });

            // Tabs area inside a card/body using the fluent Tabs() factory
            page.Row(row => {
                row.Column(TablerColumnNumber.Twelve, col => {
                    col.Card(card => {
                        card.Body(body => {
                            body.Tabs(tabs => {
                                // Standard docs (everything except SCRIPT/DOC kinds)
                                foreach (var it in list.Where(x => !string.Equals(x.Kind, "SCRIPT", System.StringComparison.OrdinalIgnoreCase)
                                                             && !string.Equals(x.Kind, "DOC", System.StringComparison.OrdinalIgnoreCase)))
                                {
                                    var title = MakeShortTabTitle(it);
                                    log?.Invoke($"Adding document tab: {title}");
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

                                // Scripts group (nested tabs)
                                var scripts = list.Where(x => string.Equals(x.Kind, "SCRIPT", System.StringComparison.OrdinalIgnoreCase)).ToList();
                                if (scripts.Count > 0)
                                {
                                    log?.Invoke($"Adding Scripts tab with {scripts.Count} items...");
                                    tabs.AddTab("🧩 Scripts", panel =>
                                    {
                                        panel.Tabs(inner => {
                                            foreach (var s in scripts)
                                            {
                                                var name = string.IsNullOrWhiteSpace(s.FileName) ? s.Title : s.FileName;
                                                inner.AddTab(name ?? string.Empty, p =>
                                                {
                                                    var md = s.Content ?? string.Empty; // already fenced with powershell
                                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true };
                                                    p.Markdown(md, options);
                                                });
                                            }
                                        });
                                    });
                                }

                                // Docs group (nested tabs)
                                var docs = list.Where(x => string.Equals(x.Kind, "DOC", System.StringComparison.OrdinalIgnoreCase)).ToList();
                                if (docs.Count > 0)
                                {
                                    log?.Invoke($"Adding Docs tab with {docs.Count} items...");
                                    tabs.AddTab("📚 Docs", panel =>
                                    {
                                        panel.Tabs(inner => {
                                            foreach (var d in docs)
                                            {
                                                var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                                                inner.AddTab(name ?? string.Empty, p =>
                                                {
                                                    var md = d.Content ?? string.Empty;
                                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true };
                                                    p.Markdown(md, options);
                                                });
                                            }
                                        });
                                    });
                                }

                                // Dependencies tab (if any)
                                if (!module.SkipDependencies && module.Dependencies.Count > 0)
                                {
                                    log?.Invoke($"Adding Dependencies tab with {module.Dependencies.Count} rows...");
                                    tabs.AddTab("🔗 Dependencies", panel =>
                                    {
                                        // Direct dependencies table
                                        panel.Card(card2 => {
                                            card2.Header(h => h.Title("Declared Dependencies"));
                                            var groups = module.Dependencies
                                                .GroupBy(d => d.Name, System.StringComparer.OrdinalIgnoreCase)
                                                .Select(g => new {
                                                    Name = g.Key,
                                                    Kinds = string.Join(", ", g.Select(x => x.Kind == ModuleDependencyKind.External ? "External" : "Required").Distinct().OrderBy(k => k == "External" ? 0 : 1)),
                                                    Version = g.Select(x => x.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty,
                                                    Guid = g.Select(x => x.Guid).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty
                                                })
                                                .OrderBy(x => x.Name)
                                                .ToList();
                                            card2.DataTable(groups, t => t.Settings(s => s.Preset(DataTablesPreset.MinimalWithExport)));
                                        });

                                        // Nested dependencies table (if any)
                                        var nested = module.Dependencies.SelectMany(d => d.Children.Select(c => new { Parent = d.Name, Child = c })).ToList();
                                        if (nested.Count > 0)
                                        {
                                            panel.Card(card3 => {
                                                card3.Header(h => h.Title("Nested Dependencies (one level)"));
                                                var rows2 = nested.Select(n => new { Parent = n.Parent, Name = n.Child.Name, Version = n.Child.Version ?? string.Empty, Guid = n.Child.Guid ?? string.Empty }).ToList();
                                                card3.DataTable(rows2, t => t.Settings(s => s.Preset(DataTablesPreset.MinimalWithExport)));
                                            });
                                        }

                                        // Dependency graph
                                        log?.Invoke("Building VisNetwork dependency graph...");
                                        panel.DiagramNetwork(net => {
                                            net.WithSize("100%", "600px").WithPhysics(true);
                                            var added = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                                            void AddNodeSafe(string id, System.Action<VisNetworkNodeOptions> cfg) {
                                                if (added.Add(id)) net.AddNode(id, cfg);
                                            }
                                            // center node
                                            AddNodeSafe(module.Name, node => node.WithLabel(module.Name).WithShape(VisNetworkNodeShape.Box).WithColor(new RGBColor("#2962FF")).WithFont(f => f.WithColor(new RGBColor("#ffffff"))));
                                            foreach (var d in module.Dependencies)
                                            {
                                                var depId = d.Name;
                                                AddNodeSafe(depId, node => node.WithLabel(depId + (string.IsNullOrEmpty(d.Version) ? string.Empty : $"\n{d.Version}")).WithShape(VisNetworkNodeShape.Ellipse));
                                                net.AddEdge(module.Name, depId, edge => edge.WithArrows(new VisNetworkArrowOptions().WithTo(true)));
                                                foreach (var c in d.Children)
                                                {
                                                    var cId = depId + ":" + c.Name;
                                                    AddNodeSafe(cId, node => node.WithLabel(c.Name + (string.IsNullOrEmpty(c.Version) ? string.Empty : $"\n{c.Version}")).WithShape(VisNetworkNodeShape.Dot));
                                                    net.AddEdge(depId, cId, edge => edge.WithArrows(new VisNetworkArrowOptions().WithTo(true)).WithDashes(true));
                                                }
                                            }
                                        });
                                    });
                                }

                                // Commands tab (help per command) using SmartTab vertical layout
                                if (!module.SkipCommands)
                                {
                                    var commands = ResolveExportedCommands(module.Name).Take(Math.Max(0, module.MaxCommands)).ToList();
                                    if (commands.Count > 0)
                                    {
                                        log?.Invoke($"Adding Commands tab with {commands.Count} commands...");
                                        // Precompute help so tabs don't duplicate content and we can log line counts
                                        var helpMap = BuildHelpMap(module.Name, commands, module.HelpTimeoutSeconds, log);
                                        tabs.AddTab("⚙️ Commands", panel =>
                                        {
                                        panel.Tabs(inner => {
                                            foreach (var entry in helpMap)
                                            {
                                                var label = $"{EmojiForCommand(entry.Command)} {entry.Command}".Replace("-", "‑");
                                                inner.AddTab(label, p => {
                                                    var content = entry.Help ?? "No help available.";
                                                    if (module.HelpAsCode)
                                                    {
                                                        var fenced = $"```powershell\n{content}\n```";
                                                        p.Markdown(fenced, new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true });
                                                    }
                                                    else
                                                    {
                                                        p.Markdown(content, new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true });
                                                    }
                                                });
                                            }
                                        });
                                        });
                                    }
                                }
                            });
                        });
                    });
                });
            });
        });


        log?.Invoke("Finalizing HTML...");
        var html = doc.ToString();
        var moduleTitle = module?.Name ?? "Module";
        string path = destinationPath ?? Path.Combine(Path.GetTempPath(), $"{Sanitize(moduleTitle)}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        // If a directory was provided, create a name inside it
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, $"{Sanitize(moduleTitle)}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        }
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, html);

        if (open)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        return path;
    }

    private static string MakeShortTabTitle(DocumentItem it)
    {
        // Prefer file name cues and prepend emoji for recognizable kinds
        var name = (it.FileName ?? it.Title ?? it.Kind ?? string.Empty).ToUpperInvariant();
        if (name.Contains("README")) return "📘 README";
        if (name.Contains("CHANGELOG")) return "📝 CHANGELOG";
        if (name.Contains("LICENSE")) return "📄 LICENSE";
        if (name.Contains("UPGRADE")) return "⬆️ Upgrade";
        if (name.Contains("INTRO")) return "💡 Introduction";
        if (name.Contains("LINKS")) return "🔗 Links";
        return it.Title ?? it.Kind ?? "📄 Document";
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "Document" : s;
    }

    // Helpers for Commands tab
    private List<string> ResolveExportedCommands(string moduleName)
    {
        var ps = System.Management.Automation.PowerShell.Create();
        // Filter only functions/cmdlets (exclude aliases)
        var script = @"
            $m = Get-Module -ListAvailable -Name '" + moduleName + @"' | Sort-Object Version -Descending | Select-Object -First 1
            if ($m) { $m.ExportedCommands.Values | Where-Object { $_.CommandType -in 'Function','Cmdlet' } | Select-Object -ExpandProperty Name }
        ";
        ps.AddScript(script);
        var results = ps.Invoke();
        return results.Select(r => r?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;
    }

    private string? GetHelpMarkdown(string moduleName, string command, int timeoutSeconds)
    {
        var ps = System.Management.Automation.PowerShell.Create();
        // Use -Full for richer content; convert basic sections to markdown headings
        var script = $"$h = Get-Help -Full -Name '{command}' -ErrorAction SilentlyContinue; if ($h) {{ $h | Out-String }} else {{ '' }}";
        ps.AddScript(script);
        var async = ps.BeginInvoke();
        if (!async.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
        {
            try { ps.Stop(); } catch { }
            return "_Get-Help timed out._";
        }
        var text = string.Join("", ps.EndInvoke(async).Select(p => p?.ToString()));
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Naive markdown shaping
        var md = text.Replace("SYNTAX", "# Syntax").Replace("DESCRIPTION", "# Description").Replace("PARAMETERS", "# Parameters").Replace("EXAMPLES", "# Examples");
        return md;
    }

    private List<(string Command, string? Help, int Lines)> BuildHelpMap(string moduleName, List<string> commands, int timeoutSeconds, Action<string>? log)
    {
        var list = new List<(string, string?, int)>();
        foreach (var cmd in commands)
        {
            var helpMd = GetHelpMarkdown(moduleName, cmd, timeoutSeconds);
            var lines = string.IsNullOrEmpty(helpMd) ? 0 : helpMd!.Split(new[] {'\n'}, StringSplitOptions.None).Length;
            log?.Invoke($"Rendering help for {cmd}... ({lines} lines)");
            list.Add((cmd, helpMd, lines));
        }
        return list;
    }

    private static string BuildDependenciesMarkdown(ModuleInfoModel module)
    {
        // Compose a markdown table with Kind, Name, Version, Guid
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Kind | Name | Version | Guid |");
        sb.AppendLine("|:-----|:-----|:--------|:-----|");
        foreach (var d in module.Dependencies)
        {
            var kind = d.Kind == ModuleDependencyKind.External ? "External" : "Required";
            var name = EscapePipe(d.Name);
            var ver  = EscapePipe(d.Version ?? string.Empty);
            var guid = EscapePipe(d.Guid ?? string.Empty);
            sb.AppendLine($"| {kind} | {name} | {ver} | {guid} |");
        }
        return sb.ToString();
    }

    private static string EscapePipe(string s)
    {
        return (s ?? string.Empty).Replace("|", "\\|");
    }

    private static string EmojiForCommand(string name)
    {
        if (string.IsNullOrEmpty(name)) return "⚙️";
        if (name.StartsWith("Get-", StringComparison.OrdinalIgnoreCase)) return "🔎";
        if (name.StartsWith("Set-", StringComparison.OrdinalIgnoreCase)) return "🛠️";
        if (name.StartsWith("New-", StringComparison.OrdinalIgnoreCase)) return "✨";
        if (name.StartsWith("Remove-", StringComparison.OrdinalIgnoreCase)) return "🗑️";
        if (name.StartsWith("Show-", StringComparison.OrdinalIgnoreCase)) return "📄";
        if (name.StartsWith("Install-", StringComparison.OrdinalIgnoreCase)) return "⬇️";
        if (name.StartsWith("Export-", StringComparison.OrdinalIgnoreCase)) return "📤";
        if (name.StartsWith("Import-", StringComparison.OrdinalIgnoreCase)) return "📥";
        if (name.StartsWith("Test-", StringComparison.OrdinalIgnoreCase)) return "🧪";
        if (name.StartsWith("Start-", StringComparison.OrdinalIgnoreCase)) return "▶️";
        if (name.StartsWith("Stop-", StringComparison.OrdinalIgnoreCase)) return "⏹️";
        if (name.StartsWith("Update-", StringComparison.OrdinalIgnoreCase)) return "🔄";
        if (name.StartsWith("Invoke-", StringComparison.OrdinalIgnoreCase)) return "⚡";
        return "⚙️";
    }
}
