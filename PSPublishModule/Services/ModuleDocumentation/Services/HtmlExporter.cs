using System;
using PowerForge;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HtmlForgeX;
using HtmlForgeX.Markdown;
using System.Management.Automation.Language;
using HtmlForgeX.Extensions;

namespace PSPublishModule;

/// <summary>
/// Builds the interactive HTML documentation page (tabs, markdown rendering, DataTables, diagrams)
/// from a module's metadata and planned document items.
/// </summary>
internal sealed partial class HtmlExporter
{
    public string Export(ModuleInfoModel module, IEnumerable<DocumentItem> items, string? destinationPath, bool open, Action<string>? log = null)
    {
        var list = items?.ToList() ?? new List<DocumentItem>();

        // Build document page per HtmlForgeX examples
        var doc = new Document { ThemeMode = ThemeMode.System, LibraryMode = LibraryMode.Offline };
        ConfigureDocumentDefaults(doc);
        // Route all Markdown(...) calls through the OfficeIMO-backed provider for consistent GFM-style parsing.
        doc.Settings(settings => settings.UseOfficeImo(options =>
        {
            options.PrismTheme = PrismJsTheme.GitHub;
            options.UseTablerAlertsForCallouts = true;
        }).End());
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
                                // Standard docs (everything except SCRIPT/DOC/ABOUT/FORMAT/TYPE/COMMUNITY/RELEASES kinds)
                                var standard = list.Where(x => !string.Equals(x.Kind, "SCRIPT", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "DOC", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "DOCSOURCE", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "ABOUT", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "FORMAT", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "TYPE", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "COMMUNITY", System.StringComparison.OrdinalIgnoreCase)
                                                           && !string.Equals(x.Kind, "RELEASES", System.StringComparison.OrdinalIgnoreCase))
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
                                            BaseUri = it.BaseUri,
                                            DataTables = new MarkdownDataTablesOptions {
                                                Responsive = true,
                                                // Start in Responsive; ToggleView button lets users switch to ScrollX
                                                Export = true,
                                                ExportFormats = new [] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy },
                                                StateSave = true
                                            }
                                        };
                                        panel.Markdown(PrepareMarkdown(module, md), options);
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
                                            ConfigureNestedTabs(inner);
                                            foreach (var s in scripts)
                                            {
                                                var name = string.IsNullOrWhiteSpace(s.FileName) ? s.Title : s.FileName;
                                                inner.AddTab(name ?? string.Empty, p =>
                                                {
                                                    var md = s.Content ?? string.Empty; // already fenced with powershell
                                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true };
                                                    p.Markdown(PrepareMarkdown(module, md), options);
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
                        ConfigureNestedTabs(group, navWidth: "12rem");
                        group.AddTab("📄 Local", p =>
                        {
                            var inner = new TablerTabs();
                            ConfigureNestedTabs(inner);
                            p.Add(inner);
                            foreach (var d in docsLocal)
                            {
                                var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                                inner.AddTab(name ?? string.Empty, pp =>
                                {
                                    var md = d.Content ?? string.Empty;
                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true, AllowRelativeLinks = true, BaseUri = d.BaseUri, TableMode = MarkdownTableMode.DataTables, DataTables = new MarkdownDataTablesOptions { Responsive = true, Export = true, ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy }, StateSave = true } };
                                    pp.Markdown(PrepareMarkdown(module, md), options);
                                });
                            }
                        });
                        group.AddTab("🌐 Repository", p =>
                        {
                            var inner = new TablerTabs();
                            ConfigureNestedTabs(inner);
                            p.Add(inner);
                            foreach (var d in docsRepo)
                            {
                                var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                                inner.AddTab(name ?? string.Empty, pp =>
                                {
                                    var md = d.Content ?? string.Empty;
                                    var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true, AllowRelativeLinks = true, BaseUri = d.BaseUri, TableMode = MarkdownTableMode.DataTables, DataTables = new MarkdownDataTablesOptions { Responsive = true, Export = true, ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy }, StateSave = true } };
                                    pp.Markdown(PrepareMarkdown(module, md), options);
                                });
                            }
                        });
                    });
                }
                else
                {
                    var render = docsLocal.Count > 0 ? docsLocal : docsRepo;
                    var inner = new TablerTabs();
                    ConfigureNestedTabs(inner);
                    panel.Add(inner);
                    foreach (var d in render)
                    {
                        var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                        inner.AddTab(name ?? string.Empty, pp =>
                        {
                            var md = d.Content ?? string.Empty;
                            var options = new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true, AllowRelativeLinks = true, BaseUri = d.BaseUri, TableMode = MarkdownTableMode.DataTables, DataTables = new MarkdownDataTablesOptions { Responsive = true, Export = true, ExportFormats = new[] { DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy }, StateSave = true } };
                            pp.Markdown(PrepareMarkdown(module, md), options);
                        });
                    }
                }
            });
        }

                                // Documentation source templates (Liquid/Jekyll or similar)
        var docSourcesAll = list.Where(x => string.Equals(x.Kind, "DOCSOURCE", System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (docSourcesAll.Count > 0)
        {
            var docSourcesLocal = docSourcesAll.Where(x => string.Equals(x.Source, "Local", StringComparison.OrdinalIgnoreCase)).ToList();
            var docSourcesRepo  = docSourcesAll.Where(x => string.Equals(x.Source, "Remote", StringComparison.OrdinalIgnoreCase)).ToList();
            log?.Invoke($"Adding Source Docs tab with {docSourcesAll.Count} items (Local={docSourcesLocal.Count}, Repo={docSourcesRepo.Count})...");
            tabs.AddTab("🧱 Source Docs", panel =>
            {
                void RenderSourceTabs(TablerTabs innerTabs, IEnumerable<DocumentItem> itemsToRender)
                {
                    foreach (var d in itemsToRender)
                    {
                        var name = string.IsNullOrWhiteSpace(d.FileName) ? MakeShortTabTitle(d) : d.FileName;
                        innerTabs.AddTab(name ?? string.Empty, pp =>
                        {
                            var md = d.Content;
                            if (string.IsNullOrWhiteSpace(md) && !string.IsNullOrWhiteSpace(d.Path) && File.Exists(d.Path))
                            {
                                md = RepositoryContentNormalizer.WrapAsSourceCodeBlock(File.ReadAllText(d.Path), "markdown");
                            }

                            pp.Markdown(PrepareMarkdown(module, md), new MarkdownOptions
                            {
                                HeadingsBaseLevel = 2,
                                AutolinkBareUrls = true,
                                Sanitize = true,
                                AllowRawHtmlInline = true,
                                AllowRawHtmlBlocks = true
                            });
                        });
                    }
                }

                if (docSourcesLocal.Count > 0 && docSourcesRepo.Count > 0)
                {
                    panel.Tabs(group =>
                    {
                        ConfigureNestedTabs(group, navWidth: "12rem");
                        group.AddTab("📄 Local", p =>
                        {
                            var inner = new TablerTabs();
                            ConfigureNestedTabs(inner);
                            p.Add(inner);
                            RenderSourceTabs(inner, docSourcesLocal);
                        });
                        group.AddTab("🌐 Repository", p =>
                        {
                            var inner = new TablerTabs();
                            ConfigureNestedTabs(inner);
                            p.Add(inner);
                            RenderSourceTabs(inner, docSourcesRepo);
                        });
                    });
                }
                else
                {
                    var render = docSourcesLocal.Count > 0 ? docSourcesLocal : docSourcesRepo;
                    var inner = new TablerTabs();
                    ConfigureNestedTabs(inner);
                    panel.Add(inner);
                    RenderSourceTabs(inner, render);
                }
            });
        }

                                // About topics (about_*.help.txt)
                                var abouts = list.Where(x => string.Equals(x.Kind, "ABOUT", System.StringComparison.OrdinalIgnoreCase)).ToList();
                                if (abouts.Count > 0)
                                {
                                    log?.Invoke($"Adding About tab with {abouts.Count} topics...");
                                    tabs.AddTab("ℹ️ About", panel =>
                                    {
                                        panel.Tabs(inner => {
                                            ConfigureNestedTabs(inner);
                                            foreach (var a in abouts.OrderBy(x => x.FileName ?? x.Title, StringComparer.OrdinalIgnoreCase))
                                            {
                                                var name = string.IsNullOrWhiteSpace(a.FileName) ? a.Title : a.FileName;
                                                inner.AddTab(name ?? string.Empty, p =>
                                                {
                                                    var md = a.Content;
                                                    if (string.IsNullOrWhiteSpace(md) && !string.IsNullOrWhiteSpace(a.Path) && File.Exists(a.Path))
                                                    {
                                                        md = $"```text\n{File.ReadAllText(a.Path)}\n```";
                                                    }
                                                    p.Markdown(PrepareMarkdown(module, md), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                                                });
                                            }
                                        });
                                    });
                                }

                                // Formats/Types tab (ps1xml)
                                var formats = list.Where(x => string.Equals(x.Kind, "FORMAT", System.StringComparison.OrdinalIgnoreCase) || string.Equals(x.Kind, "TYPE", System.StringComparison.OrdinalIgnoreCase)).ToList();
                                if (formats.Count > 0)
                                {
                                    log?.Invoke($"Adding Formats/Types tab with {formats.Count} items...");
                                    tabs.AddTab("📐 Formats / Types", panel =>
                                    {
                                        panel.Tabs(inner => {
                                            ConfigureNestedTabs(inner);
                                            foreach (var f in formats.OrderBy(x => x.FileName ?? x.Title, StringComparer.OrdinalIgnoreCase))
                                            {
                                                var name = string.IsNullOrWhiteSpace(f.FileName) ? f.Title : f.FileName;
                                                inner.AddTab(name ?? string.Empty, p =>
                                                {
                                                    var md = f.Content;
                                                    if (string.IsNullOrWhiteSpace(md) && !string.IsNullOrWhiteSpace(f.Path) && File.Exists(f.Path))
                                                    {
                                                        md = RepositoryContentNormalizer.WrapAsSourceCodeBlock(File.ReadAllText(f.Path), "text");
                                                    }
                                                    else
                                                    {
                                                        md = NormalizeSourceLikeMarkdown(md);
                                                    }

                                                    p.Markdown(PrepareMarkdown(module, md), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                                                });
                                            }
                                        });
                                    });
                                }

                                // Community health files (CONTRIBUTING/SECURITY/SUPPORT/CODE_OF_CONDUCT)
                                var community = list.Where(x => string.Equals(x.Kind, "COMMUNITY", System.StringComparison.OrdinalIgnoreCase)).ToList();
                                if (community.Count > 0)
                                {
                                    log?.Invoke($"Adding Community tab with {community.Count} items...");
                                    tabs.AddTab("👥 Community", panel =>
                                    {
                                        panel.Tabs(inner => {
                                            ConfigureNestedTabs(inner);
                                            foreach (var c in community.OrderBy(x => x.FileName ?? x.Title, StringComparer.OrdinalIgnoreCase))
                                            {
                                                var name = string.IsNullOrWhiteSpace(c.FileName) ? c.Title : c.FileName;
                                                inner.AddTab(name ?? string.Empty, p =>
                                                {
                                                    var md = c.Content;
                                                    if (string.IsNullOrWhiteSpace(md) && !string.IsNullOrWhiteSpace(c.Path) && File.Exists(c.Path))
                                                    {
                                                        md = File.ReadAllText(c.Path);
                                                    }
                                                    p.Markdown(PrepareMarkdown(module, md), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                                                });
                                            }
                                        });
                                    });
                                }

                                // Releases summary
                                var releases = list.Where(x => string.Equals(x.Kind, "RELEASES", System.StringComparison.OrdinalIgnoreCase)).ToList();
                                if (releases.Count > 0)
                                {
                                    log?.Invoke("Adding Releases tab...");
                                    var rel = releases.Last();
                                    tabs.AddTab("📦 Releases", panel =>
                                    {
                                        if (rel.Releases is { Count: > 0 })
                                        {
                                            RenderStructuredReleases(panel, rel.Releases);
                                        }
                                        else
                                        {
                                            panel.Markdown(PrepareMarkdown(module, rel.Content), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
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
                                            ConfigureNestedTabs(depTabs, navWidth: "12rem");
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
                                                        Kinds = string.Join(", ", g.Select(x => x.Kind == DocumentationDocumentationModuleDependencyKind.External ? "External" : "Required").Distinct().OrderBy(k => k == "External" ? 0 : 1)),
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
                                        var helpMap = BuildHelpMap(module.Name, commands, module.HelpTimeoutSeconds, module.ExamplesMode, module.ExamplesLayout, log);
                                        tabs.AddTab("⚙️ Commands", panel =>
                                        {
                                        panel.Tabs(inner => {
                                            ConfigureNestedTabs(inner, navWidth: "20rem");
                                            foreach (var entry in helpMap)
                                            {
                                                var label = $"{EmojiForCommand(entry.Command)} {entry.Command}".Replace("-", "‑");
                                                inner.AddTab(label, p => {
                                                    var content = entry.Help ?? "No help available.";
                                                    if (module.HelpAsCode && !entry.Structured)
                                                    {
                                                        var fenced = $"```powershell\n{content}\n```";
                                                        p.Markdown(PrepareMarkdown(module, fenced), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                                                    }
                                                    else
                                                    {
                                                        p.Markdown(PrepareMarkdown(module, content), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
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

    private static string PrepareMarkdown(ModuleInfoModel module, string? markdown)
    {
        var text = ApplyHeadingRules(markdown ?? string.Empty, module.HeadingRules);
        if (!module.DisableTokenizer || text.Length == 0)
            return text;

        var usesCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var leading = line.Length - line.TrimStart().Length;
            var trimmed = line.Substring(leading);
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var markerChar = trimmed[0];
                var markerLength = 0;
                while (markerLength < trimmed.Length && trimmed[markerLength] == markerChar)
                    markerLength++;

                if (markerLength >= 3)
                    lines[i] = line.Substring(0, leading) + new string(markerChar, markerLength);
            }
        }

        var prepared = string.Join("\n", lines);
        return usesCrLf ? prepared.Replace("\n", "\r\n") : prepared;
    }

    internal static string PrepareMarkdownForTesting(ModuleInfoModel module, string? markdown)
        => PrepareMarkdown(module, markdown);

    private static string ApplyHeadingRules(string markdown, DocumentationHeadingRules headingRules)
    {
        if (headingRules == DocumentationHeadingRules.None || string.IsNullOrEmpty(markdown))
            return markdown;

        var usesCrLf = markdown.Contains("\r\n", StringComparison.Ordinal);
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length + 8);
        var inFence = false;
        char fenceChar = '\0';
        var fenceLength = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var markerLength = CountFenceMarker(trimmed, out var markerChar);
            if (markerLength >= 3)
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceChar = markerChar;
                    fenceLength = markerLength;
                }
                else if (markerChar == fenceChar && markerLength >= fenceLength)
                {
                    inFence = false;
                    fenceChar = '\0';
                    fenceLength = 0;
                }

                output.Add(line);
                continue;
            }

            output.Add(line);
            if (inFence)
                continue;

            var level = GetAtxHeadingLevel(trimmed);
            if (level == 1 || (level == 2 && headingRules == DocumentationHeadingRules.H1AndH2))
            {
                var next = NextNonEmpty(lines, i + 1);
                if (!IsHorizontalRule(next))
                {
                    output.Add(string.Empty);
                    output.Add("---");
                    output.Add(string.Empty);
                }
            }
        }

        var prepared = string.Join("\n", output);
        return usesCrLf ? prepared.Replace("\n", "\r\n") : prepared;
    }

    private static int CountFenceMarker(string text, out char markerChar)
    {
        markerChar = '\0';
        if (string.IsNullOrEmpty(text) || (text[0] != '`' && text[0] != '~'))
            return 0;

        markerChar = text[0];
        var length = 0;
        while (length < text.Length && text[length] == markerChar)
            length++;
        return length;
    }

    private static int GetAtxHeadingLevel(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed) || trimmed[0] != '#')
            return 0;

        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level == 0 || level > 6)
            return 0;

        return level < trimmed.Length && char.IsWhiteSpace(trimmed[level]) ? level : 0;
    }

    private static string? NextNonEmpty(string[] lines, int index)
    {
        for (var i = index; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                return lines[i].Trim();
        }

        return null;
    }

    private static bool IsHorizontalRule(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line!.Trim();
        if (trimmed.Equals("<hr>", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("<hr/>", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("<hr />", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var compact = new string(trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return compact.Length >= 3
               && compact.All(c => c == compact[0])
               && (compact[0] == '-' || compact[0] == '_' || compact[0] == '*');
    }

}
