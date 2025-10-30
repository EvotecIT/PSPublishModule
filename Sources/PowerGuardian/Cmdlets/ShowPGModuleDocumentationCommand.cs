// ReSharper disable All
#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PowerGuardian;

/// <summary>
/// <para type="synopsis">Displays module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) in the console.</para>
/// <para type="description">Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent or when requested, it can fetch files directly from the module's repository specified by <c>PrivateData.PSData.ProjectUri</c> (GitHub or Azure DevOps), optionally using a Personal Access Token.</para>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -Readme -Changelog</code>
/// </example>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -FromRepository -RepositoryPaths docs -RepositoryBranch main</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Show, "ModuleDocumentation", DefaultParameterSetName = "ByName")]
[Alias("Show-Documentation")]
public sealed class ShowModuleDocumentationCommand : PSCmdlet
{
    /// <summary>Module name to display documentation for.</summary>
    [Parameter(ParameterSetName = "ByName", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    public string Name { get; set; }

    /// <summary>Module object to display documentation for. Alternative to <c>-Name</c>.</summary>
    [Parameter(ParameterSetName = "ByModule", ValueFromPipeline = true)]
    [Alias("InputObject", "ModuleInfo")]
    public PSModuleInfo Module { get; set; }

    /// <summary>Exact version to select when multiple module versions are installed.</summary>
    public Version RequiredVersion { get; set; }

    /// <summary>Direct path to a documentation folder containing README/CHANGELOG/etc.</summary>
    [Parameter(ParameterSetName = "ByPath")]
    public string DocsPath { get; set; }

    /// <summary>Path to a module root (folder that contains the module manifest). Useful for unpacked builds.</summary>
    [Parameter(ParameterSetName = "ByBase")]
    public string ModuleBase { get; set; }

    /// <summary>Show README.*.</summary>
    [Parameter] public SwitchParameter Readme { get; set; }
    /// <summary>Show CHANGELOG.*.</summary>
    [Parameter] public SwitchParameter Changelog { get; set; }
    /// <summary>Show LICENSE.*.</summary>
    [Parameter] public SwitchParameter License { get; set; }
    /// <summary>Show configured IntroText/IntroFile (from Delivery metadata).</summary>
    [Parameter] public SwitchParameter Intro { get; set; }
    /// <summary>Show configured UpgradeText/UpgradeFile (from Delivery metadata or UPGRADE.*).</summary>
    [Parameter] public SwitchParameter Upgrade { get; set; }
    /// <summary>Convenience switch to show Intro, README, CHANGELOG and LICENSE in order.</summary>
    [Parameter] public SwitchParameter All { get; set; }
    /// <summary>Display ImportantLinks defined in Delivery metadata at the end.</summary>
    [Parameter] public SwitchParameter Links { get; set; }
    /// <summary>Show a specific file by name (relative to module root or Internals) or full path.</summary>
    [Parameter] public string File { get; set; }
    /// <summary>Prefer Internals folder over module root when both contain the same file kind.</summary>
    [Parameter] public SwitchParameter PreferInternals { get; set; }
    /// <summary>List discovered documentation files (without rendering).</summary>
    [Parameter] public SwitchParameter List { get; set; }
    /// <summary>Print raw file content without Markdown rendering.</summary>
    [Parameter] public SwitchParameter Raw { get; set; }
    /// <summary>Open the chosen document in the default shell handler instead of rendering to console.</summary>
    [Parameter] public SwitchParameter Open { get; set; }
    /// <summary>Select JSON renderer for fenced JSON blocks: Auto, Spectre, or System.</summary>
    [Parameter]
    [ValidateSet("Auto","Spectre","System")]
    public string JsonRenderer { get; set; } = "Auto";
    /// <summary>Default language for unlabeled code fences (Auto, PowerShell, Json, None).</summary>
    [Parameter]
    [ValidateSet("Auto","PowerShell","Json","None")]
    public string DefaultCodeLanguage { get; set; } = "Auto";
    /// <summary>Heading rulers style. <c>H1AndH2</c> draws rules for H1/H2, <c>H1</c> for H1 only, <c>None</c> disables.</summary>
    [Parameter]
    [ValidateSet("None","H1","H1AndH2")]
    public string HeadingRules { get; set; } = "H1AndH2";
    /// <summary>Export rendered content to HTML file (tabbed). When omitted, no export is produced.</summary>
    [Parameter]
    public string ExportHtmlPath { get; set; }
    /// <summary>Open the exported HTML after rendering (requires -ExportHtmlPath or writes to a temp file).</summary>
    [Parameter]
    public SwitchParameter OpenHtml { get; set; }
    /// <summary>Disable code tokenizers and render code fences as plain text.</summary>
    [Parameter]
    public SwitchParameter DisableTokenizer { get; set; }

    // Remote repository support
    /// <summary>
    /// Pull documentation directly from the module repository (GitHub/Azure DevOps) based on <c>PrivateData.PSData.ProjectUri</c>.
    /// Use with -Readme/-Changelog/-License or -RepositoryPaths. Honors -RepositoryBranch and -RepositoryToken.
    /// </summary>
    [Parameter]
    public SwitchParameter FromRepository { get; set; }
    /// <summary>
    /// Prefer remote repository documents even if local files exist. Useful to view the current branch content.
    /// </summary>
    [Parameter]
    public SwitchParameter PreferRepository { get; set; }
    /// <summary>
    /// Branch name to use when fetching remote docs. If omitted, the provider default branch is used.
    /// </summary>
    [Parameter]
    public string RepositoryBranch { get; set; }
    /// <summary>
    /// Personal Access Token for private repositories. Alternatively set environment variables:
    /// GitHub: PG_GITHUB_TOKEN or GITHUB_TOKEN; Azure DevOps: PG_AZDO_PAT or AZURE_DEVOPS_EXT_PAT.
    /// </summary>
    [Parameter]
    public string RepositoryToken { get; set; }
    /// <summary>
    /// Repository-relative folders to enumerate and display (e.g., 'docs', 'articles').
    /// Only .md/.markdown/.txt files are rendered.
    /// </summary>
    [Parameter]
    public string[] RepositoryPaths { get; set; }

    // Remote repository support (legacy duplicates removed)

    protected override void ProcessRecord()
    {
        var pref = JsonRendererPreference.Auto;
        switch ((JsonRenderer ?? "Auto").ToLowerInvariant())
        {
            case "spectre": pref = JsonRendererPreference.Spectre; break;
            case "system":  pref = JsonRendererPreference.System;  break;
            default:         pref = JsonRendererPreference.Auto;    break;
        }
        string? defLang = null;
        switch ((DefaultCodeLanguage ?? "Auto").ToLowerInvariant())
        {
            case "powershell": defLang = "powershell"; break;
            case "json":       defLang = "json";       break;
            case "none":       defLang = "";           break; // keep empty
            default:            defLang = null;          break; // auto
        }
        // Renderer currently supports JsonRenderer + Default language only
        var renderer = new Renderer(pref, defLang);
        var finder   = new DocumentationFinder(this);
        string rootBase;
        string internalsBase;
        string titleName = null;
        string titleVersion = null;
        PSObject delivery = null;
        string projectUri = null;

        if (ParameterSetName == "ByPath")
        {
            if (string.IsNullOrWhiteSpace(DocsPath) || !Directory.Exists(DocsPath))
                throw new DirectoryNotFoundException($"DocsPath '{DocsPath}' not found.");
            rootBase = DocsPath!;
            var candidate = Path.Combine(DocsPath!, "Internals");
            internalsBase = Directory.Exists(candidate) ? candidate : null;
        }
        else if (ParameterSetName == "ByBase")
        {
            if (string.IsNullOrWhiteSpace(ModuleBase) || !Directory.Exists(ModuleBase))
                throw new DirectoryNotFoundException($"ModuleBase '{ModuleBase}' not found.");
            rootBase = ModuleBase;
            // Try to read manifest to get InternalsPath and title
            var manifestCandidates = Directory.GetFiles(ModuleBase, "*.psd1", SearchOption.TopDirectoryOnly);
            if (manifestCandidates.Length > 0)
            {
                var sb = this.InvokeCommand.NewScriptBlock("$m = Test-ModuleManifest -Path $args[0]; $m");
                var pso = sb.Invoke(manifestCandidates[0]).FirstOrDefault() as PSObject;
                if (pso != null)
                {
                    titleName = (pso.Properties["Name"]?.Value ?? pso.Properties["ModuleName"]?.Value)?.ToString();
                    titleVersion = pso.Properties["Version"]?.Value?.ToString();
                    delivery = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestCandidates[0]).FirstOrDefault() as PSObject;
                    projectUri = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ProjectUri").Invoke(manifestCandidates[0]).FirstOrDefault()?.ToString();
                    var internalsRel = delivery?.Properties["InternalsPath"]?.Value as string ?? "Internals";
                    var cand = Path.Combine(rootBase, internalsRel);
                    internalsBase = Directory.Exists(cand) ? cand : null;
                }
                else
                {
                    var cand = Path.Combine(rootBase, "Internals");
                    internalsBase = Directory.Exists(cand) ? cand : null;
                }
            }
            else
            {
                var cand = Path.Combine(rootBase, "Internals");
                internalsBase = Directory.Exists(cand) ? cand : null;
            }
        }
        else
        {
            // Resolve module by name/version or from provided PSModuleInfo
            if (Module != null)
            {
                rootBase = Module.ModuleBase;
                titleName = Module.Name;
                titleVersion = Module.Version.ToString();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Name))
                    throw new ArgumentException("Specify -Name or provide -Module.");
                var sb = this.InvokeCommand.NewScriptBlock("$m = Get-Module -ListAvailable -Name $args[0] | Sort-Object Version -Descending | Select-Object -First 1; if ($args[1]) { $m = Get-Module -ListAvailable -Name $args[0] | Where-Object { $_.Version -eq $args[1] } | Sort-Object Version -Descending | Select-Object -First 1 }; $m");
                var pso = sb.Invoke(Name, RequiredVersion).FirstOrDefault() as PSObject;
                if (pso == null)
                    throw new ItemNotFoundException($"Module '{Name}' not found.");
                rootBase = pso.Properties["ModuleBase"].Value?.ToString();
                titleName = pso.Properties["Name"]?.Value?.ToString();
                titleVersion = pso.Properties["Version"]?.Value?.ToString();
            }
            // Derive Internals path from manifest
            var manifestPath = Directory.GetFiles(rootBase, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (!string.IsNullOrEmpty(manifestPath))
            {
                delivery = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestPath).FirstOrDefault() as PSObject;
                projectUri = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ProjectUri").Invoke(manifestPath).FirstOrDefault()?.ToString();
                var internalsRel = delivery?.Properties["InternalsPath"]?.Value as string ?? "Internals";
                var cand = Path.Combine(rootBase, internalsRel);
                internalsBase = Directory.Exists(cand) ? cand : null;
            }
            else
            {
                var cand = Path.Combine(rootBase, "Internals");
                internalsBase = Directory.Exists(cand) ? cand : null;
            }
        }

        if (List)
        {
            var root = new DirectoryInfo(rootBase);
            if (root.Exists)
            {
                foreach (var f in root.GetFiles("README*").Concat(root.GetFiles("CHANGELOG*").Concat(root.GetFiles("LICENSE*"))))
                    WriteObject(new { Name = f.Name, FullName = f.FullName, Area = "Root" });
            }
            if (internalsBase != null)
            {
                var di = new DirectoryInfo(internalsBase);
                foreach (var f in di.GetFiles("README*").Concat(di.GetFiles("CHANGELOG*").Concat(di.GetFiles("LICENSE*"))))
                    WriteObject(new { Name = f.Name, FullName = f.FullName, Area = "Internals" });
            }
            return;
        }

        // Build additive items list (local)
        var items = new System.Collections.Generic.List<(string Kind,string Path)>();
        if (!string.IsNullOrEmpty(File))
        {
            string resolved = null;
            if (Path.IsPathRooted(File)) { if (!System.IO.File.Exists(File)) throw new FileNotFoundException($"File '{File}' not found."); resolved = File; }
            else {
                var t1 = Path.Combine(rootBase, File);
                var t2 = internalsBase != null ? Path.Combine(internalsBase, File) : null;
                if (System.IO.File.Exists(t1)) resolved = t1;
                else if (t2 != null && System.IO.File.Exists(t2)) resolved = t2;
                else throw new FileNotFoundException($"File '{File}' not found under root or Internals.");
            }
            items.Add(("FILE", resolved));
        }
        if (Intro) items.Add(("INTRO", null));
        if (All)
        {
            // Add Intro, Readme, Changelog, License in standard order
            if (!Intro) items.Add(("INTRO", null));
            var f1 = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Readme, PreferInternals);
            if (f1 != null && !Readme) items.Add(("FILE", f1.FullName));
            var f2 = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Changelog, PreferInternals);
            if (f2 != null && !Changelog) items.Add(("FILE", f2.FullName));
            var f3 = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.License, PreferInternals);
            if (f3 != null && !License) items.Add(("FILE", f3.FullName));
        }
        if (Readme)
        {
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Readme, PreferInternals);
            if (f != null) items.Add(("FILE", f.FullName));
        }
        if (Changelog)
        {
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Changelog, PreferInternals);
            if (f != null) items.Add(("FILE", f.FullName));
        }
        if (License)
        {
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.License, PreferInternals);
            if (f != null) items.Add(("FILE", f.FullName));
        }
        if (Upgrade) items.Add(("UPGRADE", null));

        // Remote fallback or preference
        var wantRemote = FromRepository.IsPresent || PreferRepository.IsPresent || items.Count == 0;
        var repoPathsFromDelivery = (delivery?.Properties["RepositoryPaths"]?.Value as System.Collections.IEnumerable)?.Cast<object>()?.Select(o => o?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var allRepoPaths = (RepositoryPaths != null && RepositoryPaths.Length > 0) ? RepositoryPaths : repoPathsFromDelivery;

        if (wantRemote && !string.IsNullOrWhiteSpace(projectUri))
        {
            var remoteToken = RepositoryToken;
            if (string.IsNullOrWhiteSpace(remoteToken))
            {
                // env fallbacks
                remoteToken = Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("PG_AZDO_PAT") ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT");
            }
            var info = RepoUrlParser.Parse(projectUri);
            string branch = !string.IsNullOrWhiteSpace(RepositoryBranch) ? RepositoryBranch : null;
            // Default files
            var candidatesReadme = new[] { "README.md", "README.MD", "Readme.md" };
            var candidatesChangelog = new[] { "CHANGELOG.md", "CHANGELOG.MD", "Changelog.md" };
            var candidatesLicense = new[] { "LICENSE", "LICENSE.md", "LICENSE.txt" };

            string FetchFirst(string[] candidates)
            {
                foreach (var p in candidates)
                {
                    var s = FetchFile(info, p, branch, remoteToken, ref branch);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                return null;
            }

            string readme = Readme.IsPresent || All.IsPresent ? FetchFirst(candidatesReadme) : null;
            string changelog = Changelog.IsPresent || All.IsPresent ? FetchFirst(candidatesChangelog) : null;
            string license = License.IsPresent || All.IsPresent ? FetchFirst(candidatesLicense) : null;

            if (items.Count == 0)
            {
                // No local items, show whatever we found remotely (prefer readme/changelog)
                if (!string.IsNullOrEmpty(readme))
                    renderer.ShowContent(!string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — README (remote)" : "README (remote)", readme, Raw);
                if (!string.IsNullOrEmpty(changelog))
                    renderer.ShowContent(!string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — CHANGELOG (remote)" : "CHANGELOG (remote)", changelog, Raw);
                if (!string.IsNullOrEmpty(license))
                    renderer.ShowContent(!string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — LICENSE (remote)" : "LICENSE (remote)", license, Raw);
            }

            // Additional repository paths from delivery or parameter: list and render all Markdown/text files
            if (allRepoPaths != null && allRepoPaths.Length > 0)
            {
                foreach (var rp in allRepoPaths)
                {
                    foreach (var (Name, Path) in ListFiles(info, rp, branch, remoteToken, ref branch))
                    {
                        var ext = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
                        if (!(ext == ".md" || ext == ".markdown" || ext == ".txt")) continue;
                        var content = FetchFile(info, Path, branch, remoteToken, ref branch);
                        if (string.IsNullOrEmpty(content)) continue;
                        var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — {Name} (remote)" : $"{Name} (remote)";
                        renderer.ShowContent(title, content, Raw);
                    }
                }
            }
        }

        if (Open)
        {
            var first = items.FirstOrDefault(i => i.Kind == "FILE");
            if (!string.IsNullOrEmpty(first.Path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(first.Path) { UseShellExecute = true });
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rootBase) { UseShellExecute = true });
            return;
        }

        foreach (var it in items)
        {
            if (it.Kind == "FILE")
            {
                var name = System.IO.Path.GetFileName(it.Path);
                var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — {name}" : name;
                renderer.ShowFile(title, it.Path, Raw);
                continue;
            }
            if (it.Kind == "INTRO")
            {
                var lines = delivery?.Properties["IntroText"]?.Value as System.Collections.IEnumerable;
                if (lines != null)
                {
                    var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — Introduction" : "Introduction";
                    renderer.WriteHeading(title);
                    foreach (var l in lines) Spectre.Console.AnsiConsole.MarkupLine(Spectre.Console.Markup.Escape(l?.ToString() ?? string.Empty));
                }
                continue;
            }
            if (it.Kind == "UPGRADE")
            {
                var lines = delivery?.Properties["UpgradeText"]?.Value as System.Collections.IEnumerable;
                if (lines != null)
                {
                    var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — Upgrade" : "Upgrade";
                    renderer.WriteHeading(title);
                    foreach (var l in lines) Spectre.Console.AnsiConsole.MarkupLine(Spectre.Console.Markup.Escape(l?.ToString() ?? string.Empty));
                }
                else
                {
                    var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Upgrade, PreferInternals);
                    if (f != null)
                    {
                        var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — {f.Name}" : f.Name;
                        renderer.ShowFile(title, f.FullName, Raw);
                    }
                }
                continue;
            }
        }
        // Optional HTML export
        if (!string.IsNullOrEmpty(ExportHtmlPath) || OpenHtml.IsPresent)
        {
            var exportItems = new System.Collections.Generic.List<DocumentItem>();
            foreach (var it in items)
            {
                if (it.Kind == "FILE")
                {
                    var title = System.IO.Path.GetFileName(it.Path);
                    string md;
                    try { md = System.IO.File.ReadAllText(it.Path); }
                    catch { md = $"# {title}\n\n(Unable to read file.)"; }
                    exportItems.Add(new DocumentItem { Title = title, Kind = "FILE", Content = md });
                }
                else if (it.Kind == "INTRO")
                {
                    var lines = delivery?.Properties["IntroText"]?.Value as System.Collections.IEnumerable;
                    if (lines != null)
                    {
                        var md = string.Join("\n", System.Linq.Enumerable.Select(lines.Cast<object>(), o => o?.ToString() ?? string.Empty));
                        exportItems.Add(new DocumentItem { Title = "Introduction", Kind = "INTRO", Content = md });
                    }
                }
                else if (it.Kind == "UPGRADE")
                {
                    var lines = delivery?.Properties["UpgradeText"]?.Value as System.Collections.IEnumerable;
                    if (lines != null)
                    {
                        var md = string.Join("\n", System.Linq.Enumerable.Select(lines.Cast<object>(), o => o?.ToString() ?? string.Empty));
                        exportItems.Add(new DocumentItem { Title = "Upgrade", Kind = "UPGRADE", Content = md });
                    }
                    else
                    {
                        var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Upgrade, PreferInternals);
                        if (f != null)
                        {
                            string md;
                            try { md = System.IO.File.ReadAllText(f.FullName); }
                            catch { md = "(Unable to read upgrade file)"; }
                            exportItems.Add(new DocumentItem { Title = f.Name, Kind = "UPGRADE", Content = md });
                        }
                    }
                }
            }
            if (Links)
            {
                var list = delivery?.Properties["ImportantLinks"]?.Value as System.Collections.IEnumerable;
                if (list != null)
                {
                    var lines = new System.Text.StringBuilder();
                    lines.AppendLine("# Links");
                    foreach (var l in list)
                    {
                        var p = l as System.Management.Automation.PSObject;
                        var title = p?.Properties["Title"]?.Value?.ToString() ?? p?.Properties["Name"]?.Value?.ToString();
                        var url = p?.Properties["Url"]?.Value?.ToString();
                        if (!string.IsNullOrEmpty(url))
                        {
                            lines.Append("- ");
                            if (!string.IsNullOrEmpty(title)) lines.Append('[').Append(title).Append("](").Append(url).Append(")");
                            else lines.Append(url);
                            lines.AppendLine();
                        }
                    }
                    exportItems.Add(new DocumentItem { Title = "Links", Kind = "LINKS", Content = lines.ToString() });
                }
            }
            if (exportItems.Count > 0)
            {
                var html = new HtmlExporter();
                var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion}" : (Name ?? Module?.Name ?? "Module");
                var path = html.Export(title, exportItems, ExportHtmlPath, OpenHtml);
                WriteVerbose($"HTML exported to {path}");
                WriteObject(path);
            }
        }
        if (Links)
        {
             var list = delivery?.Properties["ImportantLinks"]?.Value as System.Collections.IEnumerable;
             if (list != null)
             {
                 renderer.WriteHeading(!string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — Links" : "Links");
                 foreach (var l in list)
                 {
                     var p = l as System.Management.Automation.PSObject;
                     var title = p?.Properties["Title"]?.Value?.ToString() ?? p?.Properties["Name"]?.Value?.ToString();
                     var url = p?.Properties["Url"]?.Value?.ToString();
                     if (!string.IsNullOrEmpty(url))
                     {
                        Spectre.Console.AnsiConsole.MarkupLine($" - [link={Spectre.Console.Markup.Escape(url)}]{Spectre.Console.Markup.Escape(title ?? url)}[/]");
                     }
                 }
             }
        }
    }

    private static string FetchFile(RepoInfo info, string repoRelativePath, string branch, string token, ref string resolvedBranch)
    {
        try
        {
            switch (info.Host)
            {
                case RepoHost.GitHub:
                    var gh = new GitHubRepository(info.Owner, info.Repo, token);
                    if (string.IsNullOrWhiteSpace(resolvedBranch)) resolvedBranch = gh.GetDefaultBranch();
                    return gh.GetFileContent(repoRelativePath, resolvedBranch);
                case RepoHost.AzureDevOps:
                    var az = new AzureDevOpsRepository(info.Organization, info.Project, info.Repository, token);
                    if (string.IsNullOrWhiteSpace(resolvedBranch)) resolvedBranch = az.GetDefaultBranch();
                    return az.GetFileContent(repoRelativePath, resolvedBranch);
                default:
                    return null;
            }
        }
        catch { return null; }
    }

    private static System.Collections.Generic.List<(string Name,string Path)> ListFiles(RepoInfo info, string repoPath, string branch, string token, ref string resolvedBranch)
    {
        try
        {
            switch (info.Host)
            {
                case RepoHost.GitHub:
                    var gh = new GitHubRepository(info.Owner, info.Repo, token);
                    if (string.IsNullOrWhiteSpace(resolvedBranch)) resolvedBranch = gh.GetDefaultBranch();
                    return gh.ListFiles(repoPath, resolvedBranch);
                case RepoHost.AzureDevOps:
                    var az = new AzureDevOpsRepository(info.Organization, info.Project, info.Repository, token);
                    if (string.IsNullOrWhiteSpace(resolvedBranch)) resolvedBranch = az.GetDefaultBranch();
                    return az.ListFiles(repoPath, resolvedBranch);
                default:
                    return new System.Collections.Generic.List<(string,string)>();
            }
        }
        catch { return new System.Collections.Generic.List<(string,string)>(); }
    }
}
