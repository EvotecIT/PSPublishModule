using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Plans which documents to show/export based on local availability and repository settings.
/// </summary>
internal sealed partial class DocumentationPlanner
{
    private readonly DocumentationFinder _finder;
    public DocumentationPlanner(DocumentationFinder finder) => _finder = finder;

    internal sealed class Request
    {
        public string RootBase { get; set; } = string.Empty;
        public string? InternalsBase { get; set; }
        public object? Delivery { get; set; }
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
        public bool Links { get; set; }
        public bool All { get; set; }
        public bool Online { get; set; }
        public DocumentationMode Mode { get; set; } = DocumentationMode.PreferLocal;
        public bool ShowDuplicates { get; set; }
        public string? SingleFile { get; set; }
        public string? TitleName { get; set; }
        public string? TitleVersion { get; set; }
        public System.Collections.Generic.IEnumerable<string>? FormatsToProcess { get; set; }
        public System.Collections.Generic.IEnumerable<string>? TypesToProcess { get; set; }
        public System.Collections.Generic.IEnumerable<string>? DocsPaths { get; set; }
        public string? LocalChangelogPath { get; set; }
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
        var effectiveRepositoryBranch = ResolveRepositoryBranch(req, clientOverride);
        var hasSelectors = req.Readme || req.Changelog || req.License || req.All || req.Intro || req.Upgrade || req.Links || !string.IsNullOrEmpty(req.SingleFile);
        var includeSupplementalSections = !hasSelectors || req.All;
        var includeReleases = !hasSelectors || req.All || req.Changelog;

        // Specific file selection
        if (!string.IsNullOrEmpty(req.SingleFile))
        {
            var t1 = Path.Combine(req.RootBase, req.SingleFile);
            var t2 = req.InternalsBase != null ? Path.Combine(req.InternalsBase, req.SingleFile) : null;
            if (File.Exists(t1)) items.Add(("FILE", t1));
            else if (t2 != null && File.Exists(t2)) items.Add(("FILE", t2));
            else if (req.Online)
            {
                var remoteClient = ResolveRepoClient(req, clientOverride);
                var branch = effectiveRepositoryBranch ?? remoteClient?.GetDefaultBranch() ?? "main";
                var remoteItem = remoteClient is null ? null : TryCreateRemoteSingleFileItem(req, remoteClient, branch);
                if (remoteItem is not null)
                {
                    res.Items.Add(remoteItem);
                    res.UsedRemote = true;
                }
                else
                {
                    throw new FileNotFoundException($"File '{req.SingleFile}' not found under root, Internals, or repository.");
                }
            }
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
        if (!hasSelectors)
        {
            AddKind(items, req, DocumentKind.Readme);
            AddKind(items, req, DocumentKind.Changelog);
            AddKind(items, req, DocumentKind.License);
            AddKind(items, req, DocumentKind.Upgrade);
            // Add intro when IntroText/IntroFile present
            var introLines = GetDeliveryValue(req.Delivery, "IntroText") as System.Collections.IEnumerable;
            var introFile = GetDeliveryValue(req.Delivery, "IntroFile") as string;
            if (introLines != null || !string.IsNullOrEmpty(introFile)) items.Add(("INTRO", string.Empty));
        }

        // Remote repo fetch only when Online is requested
        var wantRemote = req.Online;
        if (wantRemote && (!string.IsNullOrWhiteSpace(req.ProjectUri) || clientOverride != null))
        {
            var client = ResolveRepoClient(req, clientOverride);
            if (client != null)
            {
                string branch = effectiveRepositoryBranch ?? client.GetDefaultBranch();
                bool anyRemote = false;
                // Extra paths
                if (includeSupplementalSections && req.RepositoryPaths != null)
                {
                    foreach (var rp in req.RepositoryPaths)
                    {
                            foreach (var (Name, Path) in client.ListFiles(rp, branch))
                            {
                                var lowerName = (Name ?? string.Empty).ToLowerInvariant();
                                var ext = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
                                var content = client.GetFileContent(Path, branch);
                                if (string.IsNullOrEmpty(content)) continue;

                                if (lowerName.StartsWith("about_") && lowerName.EndsWith(".help.txt"))
                                {
                                    res.Items.Add(new DocumentItem
                                    {
                                        Title = Name ?? string.Empty,
                                        Kind = "ABOUT",
                                        Content = AboutToMarkdown(content!),
                                        FileName = Name,
                                        Path = Path,
                                        Source = "Remote",
                                        BaseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch)
                                    });
                                    anyRemote = true;
                                    continue;
                                }

                                if (IsCommunityFile(lowerName))
                                {
                                    res.Items.Add(new DocumentItem
                                    {
                                        Title = Name ?? string.Empty,
                                        Kind = "COMMUNITY",
                                        Content = RepositoryContentNormalizer.RewriteRelativeUris(content!, RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch), RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch), Path),
                                        FileName = Name,
                                        Path = Path,
                                        Source = "Remote",
                                        BaseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch)
                                    });
                                    anyRemote = true;
                                    continue;
                                }

                            if (ext == ".md" || ext == ".markdown" || ext == ".txt" || ext == ".help" || ext == ".help.txt")
                            {
                                var baseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch);
                                var blobBaseUri = RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch);
                                var kind = RepositoryContentNormalizer.IsLikelyTemplateSource(Name, content!)
                                    ? "DOCSOURCE"
                                    : "DOC";
                                var normalizedContent = string.Equals(kind, "DOCSOURCE", StringComparison.OrdinalIgnoreCase)
                                    ? RepositoryContentNormalizer.WrapAsSourceCodeBlock(content!, "markdown")
                                    : RepositoryContentNormalizer.RewriteRelativeUris(content!, baseUri, blobBaseUri, Path);

                                // Treat repository path content as documentation pages, not standard tabs.
                                res.Items.Add(new DocumentItem
                                {
                                    Title = Name ?? string.Empty,
                                    Kind = kind,
                                    Content = normalizedContent,
                                    FileName = Name,
                                    Path = Path,
                                    Source = "Remote",
                                    BaseUri = baseUri
                                });
                                anyRemote = true;
                            }
                        }
                    }
                }
                res.UsedRemote = res.UsedRemote || anyRemote;
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
                var content = string.Empty;
                try
                {
                    if (!string.IsNullOrWhiteSpace(it.Path) && File.Exists(it.Path))
                    {
                        content = File.ReadAllText(it.Path);
                        if (TryResolveLocalRewriteBases(req, clientOverride, ref effectiveRepositoryBranch, content, out var rawBaseUri, out var blobBaseUri))
                        {
                            var repositoryPath = GetRepositoryRelativePath(req, it.Path);
                            content = RepositoryContentNormalizer.RewriteRelativeUris(
                                content,
                                rawBaseUri,
                                blobBaseUri,
                                repositoryPath);
                        }
                    }
                }
                catch { }

                res.Items.Add(new DocumentItem
                {
                    Title = title,
                    Kind = "FILE",
                    Path = it.Path,
                    FileName = fileName,
                    Source = "Local",
                    Content = content,
                    BaseUri = BuildKnownBranchRawBase(req.ProjectUri, effectiveRepositoryBranch)
                });
                continue;
            }
            if (it.Kind == "INTRO")
            {
                var introLines = GetDeliveryValue(req.Delivery, "IntroText") as System.Collections.IEnumerable;
                var introFile = GetDeliveryValue(req.Delivery, "IntroFile") as string;
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
                var upLines = GetDeliveryValue(req.Delivery, "UpgradeText") as System.Collections.IEnumerable;
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

        // About topics are part of the default/all view, but explicit selectors should stay narrow.
        if (includeSupplementalSections)
        {
            try
            {
                var aboutFiles = _finder.ResolveAboutTopics((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.DocsPaths);
                foreach (var f in aboutFiles)
                {
                    var title = BuildTitle(req, StripAboutExtensions(f.Name));
                    string raw;
                    try { raw = System.IO.File.ReadAllText(f.FullName); }
                    catch { continue; }
                    res.Items.Add(new DocumentItem { Title = title, Kind = "ABOUT", Path = f.FullName, FileName = f.Name, Content = AboutToMarkdown(raw), Source = "Local" });
                }
            }
            catch { }
        }

        // Formats and Types (local)
        if (includeSupplementalSections)
        {
            try
            {
                var formats = _finder.ResolveFormatFiles((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.FormatsToProcess);
                foreach (var f in formats)
                {
                    var content = System.IO.File.ReadAllText(f.FullName);
                    res.Items.Add(new DocumentItem
                    {
                        Title = BuildTitle(req, f.Name),
                        Kind = "FORMAT",
                        Path = f.FullName,
                        FileName = f.Name,
                        Content = RepositoryContentNormalizer.WrapAsSourceCodeBlock(content, "text"),
                        Source = "Local"
                    });
                }

                var types = _finder.ResolveTypesFiles((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.TypesToProcess);
                foreach (var f in types)
                {
                    var content = System.IO.File.ReadAllText(f.FullName);
                    res.Items.Add(new DocumentItem
                    {
                        Title = BuildTitle(req, f.Name),
                        Kind = "TYPE",
                        Path = f.FullName,
                        FileName = f.Name,
                        Content = RepositoryContentNormalizer.WrapAsSourceCodeBlock(content, "text"),
                        Source = "Local"
                    });
                }
            }
            catch { }
        }

        // Community files (local)
        if (includeSupplementalSections)
        {
            try
            {
                var community = _finder.ResolveCommunityFiles((req.RootBase, req.InternalsBase, new DeliveryOptions()), req.DocsPaths);
                foreach (var f in community)
                {
                    string content; try { content = File.ReadAllText(f.FullName); } catch { continue; }
                    if (TryResolveLocalRewriteBases(req, clientOverride, ref effectiveRepositoryBranch, content, out var rawBaseUri, out var blobBaseUri))
                    {
                        var repositoryPath = GetRepositoryRelativePath(req, f.FullName);
                        content = RepositoryContentNormalizer.RewriteRelativeUris(
                            content,
                            rawBaseUri,
                            blobBaseUri,
                            repositoryPath);
                    }
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, f.Name), Kind = "COMMUNITY", Path = f.FullName, FileName = f.Name, Content = content, Source = "Local", BaseUri = BuildKnownBranchRawBase(req.ProjectUri, effectiveRepositoryBranch) });
                }
            }
            catch { }
        }

        // Remote standard docs (README/CHANGELOG/LICENSE)
        try
        {
            bool wantRemoteFetch = req.Online && !string.IsNullOrWhiteSpace(req.ProjectUri);
            if (wantRemoteFetch)
            {
                bool hasReadme = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("README", StringComparison.OrdinalIgnoreCase)));
                bool hasChlog  = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)));
                bool hasLic    = res.Items.Any(i => i.Kind == "FILE" && ((i.FileName ?? i.Title).StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)));
                bool forceRemoteStandard = (req.Mode == DocumentationMode.All || req.Mode == DocumentationMode.PreferRemote);
                bool wantsRemoteReadme = !hasSelectors || req.All || req.Readme;
                bool wantsRemoteChangelog = !hasSelectors || req.All || req.Changelog;
                bool wantsRemoteLicense = !hasSelectors || req.All || req.License;
                bool wantsAnyRemoteStandard = wantsRemoteReadme || wantsRemoteChangelog || wantsRemoteLicense;

                var client = wantsAnyRemoteStandard ? ResolveRepoClient(req, clientOverride) : null;
                if (client != null)
                {
                    string branch = effectiveRepositoryBranch ?? client.GetDefaultBranch();
                    if (wantsRemoteReadme && (forceRemoteStandard || !hasReadme))
                    {
                        var readme = TryFetchFirst(client, branch, new[] { "README.md", "README.MD", "Readme.md" });
                        if (!string.IsNullOrEmpty(readme)) { var di = MakeContentItem(req, "README", RepositoryContentNormalizer.RewriteRelativeUris(readme!, RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch), RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch))); di.Source = "Remote"; di.FileName = "README.md"; di.Title = "README"; res.Items.Add(di); res.UsedRemote = true; }
                    }
                    if (wantsRemoteChangelog && (forceRemoteStandard || !hasChlog))
                    {
                        var ch = TryFetchFirst(client, branch, new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" });
                        if (!string.IsNullOrEmpty(ch)) { var di = MakeContentItem(req, "CHANGELOG", RepositoryContentNormalizer.RewriteRelativeUris(ch!, RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch), RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch))); di.Source = "Remote"; di.FileName = "CHANGELOG.md"; di.Title = "CHANGELOG"; res.Items.Add(di); res.UsedRemote = true; }
                    }
                    if (wantsRemoteLicense && (forceRemoteStandard || !hasLic))
                    {
                        var lc = TryFetchFirst(client, branch, new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" });
                        if (!string.IsNullOrEmpty(lc)) { var di = MakeContentItem(req, "LICENSE", lc!); di.Source = "Remote"; di.FileName = "LICENSE"; di.Title = "LICENSE"; res.Items.Add(di); res.UsedRemote = true; }
                    }
                }

                // Include repository Docs/ folder similar to Internals\Docs
                try
                {
                    var client2 = includeSupplementalSections ? ResolveRepoClient(req, clientOverride) : null;
                    if (client2 != null && (req.RepositoryPaths == null || req.RepositoryPaths.Length == 0))
                    {
                        string branch2 = effectiveRepositoryBranch ?? client2.GetDefaultBranch();
                        var roots = new[] { "docs", "Docs" };

                        var collected = new List<(string Name, string Path)>();
                        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            foreach (var item in client2.ListFiles(root, branch2))
                            {
                                var n = item.Name ?? string.Empty;
                                if (n.StartsWith("about_", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    collected.Add(item);
                                    continue;
                                }
                                if (n.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".help", StringComparison.OrdinalIgnoreCase))
                                {
                                    collected.Add(item);
                                }
                            }
                        }
                        if (collected.Count > 0)
                        {
                            // Apply DocumentationOrder if provided
                            var orderArr = GetDeliveryValue(req.Delivery, "DocumentationOrder") as System.Collections.IEnumerable;
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
                                if (f.Name.StartsWith("about_", StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    res.Items.Add(new DocumentItem { Title = f.Name, Kind = "ABOUT", Content = AboutToMarkdown(content!), FileName = f.Name, Path = f.Path, Source = "Remote", BaseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch2) });
                                    continue;
                                }
                                if (IsCommunityFile(f.Name))
                                {
                                    res.Items.Add(new DocumentItem { Title = f.Name, Kind = "COMMUNITY", Content = RepositoryContentNormalizer.RewriteRelativeUris(content!, RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch2), RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch2), f.Path), FileName = f.Name, Path = f.Path, Source = "Remote", BaseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch2) });
                                    continue;
                                }
                                var baseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, branch2);
                                var blobBaseUri = RepositoryContentNormalizer.BuildBlobBase(req.ProjectUri, branch2);
                                var kind = RepositoryContentNormalizer.IsLikelyTemplateSource(f.Name, content!)
                                    ? "DOCSOURCE"
                                    : "DOC";
                                var normalizedContent = string.Equals(kind, "DOCSOURCE", StringComparison.OrdinalIgnoreCase)
                                    ? RepositoryContentNormalizer.WrapAsSourceCodeBlock(content!, "markdown")
                                    : RepositoryContentNormalizer.RewriteRelativeUris(content!, baseUri, blobBaseUri, f.Path);
                                res.Items.Add(new DocumentItem { Title = f.Name, Kind = kind, Content = normalizedContent, FileName = f.Name, Path = f.Path, Source = "Remote", BaseUri = baseUri });
                            }
                        }
                    }
                }
                catch { /* ignore repo Docs failures */ }
            }
        }
        catch { /* ignore remote backfill errors */ }

        // Extra: scripts tab (Internals/Scripts and standalone ps1 under Internals)
        if (includeSupplementalSections)
        {
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
        }

        // Extra: docs tab (Internals/Docs/*.md)
        if (includeSupplementalSections)
        {
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
                        var orderArr = GetDeliveryValue(req.Delivery, "DocumentationOrder") as System.Collections.IEnumerable;
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
                            var fileName = Path.GetFileName(f);
                            var kind = RepositoryContentNormalizer.IsLikelyTemplateSource(fileName, content)
                                ? "DOCSOURCE"
                                : "DOC";
                            var rawBaseUri = default(string);
                            var blobBaseUri = default(string);
                            effectiveRepositoryBranch = ResolveRepositoryBranchForLocalRewrite(req, clientOverride, effectiveRepositoryBranch, content);
                            var repositoryPath = GetRepositoryRelativePath(req, f);
                            var normalizedContent = string.Equals(kind, "DOCSOURCE", StringComparison.OrdinalIgnoreCase)
                                ? RepositoryContentNormalizer.WrapAsSourceCodeBlock(content, "markdown")
                                : TryResolveLocalRewriteBases(req, clientOverride, ref effectiveRepositoryBranch, content, out rawBaseUri, out blobBaseUri)
                                    ? RepositoryContentNormalizer.RewriteRelativeUris(
                                    content,
                                    rawBaseUri,
                                    blobBaseUri,
                                    repositoryPath)
                                    : content;
                            res.Items.Add(new DocumentItem { Title = fileName, Kind = kind, Content = normalizedContent, FileName = fileName, Path = f, Source = "Local", BaseUri = BuildKnownBranchRawBase(req.ProjectUri, effectiveRepositoryBranch) });
                        }
                    }
                }
            }
            catch { /* ignore docs discovery errors */ }
        }

        // Links
        if (includeSupplementalSections || req.Links)
        {
            var links = GetDeliveryValue(req.Delivery, "ImportantLinks") as System.Collections.IEnumerable;
            if (links != null)
            {
                var md = new System.Text.StringBuilder();
                md.AppendLine("# Links");
                foreach (var l in links)
                {
                    var t = GetDeliveryValue(l, "Title")?.ToString() ?? GetDeliveryValue(l, "Name")?.ToString();
                    var u = GetDeliveryValue(l, "Url")?.ToString() ?? GetDeliveryValue(l, "Link")?.ToString();
                    if (string.IsNullOrEmpty(u)) continue;
                    if (!string.IsNullOrEmpty(t)) md.Append("- [").Append(t).Append("](").Append(u).AppendLine(")");
                    else md.Append("- ").AppendLine(u);
                }
                if (md.Length > 0)
                    res.Items.Add(new DocumentItem { Title = BuildTitle(req, "Links"), Kind = "FILE", Content = md.ToString() });
            }
        }

        // Release summary derived from CHANGELOG
        if (includeReleases)
        {
            try
            {
                string? changelogContent = null;
                var remoteChangelog = res.Items.FirstOrDefault(i => string.Equals(i.Kind, "FILE", StringComparison.OrdinalIgnoreCase) && string.Equals(i.FileName, "CHANGELOG.md", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Source, "Remote", StringComparison.OrdinalIgnoreCase));
                if (remoteChangelog != null) changelogContent = remoteChangelog.Content;
                if (string.IsNullOrEmpty(changelogContent))
                {
                    if (!string.IsNullOrEmpty(req.LocalChangelogPath) && File.Exists(req.LocalChangelogPath))
                    {
                        changelogContent = File.ReadAllText(req.LocalChangelogPath);
                    }
                }
                if (string.IsNullOrEmpty(changelogContent))
                {
                    var localChlog = res.Items.FirstOrDefault(i => string.Equals(i.Kind, "FILE", StringComparison.OrdinalIgnoreCase) && (i.FileName ?? i.Title)?.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase) == true && string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase));
                    if (localChlog != null)
                    {
                        changelogContent = string.IsNullOrEmpty(localChlog.Content) && !string.IsNullOrEmpty(localChlog.Path) && File.Exists(localChlog.Path)
                            ? File.ReadAllText(localChlog.Path!)
                            : localChlog.Content;
                    }
                }
                if (!string.IsNullOrEmpty(changelogContent))
                {
                    var parsedReleases = ParseChangelogReleases(changelogContent!);
                    if (parsedReleases.Count > 0 && !string.IsNullOrWhiteSpace(req.ProjectUri))
                    {
                        if (req.Online || clientOverride != null)
                        {
                            var repoReleases = GetNormalizedRepoReleases(req, clientOverride);
                            if (repoReleases.Count > 0)
                            {
                                parsedReleases = MergeReleaseMetadata(parsedReleases, repoReleases);
                            }
                        }
                        parsedReleases = NormalizeRepoReleases(parsedReleases, req.ProjectUri);
                    }
                    if (parsedReleases.Count > 0)
                    {
                        res.Items.Add(new DocumentItem
                        {
                            Title = BuildTitle(req, "Releases"),
                            Kind = "RELEASES",
                            Content = BuildReleaseSummaryMarkdown(parsedReleases),
                            Releases = parsedReleases,
                            Source = string.IsNullOrEmpty(req.ProjectUri) ? "Local" : "Derived"
                        });
                    }
                }
                // If changelog not present, try repo releases API
                if ((req.Online || clientOverride != null) && res.Items.All(i => !string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(req.ProjectUri))
                {
                    try
                    {
                        var normalizedReleases = GetNormalizedRepoReleases(req, clientOverride);
                        if (normalizedReleases.Count > 0)
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine("# Releases (repository API)");
                            foreach (var r in normalizedReleases)
                            {
                                sb.Append("## ").Append(string.IsNullOrEmpty(r.Name) ? r.Tag : r.Name);
                                if (r.PublishedAt.HasValue) sb.Append(" (" + r.PublishedAt.Value.ToString("yyyy-MM-dd") + ")");
                                sb.AppendLine();
                                if (!string.IsNullOrWhiteSpace(r.Body))
                                {
                                    sb.AppendLine(r.Body.Trim()).AppendLine();
                                }
                                if (r.Assets.Count > 0)
                                {
                                    sb.AppendLine("### Assets");
                                    foreach (var a in r.Assets)
                                    {
                                        sb.Append("- [").Append(a.Name).Append("](").Append(a.DownloadUrl).Append(")");
                                        if (a.Size.HasValue) sb.Append($" ({a.Size.Value / 1024} KB)");
                                        if (!string.IsNullOrEmpty(a.ContentType)) sb.Append($" {a.ContentType}");
                                        sb.AppendLine();
                                    }
                                    sb.AppendLine();
                                }
                            }
                            res.Items.Add(new DocumentItem
                            {
                                Title = BuildTitle(req, "Releases"),
                                Kind = "RELEASES",
                                Content = sb.ToString(),
                                Releases = normalizedReleases,
                                Source = "Remote",
                                BaseUri = RepositoryContentNormalizer.BuildRawBase(req.ProjectUri, normalizedReleases.FirstOrDefault()?.Tag)
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return res;
    }

    private static string? ResolveRepositoryBranch(Request req, IRepoClient? clientOverride)
    {
        if (!string.IsNullOrWhiteSpace(req.RepositoryBranch))
        {
            return req.RepositoryBranch!.Trim();
        }

        if (!req.Online || clientOverride == null)
        {
            return null;
        }

        try
        {
            var branch = clientOverride.GetDefaultBranch();
            return string.IsNullOrWhiteSpace(branch) ? null : branch!.Trim();
        }
        catch
        {
            return null;
        }
    }

}
