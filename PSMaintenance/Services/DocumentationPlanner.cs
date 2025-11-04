using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// Plans which documents to show/export based on local availability and repository settings.
/// </summary>
internal sealed class DocumentationPlanner
{
    private readonly DocumentationFinder _finder;
    public DocumentationPlanner(DocumentationFinder finder) => _finder = finder;

    internal sealed class Request
    {
        public string RootBase { get; set; } = string.Empty;
        public string? InternalsBase { get; set; }
        public PSObject? Delivery { get; set; }
        public string? ProjectUri { get; set; }
        public string? RepositoryBranch { get; set; }
        public string? RepositoryToken { get; set; }
        public string[]? RepositoryPaths { get; set; }
        public bool PreferInternals { get; set; }
        public bool Readme { get; set; }
        public bool Changelog { get; set; }
        public bool License { get; set; }
        public bool Intro { get; set; }
        public bool Upgrade { get; set; }
        public bool All { get; set; }
        public bool PreferRepository { get; set; } // legacy
        public bool FromRepository { get; set; }   // legacy
        public bool Online { get; set; }
        public DocumentationMode Mode { get; set; } = DocumentationMode.PreferLocal;
        public bool ShowDuplicates { get; set; }
        public string? SingleFile { get; set; }
        public string? TitleName { get; set; }
        public string? TitleVersion { get; set; }
    }

    internal sealed class Result
    {
        public List<DocumentItem> Items { get; } = new List<DocumentItem>();
        public bool UsedRemote { get; set; }
    }

    public Result Execute(Request req)
    {
        return Execute(req, null);
    }

    public Result Execute(Request req, IRepoClient? clientOverride)
    {
        var res = new Result();
        var items = new List<(string Kind, string Path)>();

        // Specific file selection
        if (!string.IsNullOrEmpty(req.SingleFile))
        {
            var t1 = Path.Combine(req.RootBase, req.SingleFile);
            var t2 = req.InternalsBase != null ? Path.Combine(req.InternalsBase, req.SingleFile) : null;
            if (File.Exists(t1)) items.Add(("FILE", t1));
            else if (t2 != null && File.Exists(t2)) items.Add(("FILE", t2));
            else throw new FileNotFoundException($"File '{req.SingleFile}' not found under root or Internals.");
        }

        // Intro/Upgrade/All toggles
        if (req.Intro) items.Add(("INTRO", string.Empty));
        if (req.All)
        {
            if (!req.Intro) items.Add(("INTRO", string.Empty));
            AddKind(items, req, DocumentKind.Readme, includeEvenIfSelected: !req.Readme);
            AddKind(items, req, DocumentKind.Changelog, includeEvenIfSelected: !req.Changelog);
            AddKind(items, req, DocumentKind.License, includeEvenIfSelected: !req.License);
        }

        if (req.Readme) AddKind(items, req, DocumentKind.Readme);
        if (req.Changelog) AddKind(items, req, DocumentKind.Changelog);
        if (req.License) AddKind(items, req, DocumentKind.License);
        if (req.Upgrade) items.Add(("UPGRADE", string.Empty));

        // Default selection when nothing specified: include all known docs
        bool hasSelectors = req.Readme || req.Changelog || req.License || req.All || req.Intro || req.Upgrade || !string.IsNullOrEmpty(req.SingleFile);
        if (!hasSelectors)
        {
            AddKind(items, req, DocumentKind.Readme);
            AddKind(items, req, DocumentKind.Changelog);
            AddKind(items, req, DocumentKind.License);
            AddKind(items, req, DocumentKind.Upgrade);
            // Add intro when IntroText/IntroFile present
            var introLines = req.Delivery?.Properties["IntroText"]?.Value as System.Collections.IEnumerable;
            var introFile = req.Delivery?.Properties["IntroFile"]?.Value as string;
            if (introLines != null || !string.IsNullOrEmpty(introFile)) items.Add(("INTRO", string.Empty));
        }

        // Remote repo fetch only when Online is requested
        var wantRemote = req.Online;
        if (wantRemote && (!string.IsNullOrWhiteSpace(req.ProjectUri) || clientOverride != null))
        {
            var client = clientOverride;
            if (client == null)
            {
                var info = RepoUrlParser.Parse(req.ProjectUri!);
                var token = ResolveToken(req.RepositoryToken);
                if (string.IsNullOrEmpty(token))
                {
                    // Try persisted token based on host (GitHub/Azure DevOps)
                    token = TokenStore.GetToken(info.Host) ?? string.Empty;
                }
                client = RepoClientFactory.Create(info, token);
            }
            if (client != null)
            {
                string branch = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client.GetDefaultBranch() : req.RepositoryBranch!;
                // Default remote files (always add candidates; selection policy is applied later in the exporter)
                var readme = TryFetchFirst(client, branch, new[] { "README.md", "README.MD", "Readme.md" });
                var changelog = TryFetchFirst(client, branch, new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" });
                var license = TryFetchFirst(client, branch, new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" });

                bool anyRemote = false;
                if (!string.IsNullOrEmpty(readme))
                {
                    var di = MakeContentItem(req, "README", readme!);
                    di.Source = "Remote"; di.FileName = "README.md"; di.Title = "README";
                    res.Items.Add(di); anyRemote = true;
                }
                if (!string.IsNullOrEmpty(changelog))
                {
                    var di = MakeContentItem(req, "CHANGELOG", changelog!);
                    di.Source = "Remote"; di.FileName = "CHANGELOG.md"; di.Title = "CHANGELOG";
                    res.Items.Add(di); anyRemote = true;
                }
                if (!string.IsNullOrEmpty(license))
                {
                    var di = MakeContentItem(req, "LICENSE", license!);
                    di.Source = "Remote"; di.FileName = "LICENSE"; di.Title = "LICENSE";
                    res.Items.Add(di); anyRemote = true;
                }
                // Extra paths
                if (req.RepositoryPaths != null)
                {
                    foreach (var rp in req.RepositoryPaths)
                    {
                        foreach (var (Name, Path) in client.ListFiles(rp, branch))
                        {
                            var ext = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
                            if (!(ext == ".md" || ext == ".markdown" || ext == ".txt")) continue;
                            var content = client.GetFileContent(Path, branch);
                            if (string.IsNullOrEmpty(content)) continue;
                            // Treat repository path content as documentation pages, not standard tabs
                            res.Items.Add(new DocumentItem {
                                Title = Name,
                                Kind = "DOC",
                                Content = content!,
                                FileName = Name,
                                Path = Path,
                                Source = "Remote"
                            });
                            anyRemote = true;
                        }
                    }
                }
                res.UsedRemote = anyRemote;
                // When PreferRepository is set, we still keep local standard docs so users can compare.
                // Remote items are added first; local will be added below when resolving 'items'.
            }
        }

        // Resolve local items into DocumentItem list
        foreach (var it in items)
        {
            if (it.Kind == "FILE")
            {
                var fileName = System.IO.Path.GetFileName(it.Path);
                var title = BuildTitle(req, fileName);
                res.Items.Add(new DocumentItem { Title = title, Kind = "FILE", Path = it.Path, FileName = fileName, Source = "Local" });
                continue;
            }
            if (it.Kind == "INTRO")
            {
                var introLines = req.Delivery?.Properties["IntroText"]?.Value as System.Collections.IEnumerable;
                var introFile = req.Delivery?.Properties["IntroFile"]?.Value as string;
                string content = string.Empty;
                if (introLines != null)
                {
                    content = string.Join("\n", introLines.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
                }
                else if (!string.IsNullOrEmpty(introFile))
                {
                    var p1 = Path.Combine(req.RootBase, introFile);
                    if (File.Exists(p1)) content = File.ReadAllText(p1);
                }
                if (!string.IsNullOrWhiteSpace(content))
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Introduction"), Kind = "FILE", Content = content });
                continue;
            }
            if (it.Kind == "UPGRADE")
            {
                // Prefer UpgradeText from delivery, else resolve file
                var upLines = req.Delivery?.Properties["UpgradeText"]?.Value as System.Collections.IEnumerable;
                if (upLines != null)
                {
                    var content = string.Join("\n", upLines.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Upgrade"), Kind = "FILE", Content = content });
                }
                else
                {
                    var f = _Resolve(req, DocumentKind.Upgrade);
                    if (f != null)
                    {
                        res.Items.Add(new DocumentItem { Title = BuildTitle(req, f.Name), Kind = "FILE", Path = f.FullName });
                    }
                }
            }
        }

        // Fill missing standard docs from repository if available
        try
        {
            bool wantRemoteBackfill = !string.IsNullOrWhiteSpace(req.ProjectUri);
            if (wantRemoteBackfill)
            {
                bool hasReadme = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("README", StringComparison.OrdinalIgnoreCase)));
                bool hasChlog  = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)));
                bool hasLic    = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)));

                if (!(hasReadme && hasChlog && hasLic))
                {
                    var info = RepoUrlParser.Parse(req.ProjectUri!);
                    var token = ResolveToken(req.RepositoryToken);
                    if (string.IsNullOrEmpty(token))
                    {
                        token = TokenStore.GetToken(info.Host) ?? string.Empty;
                    }
                    var client = RepoClientFactory.Create(info, token);
                    if (client != null)
                    {
                        string branch = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client.GetDefaultBranch() : req.RepositoryBranch!;
                        if (!hasReadme)
                        {
                            var readme = TryFetchFirst(client, branch, new[] { "README.md", "README.MD", "Readme.md" });
                            if (!string.IsNullOrEmpty(readme)) {
                                var di = MakeContentItem(req, "README", readme!);
                                di.Source = "Remote"; di.FileName = "README.md"; di.Title = "README";
                                res.Items.Add(di);
                            }
                        }
                        if (!hasChlog)
                        {
                            var ch = TryFetchFirst(client, branch, new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" });
                            if (!string.IsNullOrEmpty(ch)) {
                                var di = MakeContentItem(req, "CHANGELOG", ch!);
                                di.Source = "Remote"; di.FileName = "CHANGELOG.md"; di.Title = "CHANGELOG";
                                res.Items.Add(di);
                            }
                        }
                        if (!hasLic)
                        {
                            var lc = TryFetchFirst(client, branch, new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" });
                            if (!string.IsNullOrEmpty(lc)) {
                                var di = MakeContentItem(req, "LICENSE", lc!);
                                di.Source = "Remote"; di.FileName = "LICENSE"; di.Title = "LICENSE";
                                res.Items.Add(di);
                            }
                        }
                    }
                }

                // Include repository Docs/ folder similar to Internals\Docs
                try
                {
                    var info2 = RepoUrlParser.Parse(req.ProjectUri!);
                    var token2 = ResolveToken(req.RepositoryToken);
                    if (string.IsNullOrEmpty(token2))
                    {
                        token2 = TokenStore.GetToken(info2.Host) ?? string.Empty;
                    }
                    var client2 = RepoClientFactory.Create(info2, token2);
                    if (client2 != null)
                    {
                        string branch2 = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client2.GetDefaultBranch() : req.RepositoryBranch!;
                        var roots = (req.RepositoryPaths != null && req.RepositoryPaths.Length > 0)
                            ? req.RepositoryPaths
                            : new [] { "docs", "Docs" };

                        var collected = new List<(string Name, string Path)>();
                        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            foreach (var item in client2.ListFiles(root, branch2))
                            {
                                var n = item.Name;
                                if (n.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    collected.Add(item);
                                }
                            }
                        }
                        if (collected.Count > 0)
                        {
                            // Apply DocumentationOrder if provided
                            var orderArr = req.Delivery?.Properties["DocumentationOrder"]?.Value as System.Collections.IEnumerable;
                            var order = new List<string>();
                            if (orderArr != null)
                            {
                                foreach (var o in orderArr) { var s = o?.ToString(); if (!string.IsNullOrWhiteSpace(s)) order.Add(s!); }
                            }
                            IEnumerable<(string Name,string Path)> ordered;
                            if (order.Count > 0)
                            {
                                var map = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                                for (int i=0;i<order.Count;i++) map[order[i]] = i;
                                ordered = collected.OrderBy(f => map.ContainsKey(f.Name) ? map[f.Name] : int.MaxValue)
                                                   .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
                            }
                            else
                            {
                                ordered = collected.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
                            }

                            foreach (var f in ordered)
                            {
                                var content = client2.GetFileContent(f.Path, branch2);
                                if (string.IsNullOrEmpty(content)) continue;
                                res.Items.Add(new DocumentItem { Title = f.Name, Kind = "DOC", Content = content!, FileName = f.Name, Path = f.Path, Source = "Remote" });
                            }
                        }
                    }
                }
                catch { /* ignore repo Docs failures */ }
            }
        }
        catch { /* ignore remote backfill errors */ }

        // Extra: scripts tab (Internals/Scripts and standalone ps1 under Internals)
        try
        {
            if (!string.IsNullOrEmpty(req.InternalsBase) && Directory.Exists(req.InternalsBase))
            {
                var scriptRoots = new [] {
                    Path.Combine(req.InternalsBase!, "Scripts"),
                    req.InternalsBase!
                };
                foreach (var root in scriptRoots.Distinct())
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var f in Directory.GetFiles(root, "*.ps1", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(f);
                        string code;
                        try { code = File.ReadAllText(f); } catch { continue; }
                        // wrap as fenced code for HTML renderer
                        var md = $"```powershell\n{code}\n```";
                        res.Items.Add(new DocumentItem { Title = name, Kind = "SCRIPT", Content = md, FileName = name, Path = f });
                    }
                }
            }
        }
        catch { /* ignore scripts discovery errors */ }

        // Extra: docs tab (Internals/Docs/*.md)
        try
        {
            if (!string.IsNullOrEmpty(req.InternalsBase))
            {
                var docsRoot = Path.Combine(req.InternalsBase!, "Docs");
                if (Directory.Exists(docsRoot))
                {
                    var mdFiles = Directory.GetFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly)
                                            .Concat(Directory.GetFiles(docsRoot, "*.markdown", SearchOption.TopDirectoryOnly))
                                            .ToList();
                    // Optional ordering from delivery metadata
                    var orderArr = req.Delivery?.Properties["DocumentationOrder"]?.Value as System.Collections.IEnumerable;
                    var order = new List<string>();
                    if (orderArr != null)
                    {
                        foreach (var o in orderArr) { var s = o?.ToString(); if (!string.IsNullOrWhiteSpace(s)) order.Add(s!); }
                    }
                    IEnumerable<string> ordered;
                    if (order.Count > 0)
                    {
                        // First explicit order by file name (case-insensitive), then remaining alphabetically
                        var map = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                        for (int i=0;i<order.Count;i++) map[order[i]] = i;
                        ordered = mdFiles.OrderBy(f => map.ContainsKey(Path.GetFileName(f)) ? map[Path.GetFileName(f)] : int.MaxValue)
                                         .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        ordered = mdFiles.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (var f in ordered)
                    {
                        string content; try { content = File.ReadAllText(f); } catch { continue; }
                        res.Items.Add(new DocumentItem { Title = Path.GetFileName(f), Kind = "DOC", Content = content, FileName = Path.GetFileName(f), Path = f, Source = "Local" });
                    }
                }
            }
        }
        catch { /* ignore docs discovery errors */ }

        // Links
        var links = req.Delivery?.Properties["ImportantLinks"]?.Value as System.Collections.IEnumerable;
        if (links != null)
        {
            var md = new System.Text.StringBuilder();
            md.AppendLine("# Links");
            foreach (var l in links)
            {
                var p = l as PSObject; if (p == null) continue;
                var t = p.Properties["Title"]?.Value?.ToString() ?? p.Properties["Name"]?.Value?.ToString();
                var u = p.Properties["Url"]?.Value?.ToString();
                if (string.IsNullOrEmpty(u)) continue;
                if (!string.IsNullOrEmpty(t)) md.Append("- [").Append(t).Append("](").Append(u).AppendLine(")");
                else md.Append("- ").AppendLine(u);
            }
            if (md.Length > 0)
                res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Links"), Kind = "FILE", Content = md.ToString() });
        }

        return res;
    }

    private void AddKind(List<(string Kind,string Path)> items, Request req, DocumentKind kind, bool includeEvenIfSelected = true)
    {
        var f = _Resolve(req, kind);
        if (f != null) items.Add(("FILE", f.FullName));
    }

    private FileInfo? _Resolve(Request req, DocumentKind kind)
        => _finder.ResolveDocument((req.RootBase, req.InternalsBase, new DeliveryOptions()), kind, req.PreferInternals);

    private static string ResolveToken(string? explicitToken)
        => explicitToken
           ?? Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN")
           ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
           ?? Environment.GetEnvironmentVariable("PG_AZDO_PAT")
           ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT")
           ?? string.Empty;

    private static string? TryFetchFirst(IRepoClient client, string branch, string[] candidates)
    {
        foreach (var p in candidates)
        {
            var s = client.GetFileContent(p, branch);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }

    private static DocumentItem MakeContentItem(Request req, string name, string content)
        => new DocumentItem { Title = BuildTitle(req, name), Kind = "FILE", Content = content };

    private static string BuildTitle(Request req, string leaf)
        => !string.IsNullOrEmpty(req.TitleName) ? $"{req.TitleName} {req.TitleVersion} - {leaf}" : leaf;
}
