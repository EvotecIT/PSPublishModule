using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PowerGuardian;

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
        public bool PreferRepository { get; set; }
        public bool FromRepository { get; set; }
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

        // Remote repo fetch
        var wantRemote = req.FromRepository || req.PreferRepository || items.Count == 0;
        if (wantRemote && (!string.IsNullOrWhiteSpace(req.ProjectUri) || clientOverride != null))
        {
            var client = clientOverride;
            if (client == null)
            {
                var info = RepoUrlParser.Parse(req.ProjectUri!);
                var token = ResolveToken(req.RepositoryToken);
                client = RepoClientFactory.Create(info, token);
            }
            if (client != null)
            {
                string branch = string.IsNullOrWhiteSpace(req.RepositoryBranch) ? client.GetDefaultBranch() : req.RepositoryBranch!;
                // Default remote files
                var readme = TryFetchFirst(client, branch, new[] { "README.md", "README.MD", "Readme.md" });
                var changelog = TryFetchFirst(client, branch, new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" });
                var license = TryFetchFirst(client, branch, new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" });

                // Render remote regardless of local presence if -PreferRepository/-FromRepository
                bool anyRemote = false;
                if (!string.IsNullOrEmpty(readme) && (req.Readme || req.All || !hasSelectors))
                {
                    res.Items.Add(MakeContentItem(req, "README (remote)", readme!)); anyRemote = true;
                }
                if (!string.IsNullOrEmpty(changelog) && (req.Changelog || req.All))
                {
                    res.Items.Add(MakeContentItem(req, "CHANGELOG (remote)", changelog!)); anyRemote = true;
                }
                if (!string.IsNullOrEmpty(license) && (req.License || req.All))
                {
                    res.Items.Add(MakeContentItem(req, "LICENSE (remote)", license!)); anyRemote = true;
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
                            res.Items.Add(MakeContentItem(req, $"{Name} (remote)", content!)); anyRemote = true;
                        }
                    }
                }
                res.UsedRemote = anyRemote;
                if (req.PreferRepository && anyRemote)
                {
                    // Clear local FILE items (keep INTRO/UPGRADE placeholders)
                    items = items.Where(i => i.Kind != "FILE").ToList();
                }
            }
        }

        // Resolve local items into DocumentItem list
        foreach (var it in items)
        {
            if (it.Kind == "FILE")
            {
                var fileName = System.IO.Path.GetFileName(it.Path);
                var title = BuildTitle(req, fileName);
                res.Items.Add(new DocumentItem { Title = title, Kind = "FILE", Path = it.Path });
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
        => !string.IsNullOrEmpty(req.TitleName) ? $"{req.TitleName} {req.TitleVersion} — {leaf}" : leaf;
}
