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
                                var standard = list.Where(x => !string.Equals(x.Kind, "SCRIPT", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "DOC", System.StringComparison.OrdinalIgnoreCase))
                                                   .ToList();

                                // If both local and remote versions of README/CHANGELOG/LICENSE exist,
                                // annotate local copies with "(local)" for clarity.
                                try
                                {
                                    string BaseLabel(string t)
                                    {
                                        if (string.IsNullOrEmpty(t)) return t;
                                        var u = t.Trim();
                                        if (u.StartsWith("README", StringComparison.OrdinalIgnoreCase)) return "README";
                                        if (u.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)) return "CHANGELOG";
                                        if (u.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)) return "LICENSE";
                                        return u;
                                    }
                                    var groups = standard.GroupBy(s => BaseLabel(s.Title ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                                    foreach (var g in groups)
                                    {
                                        if (g.Key == "README" || g.Key == "CHANGELOG" || g.Key == "LICENSE")
                                        {
                                            bool hasRemote = g.Any(x => string.Equals(x.Source, "Remote", StringComparison.OrdinalIgnoreCase));
                                            if (hasRemote)
                                            {
                                                foreach (var x in g)
                                                {
                                                    var title = x.Title ?? string.Empty;
                                                    bool isRemote = string.Equals(x.Source, "Remote", StringComparison.OrdinalIgnoreCase);
                                                    if (!isRemote && title.IndexOf("(local)", StringComparison.OrdinalIgnoreCase) < 0)
                                                    {
                                                        x.Title = title + " (local)";
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    log?.Invoke($"[WARN] Document labelling failed: {ex.Message}");
                                }
                                // Do not append (root)/(internals) suffixes; upstream selection ensures a single local copy.

                                foreach (var it in standard)
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
                                            AllowRawHtmlInline = true,
                                            AllowRawHtmlBlocks = true,
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
                                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true };
                                                    p.Markdown(md, options);
                                                });
                                            }
                                        });
                                    });
                                }

                                // Docs group (nested tabs)
        var docsAll = list.Where(x => string.Equals(x.Kind, "DOC", System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (docsAll.Count > 0)
        {
            var docsLocal = docsAll.Where(x => string.Equals(x.Source, "Local", StringComparison.OrdinalIgnoreCase)).ToList();
            var docsRepo  = docsAll.Where(x => string.Equals(x.Source, "Remote", StringComparison.OrdinalIgnoreCase)).ToList();
            log?.Invoke($"Adding Documentation tab with {docsAll.Count} items (Local={docsLocal.Count}, Repo={docsRepo.Count})...");
            tabs.AddTab("📚 Documentation", panel =>
            {
                // If both present, split into subtabs; otherwise render directly
                if (docsLocal.Count > 0 && docsRepo.Count > 0)
                {
                    panel.Tabs(group => {
                        group.AddTab("📄 Local", p =>
                        {
                            var inner = new TablerTabs();
                            p.Add(inner);
                            foreach (var d in docsLocal)
                            {
                                var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                                inner.AddTab(name ?? string.Empty, pp =>
                                {
                                    var md = d.Content ?? string.Empty;
                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true };
                                    pp.Markdown(md, options);
                                });
                            }
                        });
                        group.AddTab("🌐 Repository", p =>
                        {
                            var inner = new TablerTabs();
                            p.Add(inner);
                            foreach (var d in docsRepo)
                            {
                                var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                                inner.AddTab(name ?? string.Empty, pp =>
                                {
                                    var md = d.Content ?? string.Empty;
                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true };
                                    pp.Markdown(md, options);
                                });
                            }
                        });
                    });
                }
                else
                {
                    var render = docsLocal.Count > 0 ? docsLocal : docsRepo;
                    var inner = new TablerTabs();
                    panel.Add(inner);
                    foreach (var d in render)
                    {
                        var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                        inner.AddTab(name ?? string.Empty, pp =>
                        {
                            var md = d.Content ?? string.Empty;
                            var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true };
                            pp.Markdown(md, options);
                        });
                    }
                }
            });
        }

                                // Dependencies tab (if any)
                                if (!module.SkipDependencies && module.Dependencies.Count > 0)
                                {
                                    log?.Invoke($"Adding Dependencies tab with {module.Dependencies.Count} rows...");
                                    tabs.AddTab("🔗 Dependencies", panel =>
                                    {
                                        panel.Tabs(depTabs => {
                                            // Declared (combined) table
                                            depTabs.AddTab("📋 Declared", p => {
                                                var rootRow = new [] { new {
                                                    Relation = "Root",
                                                    Level = 0,
                                                    Parent = string.Empty,
                                                    Name = module.Name,
                                                    Kinds = "",
                                                    Version = module.Version ?? string.Empty,
                                                    Guid = string.Empty
                                                }};

                                                var direct = module.Dependencies
                                                    .GroupBy(d => d.Name, System.StringComparer.OrdinalIgnoreCase)
                                                    .Select(g => new {
                                                        Relation = "Direct",
                                                        Level = 1,
                                                        Parent = module.Name,
                                                        Name = g.Key,
                                                        Kinds = string.Join(", ", g.Select(x => x.Kind == ModuleDependencyKind.External ? "External" : "Required").Distinct().OrderBy(k => k == "External" ? 0 : 1)),
                                                        Version = g.Select(x => x.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty,
                                                        Guid = g.Select(x => x.Guid).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty
                                                    });

                                                var nested = module.Dependencies
                                                    .SelectMany(d => d.Children.Select(c => new {
                                                        Relation = "Nested",
                                                        Level = 2,
                                                        Parent = d.Name,
                                                        Name = c.Name,
                                                        Kinds = "Required",
                                                        Version = c.Version ?? string.Empty,
                                                        Guid = c.Guid ?? string.Empty
                                                    }));

                                                var rows = rootRow.Concat(direct).Concat(nested)
                                                    .OrderBy(r => r.Level)
                                                    .ThenBy(r => r.Parent)
                                                    .ThenBy(r => r.Name)
                                                    .ToList();

                                                p.Card(c => {
                                                    c.Header(h => h.Title("Declared Dependencies"));
                                                    c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.MinimalWithExport)));
                                                });
                                            });

                                            // Diagram
                                            depTabs.AddTab("🗺️ Diagram", p => {
                                                log?.Invoke("Building VisNetwork dependency graph...");
                                                p.DiagramNetwork(net => {
                                                    net.WithSize("100%", "900px")
                                                       .WithHierarchicalLayout(VisNetworkLayoutDirection.Ud)
                                                       .WithPhysics(false)
                                                       .WithOptions(opt => {
                                                           opt.WithLayout(layout => layout.WithHierarchical(new VisNetworkHierarchicalOptions().WithLevelSeparation(200)));
                                                           opt.WithNodes(n => n.WithShape(VisNetworkNodeShape.Box).WithBorderWidth(2));
                                                           opt.WithEdges(e => e.WithArrows(new VisNetworkArrowOptions().WithTo(true)));
                                                       });

                                                    var added = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                                                    void AddNodeSafe(string id, System.Action<VisNetworkNodeOptions> cfg) { if (added.Add(id)) net.AddNode(id, cfg); }

                                                    AddNodeSafe(module.Name, node => node.WithLabel(module.Name).WithLevel(0).WithColor(new RGBColor("#2962FF")).WithFont(f => f.WithColor(new RGBColor("#ffffff"))));
                                                    foreach (var d in module.Dependencies)
                                                    {
                                                        var depId = d.Name;
                                                        AddNodeSafe(depId, node => node.WithLabel(depId + (string.IsNullOrEmpty(d.Version) ? string.Empty : $"\n{d.Version}")).WithLevel(1));
                                                        net.AddEdge(module.Name, depId);
                                                        foreach (var c in d.Children)
                                                        {
                                                            var childId = c.Name;
                                                            AddNodeSafe(childId, node => node.WithLabel(c.Name + (string.IsNullOrEmpty(c.Version) ? string.Empty : $"\n{c.Version}")).WithLevel(2));
                                                            net.AddEdge(depId, childId, edge => edge.WithDashes(true));
                                                        }
                                                    }
                                                });
                                            });
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
                                                    if (entry.Model != null)
                                                    {
                                                        RenderHelpPanel(p, entry.Model);
                                                    }
                                                    else
                                                    {
                                                        var content = entry.Help ?? "No help available.";
                                                        if (module.HelpAsCode)
                                                        {
                                                            var fenced = $"```powershell\n{content}\n```";
                                                            p.Markdown(fenced, new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                                                        }
                                                        else
                                                        {
                                                            p.Markdown(content, new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                                                        }
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
        // Prefer file name cues and prepend emoji for recognizable kinds.
        // Preserve (local) suffix when present. Ignore any legacy (remote) suffixes in title.
        var basis = it.FileName ?? it.Title ?? it.Kind ?? string.Empty;
        var upper = basis.ToUpperInvariant();
        string SuffixFromTitle()
        {
            var t = (it.Title ?? string.Empty).Trim();
            var idx = t.IndexOf('(');
            if (idx >= 0)
            {
                var suf = t.Substring(idx);
                if (suf.IndexOf("(remote)", StringComparison.OrdinalIgnoreCase) >= 0) return string.Empty;
                return " " + suf; // include leading space
            }
            return string.Empty;
        }
        if (upper.Contains("README")) return "📘 README" + SuffixFromTitle();
        if (upper.Contains("CHANGELOG")) return "📝 CHANGELOG" + SuffixFromTitle();
        if (upper.Contains("LICENSE")) return "📄 LICENSE" + SuffixFromTitle();
        if (upper.Contains("UPGRADE")) return "⬆️ Upgrade" + SuffixFromTitle();
        if (upper.Contains("INTRO")) return "💡 Introduction" + SuffixFromTitle();
        if (upper.Contains("LINKS")) return "🔗 Links";
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

    private List<(string Command, CommandHelpModel? Model, string? Help, int Lines)> BuildHelpMap(string moduleName, List<string> commands, int timeoutSeconds, Action<string>? log)
    {
        var list = new List<(string, CommandHelpModel?, string?, int)>();
        var parser = new GetHelpParser();
        foreach (var cmd in commands)
        {
            string? content = null;
            // Prefer structured parse
            var model = parser.Parse(cmd, timeoutSeconds);
            if (model != null)
            {
                content = null; // render with components
            }
            else
            {
                // Fallback to raw Get-Help text -> markdown
                content = GetHelpMarkdown(moduleName, cmd, timeoutSeconds);
            }
            var lines = string.IsNullOrEmpty(content) ? 0 : content!.Split(new[] {'\n'}, StringSplitOptions.None).Length;
            if (model != null)
            {
                var sets = model.Syntax?.Count ?? 0;
                var pcount = model.Parameters?.Count ?? 0;
                var ex = model.Examples?.Count ?? 0;
                log?.Invoke($"Rendering help for {cmd}... (structured: sets={sets}, params={pcount}, examples={ex})");
            }
            else
            {
                log?.Invoke($"Rendering help for {cmd}... ({lines} lines)");
            }
            list.Add((cmd, model, content, lines));
        }
        return list;
    }

    private static void RenderHelpPanel(TablerTabsPanel panel, CommandHelpModel m)
    {
        if (!string.IsNullOrWhiteSpace(m.Synopsis))
        {
            panel.Markdown("## Synopsis", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.Markdown(m.Synopsis.Trim(), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.LineBreak();
        }
        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            panel.Markdown("## Description", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.Markdown(m.Description.Trim(), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.LineBreak();
        }
        if (m.Syntax.Count > 0)
        {
            panel.Markdown("## Syntax", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            var total = m.Syntax.Count;
            for (int i = 0; i < total; i++)
            {
                var s = m.Syntax[i];
                panel.Markdown($"### Syntax — Set {i + 1} of {total}", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                var parts = new System.Collections.Generic.List<string>();
                foreach (var p in s.Parameters)
                {
                    var name = p.Name;
                    var type = string.IsNullOrEmpty(p.Type) ? string.Empty : $" <{p.Type}>";
                    var token = $"-{name}{type}";
                    if (!(p.Required ?? false)) token = $"[{token}]";
                    parts.Add(token);
                }
                var line = (s.Name ?? m.Name) + (parts.Count > 0 ? (" " + string.Join(" ", parts)) : string.Empty);
                var fenced = $"```powershell\n{line}\n```";
                panel.Markdown(fenced, new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            }
            panel.LineBreak();
        }
        if (m.Parameters.Count > 0)
        {
            var byName = m.Parameters.ToDictionary(x => x.Name, System.StringComparer.OrdinalIgnoreCase);
            var total = m.Syntax.Count;
            for (int i = 0; i < System.Math.Max(1, total); i++)
            {
                var set = total > 0 ? m.Syntax[i] : null;
                var rows = new System.Collections.Generic.List<object>();
                System.Collections.Generic.IEnumerable<ParameterHelp> plist = m.Parameters;
                if (set != null)
                {
                    var names = set.Parameters.Select(p => p.Name).Distinct(System.StringComparer.OrdinalIgnoreCase);
                    plist = names.Select(n => byName.TryGetValue(n, out var ph) ? ph : new ParameterHelp { Name = n });
                }
                foreach (var p in plist)
                {
                    rows.Add(new {
                        Name = p.Name,
                        Type = p.Type ?? string.Empty,
                        Required = p.Required.HasValue ? (p.Required.Value ? "Yes" : "No") : string.Empty,
                        Position = p.Position ?? string.Empty,
                        Pipeline = p.PipelineInput ?? string.Empty,
                        Wildcards = p.Globbing.HasValue ? (p.Globbing.Value ? "Yes" : "No") : string.Empty,
                        Default = p.DefaultValue ?? string.Empty,
                        Aliases = (p.Aliases == null || p.Aliases.Count == 0) ? string.Empty : string.Join(", ", p.Aliases),
                        Description = p.Description ?? string.Empty
                    });
                }
                panel.Card(c => {
                    c.Header(h => h.Title(total > 0 ? $"Parameters — Set {i + 1} of {total}" : "Parameters"));
                    c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.MinimalWithExport)));
                });
            }
        }
        if (m.Inputs.Count > 0)
        {
            var rows = m.Inputs.Select(t => new { Type = t.TypeName, Description = t.Description ?? string.Empty }).ToList();
            panel.Card(c => { c.Header(h => h.Title("Inputs")); c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.MinimalWithExport))); });
        }
        if (m.Outputs.Count > 0)
        {
            var rows = m.Outputs.Select(t => new { Type = t.TypeName, Description = t.Description ?? string.Empty }).ToList();
            panel.Card(c => { c.Header(h => h.Title("Outputs")); c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.MinimalWithExport))); });
        }
        if (m.Examples.Count > 0)
        {
            panel.Markdown("## Examples", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            int idx = 1;
            foreach (var ex in m.Examples)
            {
                var title = NormalizeExampleTitle(ex.Title, idx);
                panel.Markdown($"### {title}", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                if (!string.IsNullOrWhiteSpace(ex.Code))
                {
                    var fenced = $"```powershell\n{ex.Code.Trim()}\n```";
                    panel.Markdown(fenced, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                }
                if (!string.IsNullOrWhiteSpace(ex.Remarks)) panel.Text(ex.Remarks!.Trim());
                panel.LineBreak();
                idx++;
            }
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            panel.Markdown("## Notes", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.Markdown(m.Notes!.Trim(), new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
        }
        if (m.RelatedLinks.Count > 0)
        {
            panel.Markdown("## Related Links", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            var md = string.Join("\n", m.RelatedLinks.Select(l => !string.IsNullOrEmpty(l.Uri) ? $"- [{l.Title}]({l.Uri})" : $"- {l.Title}"));
            panel.Markdown(md, new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
        }
    }

    private static string NormalizeExampleTitle(string raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw)) return $"Example {index}";
        var t = raw.Trim();
        t = t.Trim('-').Trim();
        if (t.Length == 0) return $"Example {index}";
        return t;
    }

    private static string RenderHelpMarkdown(CommandHelpModel m)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(m.Synopsis))
        {
            sb.AppendLine("## Synopsis");
            sb.AppendLine(m.Synopsis.Trim());
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            sb.AppendLine("## Description");
            sb.AppendLine(m.Description.Trim());
            sb.AppendLine();
        }
        if (m.Syntax.Count > 0)
        {
            sb.AppendLine("## Syntax");
            var total = m.Syntax.Count;
            for (int i = 0; i < total; i++)
            {
                var s = m.Syntax[i];
                // Friendly preface between blocks
                sb.AppendLine($"_Parameter set {i + 1} of {total}_");
                var parts = new List<string>();
                foreach (var p in s.Parameters)
                {
                    var name = p.Name;
                    var type = string.IsNullOrEmpty(p.Type) ? string.Empty : $" <{p.Type}>";
                    var token = $"-{name}{type}";
                    if (!(p.Required ?? false)) token = $"[{token}]";
                    parts.Add(token);
                }
                var line = (s.Name ?? m.Name) + (parts.Count > 0 ? (" " + string.Join(" ", parts)) : string.Empty);
                sb.AppendLine("```powershell");
                sb.AppendLine(line);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        if (m.Parameters.Count > 0)
        {
            sb.AppendLine("## Parameters");
            sb.AppendLine("| Name | Type | Required | Position | Pipeline | Wildcards | Default | Aliases | Description |");
            sb.AppendLine("|:-----|:-----|:--------:|:--------:|:--------:|:---------:|:--------|:--------|:-----------|");
            foreach (var p in m.Parameters)
            {
                var name = $"-{p.Name}";
                var type = string.IsNullOrWhiteSpace(p.Type) ? "" : p.Type.Replace("|", "\\|");
                var req = p.Required.HasValue ? (p.Required.Value ? "Yes" : "No") : "";
                var pos = string.IsNullOrWhiteSpace(p.Position) ? "" : p.Position;
                var pipe = ((p.PipelineInput ?? string.Empty)).Replace("|", "\\|");
                var wc = p.Globbing.HasValue ? (p.Globbing.Value ? "Yes" : "No") : "";
                var def = ((p.DefaultValue ?? string.Empty)).Replace("|", "\\|");
                var aliases = (p.Aliases == null || p.Aliases.Count == 0) ? "" : string.Join(", ", p.Aliases).Replace("|", "\\|");
                var desc = string.IsNullOrWhiteSpace(p.Description) ? "" : p.Description!.Replace("\n", " ").Replace("|", "\\|");
                sb.AppendLine($"| {name} | {type} | {req} | {pos} | {pipe} | {wc} | {def} | {aliases} | {desc} |");
            }
            sb.AppendLine();
        }
        if (m.Examples.Count > 0)
        {
            sb.AppendLine("## Examples");
            int i = 1;
            foreach (var ex in m.Examples)
            {
                var title = string.IsNullOrWhiteSpace(ex.Title) ? $"Example {i}" : ex.Title.Trim();
                sb.AppendLine($"### {title}");
                if (!string.IsNullOrWhiteSpace(ex.Code))
                {
                    sb.AppendLine("```powershell");
                    sb.AppendLine(ex.Code.Trim());
                    sb.AppendLine("```");
                }
                if (!string.IsNullOrWhiteSpace(ex.Remarks)) sb.AppendLine(ex.Remarks!.Trim());
                sb.AppendLine();
                i++;
            }
        }
        if (m.Inputs.Count > 0)
        {
            sb.AppendLine("## Inputs");
            sb.AppendLine("| Type | Description |");
            sb.AppendLine("|:-----|:------------|");
            foreach (var t in m.Inputs)
            {
                var type = (t.TypeName ?? string.Empty).Replace("|", "\\|");
                var desc = string.IsNullOrWhiteSpace(t.Description) ? "" : t.Description!.Replace("|", "\\|");
                sb.AppendLine($"| {type} | {desc} |");
            }
            sb.AppendLine();
        }
        if (m.Outputs.Count > 0)
        {
            sb.AppendLine("## Outputs");
            sb.AppendLine("| Type | Description |");
            sb.AppendLine("|:-----|:------------|");
            foreach (var t in m.Outputs)
            {
                var type = (t.TypeName ?? string.Empty).Replace("|", "\\|");
                var desc = string.IsNullOrWhiteSpace(t.Description) ? "" : t.Description!.Replace("|", "\\|");
                sb.AppendLine($"| {type} | {desc} |");
            }
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            sb.AppendLine("## Notes");
            sb.AppendLine(m.Notes!.Trim());
            sb.AppendLine();
        }
        if (m.RelatedLinks.Count > 0)
        {
            sb.AppendLine("## Related Links");
            foreach (var l in m.RelatedLinks)
            {
                if (!string.IsNullOrEmpty(l.Uri)) sb.AppendLine($"- [{l.Title}]({l.Uri})"); else sb.AppendLine($"- {l.Title}");
            }
        }
        return sb.ToString();
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
