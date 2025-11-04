using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HtmlForgeX;
using HtmlForgeX.Markdown;
using System.Management.Automation.Language;
using HtmlForgeX.Extensions;

namespace PSMaintenance;

/// <summary>
/// Builds the interactive HTML documentation page (tabs, markdown rendering, DataTables, diagrams)
/// from a module's metadata and planned document items.
/// </summary>
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
                                // Standard tabs selection with de-dup and mode
                                string BaseLabel(string t, string? fileName)
                                {
                                    if (!string.IsNullOrEmpty(fileName))
                                    {
                                        var fn = (fileName ?? string.Empty).Trim();
                                        if (fn.StartsWith("README", StringComparison.OrdinalIgnoreCase)) return "README";
                                        if (fn.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)) return "CHANGELOG";
                                        if (fn.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)) return "LICENSE";
                                    }
                                    if (string.IsNullOrEmpty(t)) return t;
                                    var u = t.Trim();
                                    if (u.StartsWith("README", StringComparison.OrdinalIgnoreCase)) return "README";
                                    if (u.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)) return "CHANGELOG";
                                    if (u.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)) return "LICENSE";
                                    return u;
                                }
                                string NormalizeForCompare(string s)
                                    => string.Join("\n", (s ?? string.Empty).Replace("\r\n","\n").Split('\n').Select(l => l.TrimEnd())).TrimEnd();

                                var groups = standard.GroupBy(s => BaseLabel(s.Title ?? string.Empty, s.FileName), StringComparer.OrdinalIgnoreCase);
                                var selected = new System.Collections.Generic.List<DocumentItem>();
                                foreach (var g in groups)
                                {
                                    var label = g.Key;
                                    if (!(label == "README" || label == "CHANGELOG" || label == "LICENSE"))
                                    {
                                        // Non-standard files (should be rare here) - include as-is
                                        selected.AddRange(g);
                                        continue;
                                    }
                                    var locals = g.Where(x => string.Equals(x.Source, "Local", StringComparison.OrdinalIgnoreCase)).ToList();
                                    var remotes = g.Where(x => string.Equals(x.Source, "Remote", StringComparison.OrdinalIgnoreCase)).ToList();

                                    // de-dup identical local copies (if multiple appear)
                                    if (locals.Count > 1 && !module.ShowDuplicates)
                                    {
                                        var first = locals.First();
                                        var firstN = NormalizeForCompare(first.Content ?? string.Empty);
                                        var uniq = new System.Collections.Generic.List<DocumentItem> { first };
                                        foreach (var it in locals.Skip(1))
                                        {
                                            if (NormalizeForCompare(it.Content ?? string.Empty) != firstN) { uniq.Add(it); }
                                        }
                                        var removed = locals.Count - uniq.Count;
                                        if (removed > 0)
                                            log?.Invoke($"[INFO] {label}: removed {removed} duplicate Local copy/copies (identical content).");
                                        locals = uniq;
                                    }
                                    // de-dup identical remote copies
                                    if (remotes.Count > 1 && !module.ShowDuplicates)
                                    {
                                        var first = remotes.First();
                                        var firstN = NormalizeForCompare(first.Content ?? string.Empty);
                                        var uniq = new System.Collections.Generic.List<DocumentItem> { first };
                                        foreach (var it in remotes.Skip(1))
                                        {
                                            if (NormalizeForCompare(it.Content ?? string.Empty) != firstN) { uniq.Add(it); }
                                        }
                                        var removed = remotes.Count - uniq.Count;
                                        if (removed > 0)
                                            log?.Invoke($"[INFO] {label}: removed {removed} duplicate Remote copy/copies (identical content).");
                                        remotes = uniq;
                                    }

                                    var hasLocal = locals.Count > 0;
                                    var hasRemote = module.Online && remotes.Count > 0;

                                    DocumentItem? pickLocal = hasLocal ? locals.First() : null;
                                    DocumentItem? pickRemote = hasRemote ? remotes.First() : null;

                                    bool equalLR = false;
                                    if (pickLocal != null && pickRemote != null)
                                    {
                                        equalLR = NormalizeForCompare(pickLocal.Content ?? string.Empty) == NormalizeForCompare(pickRemote.Content ?? string.Empty);
                                    }

                                    if (!module.Online)
                                    {
                                        // Local-only
                                        if (pickLocal != null) selected.Add(pickLocal);
                                        continue;
                                    }

                                    switch (module.Mode)
                                    {
                                        case DocumentationMode.All:
                                            if (pickLocal != null && pickRemote != null)
                                            {
                                                if (equalLR && !module.ShowDuplicates)
                                                {
                                                    // collapse to one (prefer Local)
                                                    log?.Invoke($"[INFO] {label}: Remote identical to Local - showing one tab (Local).");
                                                    selected.Add(pickLocal);
                                                }
                                                else
                                                {
                                                    selected.Add(pickLocal);
                                                    selected.Add(pickRemote);
                                                    if (!equalLR)
                                                        log?.Invoke($"[INFO] {label}: Local and Remote differ - showing both.");
                                                }
                                            }
                                            else if (pickLocal != null) selected.Add(pickLocal);
                                            else if (pickRemote != null) selected.Add(pickRemote);
                                            break;
                                        case DocumentationMode.PreferRemote:
                                            if (pickRemote != null)
                                            {
                                                if (pickLocal != null && equalLR && !module.ShowDuplicates)
                                                    log?.Invoke($"[INFO] {label}: Local identical to Remote - hiding Local (PreferRemote).");
                                                selected.Add(pickRemote);
                                            }
                                            else if (pickLocal != null)
                                            {
                                                log?.Invoke($"[INFO] {label}: Remote missing - using Local (PreferRemote fallback).");
                                                selected.Add(pickLocal);
                                            }
                                            break;
                                        default: // PreferLocal
                                            if (pickLocal != null)
                                            {
                                                if (pickRemote != null && equalLR && !module.ShowDuplicates)
                                                    log?.Invoke($"[INFO] {label}: Remote identical to Local - hiding Remote (PreferLocal).");
                                                selected.Add(pickLocal);
                                            }
                                            else if (pickRemote != null)
                                            {
                                                log?.Invoke($"[INFO] {label}: Local missing - using Remote (PreferLocal fallback).");
                                                selected.Add(pickRemote);
                                            }
                                            break;
                                    }
                                }

                                foreach (var it in selected)
                                {
                                    var title = MakeShortTabTitle(it);
                                    var src = string.IsNullOrEmpty(it.Source) ? "unknown" : it.Source;
                                    log?.Invoke($"Adding document tab: {title} [source: {src}]");
                                    var added = tabs.AddTab(title, panel =>
                                    {
                                        var md = it.Content ?? string.Empty;
                                        var options = new MarkdownOptions
                                        {
                                            HeadingsBaseLevel = 2,
                                            TableMode = MarkdownTableMode.DataTables,
                                            OpenLinksInNewTab = true,
                                            Sanitize = true,
                                            AutolinkBareUrls = true,
                                            AllowRelativeLinks = true,
                                            AllowRawHtmlInline = true,
                                            AllowRawHtmlBlocks = true,
                                            DataTables = new MarkdownDataTablesOptions {
                                                Responsive = true,
                                                // Start in Responsive; ToggleView button lets users switch to ScrollX
                                                Export = true,
                                                ExportFormats = new [] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy },
                                                StateSave = true
                                            }
                                        };
                                        panel.Markdown(md, options);
                                    });
                                    // Badge to indicate source
                                    try
                                    {
                                        if (string.Equals(it.Source, "Local", StringComparison.OrdinalIgnoreCase)) added.WithBadge("local", TablerBadgeColor.Blue);
                                        else if (string.Equals(it.Source, "Remote", StringComparison.OrdinalIgnoreCase)) added.WithBadge("remote", TablerBadgeColor.Teal);
                                    }
                                    catch { }
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
                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true, TableMode = MarkdownTableMode.DataTables, DataTables = new MarkdownDataTablesOptions { Responsive = true, Export = true, ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy }, StateSave = true } };
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
                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true, TableMode = MarkdownTableMode.DataTables, DataTables = new MarkdownDataTablesOptions { Responsive = true, Export = true, ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy }, StateSave = true } };
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
                            var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true, TableMode = MarkdownTableMode.DataTables, DataTables = new MarkdownDataTablesOptions { Responsive = true, Export = true, ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy }, StateSave = true } };
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
                                                    c.DataTable(rows, t => t
                                                        .Settings(s => s.Preset(DataTablesPreset.Minimal))
                                                        .Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy))
                                                        .Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))
                                                    );
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
                                                    string NodeId(string name, int level) => $"{name}||L{level}"; // duplicate per level
                                                    void AddNodeSafe(string id, System.Action<VisNetworkNodeOptions> cfg) { if (added.Add(id)) net.AddNode(id, cfg); }

                                                    // Root node
                                                    var rootId = NodeId(module.Name, 0);
                                                    AddNodeSafe(rootId, node => node.WithLabel(module.Name).WithLevel(0).WithColor(new RGBColor("#2962FF")).WithFont(f => f.WithColor(new RGBColor("#ffffff"))));

                                                    // Direct dependencies at Level 1
                                                    foreach (var d in module.Dependencies)
                                                    {
                                                        var depIdL1 = NodeId(d.Name, 1);
                                                        AddNodeSafe(depIdL1, node => node.WithLabel(d.Name + (string.IsNullOrEmpty(d.Version) ? string.Empty : $"\n{d.Version}")).WithLevel(1));
                                                        net.AddEdge(rootId, depIdL1);
                                                    }

                                                    // Nested dependencies at Level 2 (even if child also exists at Level 1)
                                                    foreach (var d in module.Dependencies)
                                                    {
                                                        var parentL1 = NodeId(d.Name, 1);
                                                        foreach (var c in d.Children)
                                                        {
                                                            var childL2 = NodeId(c.Name, 2);
                                                            AddNodeSafe(childL2, node => node.WithLabel(c.Name + (string.IsNullOrEmpty(c.Version) ? string.Empty : $"\n{c.Version}")).WithLevel(2));
                                                            net.AddEdge(parentL1, childL2, edge => {
                                                                edge.WithDashes(true);
                                                                edge.WithSmooth(s => { s.Enabled = true; s.Type = VisNetworkSmoothType.Dynamic; });
                                                            });
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
                                        var helpMap = BuildHelpMap(module.Name, commands, module.HelpTimeoutSeconds, module.ExamplesMode, log);
                                        tabs.AddTab("⚙️ Commands", panel =>
                                        {
                                        panel.Tabs(inner => {
                                            foreach (var entry in helpMap)
                                            {
                                                var label = $"{EmojiForCommand(entry.Command)} {entry.Command}".Replace("-", "‑");
                                                inner.AddTab(label, p => {
                                                    if (entry.Model != null)
                                                    {
                                                        RenderHelpPanel(p, entry.Model, module.ExamplesLayout);
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
        try { return FormatRawHelpMarkdown(text); } catch { }
        // Naive fallback
        var md = text.Replace("SYNTAX", "# Syntax").Replace("DESCRIPTION", "# Description").Replace("PARAMETERS", "# Parameters").Replace("EXAMPLES", "# Examples");
        return md;
    }

    // --- Raw Get-Help text -> Markdown with robust Examples handling ---
    private static readonly string[] RawHelpHeaders = new[] { "NAME", "SYNOPSIS", "SYNTAX", "DESCRIPTION", "PARAMETERS", "EXAMPLES", "INPUTS", "OUTPUTS", "NOTES", "RELATED LINKS" };

    private string FormatRawHelpMarkdown(string raw)
    {
        var nl = (raw ?? string.Empty).Replace("\r\n", "\n");
        var lines = nl.Split(new[] {'\n'}, StringSplitOptions.None);
        var idx = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for (int i=0;i<lines.Length;i++)
        {
            var t = (lines[i] ?? string.Empty).Trim();
            if (Array.IndexOf(RawHelpHeaders, t.ToUpperInvariant()) >= 0 && !idx.ContainsKey(t)) idx[t] = i;
        }
        var sb = new System.Text.StringBuilder(nl.Length + 1024);
        void AppendSection(string h)
        {
            if (!idx.TryGetValue(h, out var start)) return;
            int end = lines.Length;
            foreach (var oh in RawHelpHeaders)
            {
                if (oh.Equals(h, StringComparison.OrdinalIgnoreCase)) continue;
                if (idx.TryGetValue(oh, out var pos) && pos > start && pos < end) end = pos;
            }
            var body = lines.Skip(start + 1).Take(end - start - 1).ToList();
            if (h.Equals("EXAMPLES", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("# Examples");
                AppendExamplesBlock(body, sb);
            }
            else
            {
                sb.AppendLine("# " + ToTitle(h));
                foreach (var l in body) sb.AppendLine(l);
                sb.AppendLine();
            }
        }
        foreach (var h in RawHelpHeaders) AppendSection(h);
        return sb.ToString();
    }

    private static string ToTitle(string s)
        => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase((s ?? string.Empty).ToLowerInvariant());

    private void AppendExamplesBlock(List<string> body, System.Text.StringBuilder sb)
    {
        var boundaries = new List<int>();
        for (int i=0;i<body.Count;i++)
        {
            var t = (body[i] ?? string.Empty).Trim();
            if (t.StartsWith("EXAMPLE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Example", StringComparison.OrdinalIgnoreCase)) boundaries.Add(i);
        }
        if (boundaries.Count == 0)
        {
            sb.AppendLine("````"); foreach (var l in body) sb.AppendLine(l); sb.AppendLine("````"); return;
        }
        boundaries.Add(body.Count);
        for (int b=0;b<boundaries.Count-1;b++)
        {
            int start = boundaries[b]; int end = boundaries[b+1];
            var chunk = body.Skip(start).Take(end-start).ToList();
            var title = (chunk.FirstOrDefault() ?? string.Empty).Trim();
            var content = string.Join("\n", chunk.Skip(1));
            // Prefer AST-based split using ExampleClassifier; fall back to internal heuristic
            string codeOut, remarksOut, mode;
            var classifier = new ExampleClassifier();
            if (!classifier.Classify(content, out codeOut, out remarksOut, out mode))
            {
                if (!TrySplitExampleWithAst(content, out codeOut, out remarksOut))
                {
                    ReclassifyExample(string.Empty, content, out codeOut, out remarksOut);
                }
            }
            if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine("## " + title);
            var block = string.IsNullOrEmpty(remarksOut) ? codeOut : ((codeOut ?? string.Empty) + "\n\n" + remarksOut);
            sb.AppendLine("```powershell");
            sb.AppendLine((block ?? string.Empty).TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    // Attempt AST/token-based split of example content into code and remarks.
    // Returns true on success with code/remarks assigned; false to allow caller fallback.
    private bool TrySplitExampleWithAst(string exampleBody, out string codeOut, out string remarksOut)
    {
        codeOut = string.Empty; remarksOut = string.Empty;
        if (exampleBody == null) return false;
        var text = exampleBody.Replace("\r\n", "\n");
        // Quick skip: if it looks empty, bail
        if (string.IsNullOrWhiteSpace(text)) { codeOut = string.Empty; remarksOut = string.Empty; return true; }

        try
        {
            // Parse the entire example body. We tolerate errors and still use tokens/AST extents.
            Token[] tokens; ParseError[] errors;
            var ast = Parser.ParseInput(text, out tokens, out errors);

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            var isCode = new bool[lines.Length];

            // Mark lines covered by significant AST nodes as code
            // We include statements, pipelines, commands, hashtables, arrays, scriptblocks, assignments, etc.
            Func<Ast, bool> selector = a =>
                a is PipelineAst || a is CommandAst || a is CommandExpressionAst ||
                a is AssignmentStatementAst || a is HashtableAst || a is ArrayLiteralAst || a is ArrayExpressionAst ||
                a is ScriptBlockAst || a is FunctionDefinitionAst || a is IfStatementAst || a is ForEachStatementAst || a is ForStatementAst ||
                a is WhileStatementAst || a is DoWhileStatementAst || a is DoUntilStatementAst || a is TryStatementAst || a is SwitchStatementAst ||
                a is TrapStatementAst || a is ReturnStatementAst || a is ThrowStatementAst || a is BreakStatementAst || a is ContinueStatementAst;

            foreach (var node in ast.FindAll(selector, true))
            {
                var start = Math.Max(1, node.Extent.StartLineNumber) - 1;
                var end = Math.Max(start, node.Extent.EndLineNumber - 1);
                for (int i = start; i <= end && i < isCode.Length; i++) isCode[i] = true;
            }

            // Avoid treating arbitrary tokens as code; rely on AST nodes only to minimize false positives.

            // Build code and remarks while preserving blank lines. Attach blank lines to the active block kind.
            var codeSb = new System.Text.StringBuilder();
            var remSb = new System.Text.StringBuilder();
            bool lastWasCode = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (isCode[i] || (string.IsNullOrWhiteSpace(l) && lastWasCode))
                {
                    codeSb.AppendLine(l);
                    lastWasCode = true;
                }
                else
                {
                    remSb.AppendLine(l);
                    lastWasCode = false;
                }
            }

            codeOut = codeSb.ToString().TrimEnd();
            remarksOut = remSb.ToString().Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Heuristic similar to the structured path: reclassify leading remarks back into code when appropriate
    private void ReclassifyExample(string codeRaw, string remarksRaw, out string codeOut, out string remarksOut)
    {
        var code = (codeRaw ?? string.Empty).Replace("\r\n", "\n");
        var remarks = (remarksRaw ?? string.Empty).Replace("\r\n", "\n");
        if (string.IsNullOrWhiteSpace(remarks)) { codeOut = code.TrimEnd(); remarksOut = string.Empty; return; }
        var lines = remarks.Split(new[] {'\n'}, StringSplitOptions.None).ToList();
        var prefix = new System.Text.StringBuilder();
        int idx = 0; int curly = 0, paren = 0; bool inDQ=false,inSQ=false,inHereD=false,inHereS=false;
        bool IsLikelyCode(string line)
        {
            var t=(line??string.Empty); var trimmed=t.TrimStart();
            if (trimmed.Length==0) return true; if (trimmed.StartsWith("PS ")||trimmed.StartsWith("PS>")||trimmed.StartsWith("C:\\")) return true;
            if (trimmed.StartsWith("#")) return true; if (trimmed.StartsWith("@{")||trimmed.StartsWith("@(")||trimmed.StartsWith("param(")) return true;
            if (trimmed.StartsWith("function ", StringComparison.OrdinalIgnoreCase)||trimmed.StartsWith("if (", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("$")) return true; if (trimmed.Contains(" = ")||trimmed.Contains(" -" )||trimmed.Contains("`")) return true;
            if (trimmed.StartsWith("Email", StringComparison.OrdinalIgnoreCase)) return true; return false;
        }
        void ScanDepth(string l)
        {
            for (int i=0;i<l.Length;i++)
            {
                var ch=l[i];
                if (!inHereD && !inHereS)
                {
                    if (ch=='"' && !inSQ) inDQ=!inDQ; else if (ch=='\'' && !inDQ) inSQ=!inSQ;
                    if (!inDQ && !inSQ) { if (ch=='{') curly++; else if (ch=='}') curly=Math.Max(0,curly-1); else if (ch=='(') paren++; else if (ch==')') paren=Math.Max(0,paren-1);}
                    if (i+1<l.Length && l[i]=='@' && (l[i+1]=='"' || l[i+1]=='\'')) { if (l[i+1]=='"' && !inSQ) { inHereD=true; i++; } else if (l[i+1]=='\'' && !inDQ) { inHereS=true; i++; } }
                }
                else { var s=l.TrimStart(); if (inHereD && s.StartsWith("\"@")) inHereD=false; if (inHereS && s.StartsWith("'@")) inHereS=false; }
            }
        }
        while (idx<lines.Count)
        {
            var line=lines[idx]; ScanDepth(line);
            if (IsLikelyCode(line) || inHereD || inHereS || curly>0 || paren>0) { prefix.AppendLine(line); idx++; continue; }
            break;
        }
        if (prefix.Length>0)
        {
            codeOut = string.IsNullOrEmpty(code) ? prefix.ToString().TrimEnd() : (code.TrimEnd()+"\n\n"+prefix.ToString().TrimEnd());
            remarksOut = string.Join("\n", lines.Skip(idx)).TrimStart('\n');
        }
        else { codeOut = code.TrimEnd(); remarksOut = remarks.TrimStart('\n'); }
    }

    private List<(string Command, CommandHelpModel? Model, string? Help, int Lines)> BuildHelpMap(string moduleName, List<string> commands, int timeoutSeconds, ExamplesMode examplesMode, Action<string>? log)
    {
        var list = new List<(string, CommandHelpModel?, string?, int)>();
            var parser = new GetHelpParser();
        foreach (var cmd in commands)
        {
            string? content = null;
            // Prefer structured parse
            var model = parser.Parse(cmd, timeoutSeconds, examplesMode);
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
                // Per-example diagnostics
                if (model.Examples != null && model.Examples.Count > 0)
                {
                    for (int i = 0; i < model.Examples.Count; i++)
                    {
                        var e = model.Examples[i];
                        var mode = string.IsNullOrEmpty(e.Mode) ? "structured" : e.Mode!;
                        log?.Invoke($"  • Example {i + 1}: mode={mode}, codeLines={e.CodeLines}, remarksLines={e.RemarksLines}");
                    }
                }
            }
            else
            {
                log?.Invoke($"Rendering help for {cmd}... ({lines} lines)");
            }
            list.Add((cmd, model, content, lines));
        }
        return list;
    }

    private static void RenderHelpPanel(TablerTabsPanel panel, CommandHelpModel m, ExamplesLayout examplesLayout)
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
            var byName = m.Parameters.ToDictionary(x => x.Name, System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < total; i++)
            {
                var s = m.Syntax[i];
                panel.Markdown($"### Syntax - Set {i + 1} of {total}", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
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

                // Immediately render the parameters for this set
                var names = s.Parameters.Select(p => p.Name).Distinct(System.StringComparer.OrdinalIgnoreCase);
                var rows = new System.Collections.Generic.List<object>();
                foreach (var n in names)
                {
                    var p = byName.TryGetValue(n, out var ph) ? ph : new ParameterHelp { Name = n };
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
                    c.Header(h => h.Title($"Parameters - Set {i + 1} of {total}"));
                    c.DataTable(rows, t => t
                        .Settings(s => s.Preset(DataTablesPreset.Minimal))
                        .Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy))
                        .Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))
                    );
                });
            }
            panel.LineBreak();
        }
        if (m.Inputs.Count > 0)
        {
            var rows = m.Inputs.Select(t => new { Type = t.TypeName, Description = t.Description ?? string.Empty }).ToList();
            panel.Card(c => { c.Header(h => h.Title("Inputs")); c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.Minimal)).Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy)).Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))); });
        }
        if (m.Outputs.Count > 0)
        {
            var rows = m.Outputs.Select(t => new { Type = t.TypeName, Description = t.Description ?? string.Empty }).ToList();
            panel.Card(c => { c.Header(h => h.Title("Outputs")); c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.Minimal)).Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy)).Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))); });
        }
        if (m.Examples.Count > 0)
        {
            panel.Markdown("## Examples", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            int idx = 1;
            foreach (var ex in m.Examples)
            {
                var title = NormalizeExampleTitle(ex.Title, idx);
                panel.Markdown($"### {title}", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                var code = ex.Code ?? string.Empty;
                var remarks = ex.Remarks ?? string.Empty;
                switch (examplesLayout)
                {
                    case ExamplesLayout.ProseFirst:
                        if (!string.IsNullOrWhiteSpace(remarks)) panel.Markdown(remarks.Trim(), new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        if (!string.IsNullOrWhiteSpace(code)) {
                            var fenced1 = $"```powershell\n{code.TrimEnd()}\n```";
                            panel.Markdown(fenced1, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        }
                        break;
                    case ExamplesLayout.AllAsCode:
                        var block = string.IsNullOrEmpty(remarks) ? code : (code + "\n\n" + remarks);
                        var fenced2 = $"```powershell\n{(block ?? string.Empty).TrimEnd()}\n```";
                        panel.Markdown(fenced2, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        break;
                    default: // MamlDefault
                        if (!string.IsNullOrWhiteSpace(code)) {
                            var fenced3 = $"```powershell\n{code.TrimEnd()}\n```";
                            panel.Markdown(fenced3, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        }
                        if (!string.IsNullOrWhiteSpace(remarks)) panel.Markdown(remarks.Trim(), new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        break;
                }
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
                var block = string.IsNullOrEmpty(ex.Remarks) ? (ex.Code ?? string.Empty) : ((ex.Code ?? string.Empty) + "\n\n" + ex.Remarks);
                sb.AppendLine("```powershell");
                sb.AppendLine((block ?? string.Empty).TrimEnd());
                sb.AppendLine("```");
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
