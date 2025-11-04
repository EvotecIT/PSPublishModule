// ReSharper disable All
using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// <para type="synopsis">Displays module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) in the console.</para>
/// <para type="description">Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent or when requested, it can fetch files directly from the module's repository specified by <c>PrivateData.PSData.ProjectUri</c> (GitHub or Azure DevOps), optionally using a Personal Access Token.</para>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -Readme -Changelog</code>
/// </example>
/// <example>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -Readme -Changelog -PreferInternals</code>
/// </example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -PreferRepository -RepositoryPaths 'docs' -RepositoryBranch 'main'</code>
/// </example>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -PreferRepository -RepositoryToken 'ghp_xxx'</code>
/// </example>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -PreferRepository -RepositoryPaths 'Docs/en-US' -RepositoryBranch 'main'</code>
/// <example>
///   <code>Show-ModuleDocumentation -Module (Get-Module -ListAvailable EFAdminManager) -All</code>
/// </example>
/// <example>
///   <code>Show-ModuleDocumentation -ModuleBase 'C:\\Program Files\\WindowsPowerShell\\Modules\\EFAdminManager\\3.0.0' -Readme</code>
/// </example>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -ExamplesMode Raw</code>
/// </example>
/// <example>
///   <code>Show-ModuleDocumentation -Name EFAdminManager -ExamplesLayout ProseFirst</code>
/// </example>
/// </example>
/// <example>
///   <code>Set-ModuleDocumentation -FromEnvironment; Show-ModuleDocumentation -Name EFAdminManager -PreferRepository</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Show, "ModuleDocumentation", DefaultParameterSetName = "ByName")]
[Alias("Show-Documentation")]
public sealed partial class ShowModuleDocumentationCommand : PSCmdlet
{

    // Remote repository support (legacy duplicates removed)

    /// <summary>
    /// Executes the cmdlet processing logic and renders requested documents.
    /// </summary>
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
        string? internalsBase;
        string? titleName = null;
        string? titleVersion = null;
        PSObject? delivery = null;
        string? projectUri = null;

        string? manifestPathUsed = null;
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
            rootBase = ModuleBase!;
            // Try to read manifest to get InternalsPath and title
            var manifestCandidates = Directory.GetFiles(ModuleBase!, "*.psd1", SearchOption.TopDirectoryOnly);
            if (manifestCandidates.Length > 0)
            {
                var sb = this.InvokeCommand.NewScriptBlock("$m = Test-ModuleManifest -Path $args[0]; $m");
                manifestPathUsed = manifestCandidates[0];
                var pso = sb.Invoke(manifestPathUsed).FirstOrDefault() as PSObject;
                if (pso != null)
                {
                    titleName = (pso.Properties["Name"]?.Value ?? pso.Properties["ModuleName"]?.Value)?.ToString();
                    titleVersion = pso.Properties["Version"]?.Value?.ToString();
                    delivery = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestPathUsed).FirstOrDefault() as PSObject;
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
                rootBase = pso.Properties["ModuleBase"].Value?.ToString() ?? throw new InvalidOperationException("ModuleBase not found in manifest.");
                titleName = pso.Properties["Name"]?.Value?.ToString();
                titleVersion = pso.Properties["Version"]?.Value?.ToString();
            }
            // Derive Internals path from manifest
            var manifestPath = Directory.GetFiles(rootBase, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (!string.IsNullOrEmpty(manifestPath))
            {
                manifestPathUsed = manifestPath;
                delivery = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestPathUsed).FirstOrDefault() as PSObject;
                projectUri = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ProjectUri").Invoke(manifestPathUsed).FirstOrDefault()?.ToString();
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

        // Pull repository hints from delivery metadata when parameters are not specified
        string? branchToUse = RepositoryBranch;
        string[]? pathsToUse = RepositoryPaths;
        try
        {
            if (string.IsNullOrWhiteSpace(branchToUse) && delivery != null)
            {
                string? b = null;
                try { b = delivery.Properties["RepositoryBranch"]?.Value?.ToString(); } catch { }
                if (string.IsNullOrWhiteSpace(b))
                {
                    // Case-insensitive fallback
                    var prop = delivery.Properties.FirstOrDefault(pp => string.Equals(pp.Name, "RepositoryBranch", StringComparison.OrdinalIgnoreCase));
                    b = prop?.Value?.ToString();
                }
                if (string.IsNullOrWhiteSpace(b) && !string.IsNullOrEmpty(manifestPathUsed))
                {
                    // Hashtable-safe manifest fallback
                    var sbGet = this.InvokeCommand.NewScriptBlock("$d = (Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery; if ($d -is [hashtable]) { $d['RepositoryBranch'] } else { $d.RepositoryBranch }");
                    b = sbGet.Invoke(manifestPathUsed).FirstOrDefault()?.ToString();
                }
                if (!string.IsNullOrWhiteSpace(b)) branchToUse = b;
            }
            if ((pathsToUse == null || pathsToUse.Length == 0) && delivery != null)
            {
                System.Collections.IEnumerable? arr = null;
                try { arr = delivery.Properties["RepositoryPaths"]?.Value as System.Collections.IEnumerable; } catch { }
                if (arr == null)
                {
                    var prop = delivery.Properties.FirstOrDefault(pp => string.Equals(pp.Name, "RepositoryPaths", StringComparison.OrdinalIgnoreCase));
                    arr = prop?.Value as System.Collections.IEnumerable;
                }
                if (arr == null && !string.IsNullOrEmpty(manifestPathUsed))
                {
                    var sbGet = this.InvokeCommand.NewScriptBlock("$d = (Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery; if ($d -is [hashtable]) { $d['RepositoryPaths'] } else { $d.RepositoryPaths }");
                    var res = sbGet.Invoke(manifestPathUsed).FirstOrDefault();
                    arr = res as System.Collections.IEnumerable;
                }
                if (arr != null)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var o in arr) { var s = o?.ToString(); if (!string.IsNullOrWhiteSpace(s)) list.Add(s!); }
                    if (list.Count > 0) pathsToUse = list.ToArray();
                }
            }
        }
        catch { }

        // (-List removed; HTML viewer renders the full page.)

        // Fast mode maps to all skip flags
        if (Fast.IsPresent) { SkipDependencies = true; SkipCommands = true; }

        WriteVerbose("Resolving module and manifest...");

        // No persisted settings file - rely on explicit parameters and environment variables

        // Build module metadata from manifest
        ModuleInfoModel meta = new ModuleInfoModel();
        var manifestPathMeta = System.IO.Directory.GetFiles(rootBase, "*.psd1", System.IO.SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrEmpty(manifestPathMeta))
        {
            var psoFull = this.InvokeCommand.NewScriptBlock("Test-ModuleManifest -Path $args[0]").Invoke(manifestPathMeta).FirstOrDefault() as PSObject;
            if (psoFull != null)
            {
                meta.Name = (psoFull.Properties["Name"]?.Value ?? psoFull.Properties["ModuleName"]?.Value)?.ToString() ?? (titleName ?? "Module");
                meta.Version = psoFull.Properties["Version"]?.Value?.ToString() ?? (titleVersion ?? "");
                meta.Description = psoFull.Properties["Description"]?.Value?.ToString();
                meta.Author = psoFull.Properties["Author"]?.Value?.ToString();
                meta.PowerShellVersion = psoFull.Properties["PowerShellVersion"]?.Value?.ToString();
                // PSData
                meta.ProjectUri = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ProjectUri").Invoke(manifestPathMeta).FirstOrDefault()?.ToString();
                meta.IconUri    = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.IconUri").Invoke(manifestPathMeta).FirstOrDefault()?.ToString();
                var rla = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.RequireLicenseAcceptance").Invoke(manifestPathMeta).FirstOrDefault();
                if (rla != null) meta.RequireLicenseAcceptance = (rla.BaseObject as bool?) ?? null;

                // RequiredModules (hashtables or strings)
                var reqVal = psoFull.Properties["RequiredModules"]?.Value;
                foreach (var item in EnumerateItems(reqVal))
                {
                    var p = item as PSObject;
                    if (p?.BaseObject is System.Collections.IDictionary dict)
                    {
                        var dep = new ModuleDependency { Kind = ModuleDependencyKind.Required };
                        if (dict.Contains("ModuleName")) dep.Name = (dict["ModuleName"]?.ToString() ?? string.Empty).Trim();
                        if (dict.Contains("ModuleVersion")) dep.Version = dict["ModuleVersion"]?.ToString();
                        if (dict.Contains("Guid")) dep.Guid = dict["Guid"]?.ToString();
                        if (!string.IsNullOrEmpty(dep.Name)) meta.Dependencies.Add(dep);
                    }
                    else
                    {
                        var name = item?.ToString()?.Trim(); if (!string.IsNullOrEmpty(name)) meta.Dependencies.Add(new ModuleDependency { Kind = ModuleDependencyKind.Required, Name = name! });
                    }
                }

                // ExternalModuleDependencies
                var extObj = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ExternalModuleDependencies").Invoke(manifestPathMeta).FirstOrDefault();
                var extVal = extObj is PSObject pso ? pso.BaseObject : extObj;
                foreach (var e in EnumerateItems(extVal))
                {
                    var name = e?.ToString()?.Trim(); if (!string.IsNullOrEmpty(name)) meta.Dependencies.Add(new ModuleDependency { Kind = ModuleDependencyKind.External, Name = name! });
                }

                // Enrich direct dependencies with installed manifest info (Version/Guid if missing)
                foreach (var d in meta.Dependencies)
                {
                    if (string.IsNullOrEmpty(d.Name)) continue;
                    if (!string.IsNullOrEmpty(d.Version) && !string.IsNullOrEmpty(d.Guid)) continue;
                    var sbDir = this.InvokeCommand.NewScriptBlock(@"$m = Get-Module -ListAvailable -Name $args[0] | Sort-Object Version -Descending | Select-Object -First 1; if ($m) { Test-ModuleManifest -Path (Join-Path $m.ModuleBase ($m.Name + '.psd1')) } else { $null }");
                    var dirMan = sbDir.Invoke(d.Name).FirstOrDefault() as PSObject; if (dirMan != null)
                    {
                        d.Version = d.Version ?? dirMan.Properties["Version"]?.Value?.ToString();
                        d.Guid    = d.Guid    ?? dirMan.Properties["Guid"]?.Value?.ToString();
                    }
                }

                // Optionally enrich one level deep using installed manifests (children)
                foreach (var d in meta.Dependencies.ToList())
                {
                    if (string.IsNullOrEmpty(d.Name)) continue;
                    var sbDep = this.InvokeCommand.NewScriptBlock(@"$m = Get-Module -ListAvailable -Name $args[0] | Sort-Object Version -Descending | Select-Object -First 1; if ($m) { Test-ModuleManifest -Path (Join-Path $m.ModuleBase ($m.Name + '.psd1')) } else { $null }");
                    var depMan = sbDep.Invoke(d.Name).FirstOrDefault() as PSObject; if (depMan == null) continue;
                    var req2Val = depMan.Properties["RequiredModules"]?.Value;
                    foreach (var item in EnumerateItems(req2Val))
                    {
                        var p2 = item as PSObject;
                        if (p2?.BaseObject is System.Collections.IDictionary dict2)
                        {
                            var dep2 = new ModuleDependency { Kind = ModuleDependencyKind.Required };
                            if (dict2.Contains("ModuleName")) dep2.Name = (dict2["ModuleName"]?.ToString() ?? string.Empty).Trim();
                            if (dict2.Contains("ModuleVersion")) dep2.Version = dict2["ModuleVersion"]?.ToString();
                            if (dict2.Contains("Guid")) dep2.Guid = dict2["Guid"]?.ToString();
                            if (!string.IsNullOrEmpty(dep2.Name)) d.Children.Add(dep2);
                        }
                        else
                        {
                            var name2 = item?.ToString()?.Trim(); if (!string.IsNullOrEmpty(name2)) d.Children.Add(new ModuleDependency { Kind = ModuleDependencyKind.Required, Name = name2! });
                        }
                    }
                }
            }
        }

        // Verbose: remote repository intent and inputs (without leaking secrets)
        // Legacy mapping (one-time warnings could be added here)
        bool legacyRepoPaths  = (RepositoryPaths != null && RepositoryPaths.Length > 0);
        bool wantsRemote = Online.IsPresent || legacyRepoPaths;
        if (wantsRemote)
        {
            if (string.IsNullOrWhiteSpace(projectUri))
            {
                WriteVerbose("Repository requested but manifest PrivateData.PSData.ProjectUri is empty.");
            }
            else
            {
                var info = RepoUrlParser.Parse(projectUri!);
                if (info.Host == RepoHost.Unknown)
                {
                    WriteVerbose($"Repository URI could not be parsed: {projectUri}");
                    if ((projectUri ?? string.Empty).IndexOf("dev.azure.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        WriteVerbose("Expected Azure DevOps URL form: https://dev.azure.com/{organization}/{project}/_git/{repository}");
                    }
                }
                else
                {
                    if (info.Host == RepoHost.GitHub)
                    {
                        WriteVerbose($"Repository host: GitHub, Owner: {info.Owner}, Repo: {info.Repo}");
                    }
                    else if (info.Host == RepoHost.AzureDevOps)
                    {
                        WriteVerbose($"Repository host: Azure DevOps, Org: {info.Organization}, Project: {info.Project}, Repo: {info.Repository}");
                    }
                    var branchMsg = string.IsNullOrWhiteSpace(branchToUse) ? "(default branch)" : branchToUse;
                    WriteVerbose($"Branch requested: {branchMsg}");
                    if (pathsToUse != null && pathsToUse.Length > 0)
                    {
                        WriteVerbose($"Repository paths: {string.Join(", ", pathsToUse)}");
                    }
                    // Token source (do not print secrets)
                    bool tokenFromParam = !string.IsNullOrWhiteSpace(RepositoryToken);
                    bool tokenFromEnv = !tokenFromParam && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN"))
                                                             || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                                                             || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PG_AZDO_PAT"))
                                                             || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT")));
                    bool tokenFromStore = false;
                    try { tokenFromStore = !tokenFromParam && !tokenFromEnv && (TokenStore.GetToken(info.Host) != null); } catch { }
                    var tokenSrc = tokenFromParam ? "parameter" : tokenFromEnv ? "environment" : tokenFromStore ? "stored" : "none";
                    WriteVerbose($"Authentication token source: {tokenSrc}");
                }
            }
        }

        // Centralized plan: HTML/Word only (no console rendering)
        var planner = new DocumentationPlanner(finder);
        var reqObj = new DocumentationPlanner.Request
        {
            RootBase = rootBase,
            InternalsBase = internalsBase,
            Delivery = delivery,
            ProjectUri = wantsRemote ? projectUri : null,
            RepositoryBranch = branchToUse,
            RepositoryToken = RepositoryToken,
            RepositoryPaths = pathsToUse,
            PreferInternals = PreferInternals,
            Online = wantsRemote,
            Mode = (Mode ?? (wantsRemote ? "All" : "PreferLocal")).Equals("PreferRemote", System.StringComparison.OrdinalIgnoreCase)
                ? DocumentationMode.PreferRemote
                : (Mode ?? (wantsRemote ? "All" : "PreferLocal")).Equals("All", System.StringComparison.OrdinalIgnoreCase)
                    ? DocumentationMode.All
                    : DocumentationMode.PreferLocal,
            ShowDuplicates = ShowDuplicates.IsPresent,
            SingleFile = File,
            TitleName = titleName,
            TitleVersion = titleVersion
        };
        var modeLabel = reqObj.Online ? ($"Online/{reqObj.Mode}") : "LocalOnly";
        WriteVerbose($"Planning documents (mode: {modeLabel})...");
        var plan = planner.Execute(reqObj);

        // Verbose summary of repository/local docs discovered
        try
        {
            var remoteDocs = plan.Items.Where(i => string.Equals(i.Source, "Remote", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Kind, "DOC", StringComparison.OrdinalIgnoreCase)).Count();
            var localDocs  = plan.Items.Where(i => string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase)  && string.Equals(i.Kind, "DOC", StringComparison.OrdinalIgnoreCase)).Count();
            if (wantsRemote)
            {
                WriteVerbose($"Repository docs discovered: {remoteDocs}");
                if (remoteDocs == 0 && RepositoryPaths != null && RepositoryPaths.Length > 0)
                {
                    WriteVerbose("No repository docs found at the requested paths. Verify branch and folder names.");
                }
            }
            if (localDocs > 0) { WriteVerbose($"Internals docs discovered: {localDocs}"); }
        }
        catch { }

        var finalExportItems = new System.Collections.Generic.List<DocumentItem>();
        foreach (var di in plan.Items)
        {
            if (string.Equals(di.Kind, "FILE", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(di.Path))
            {
                if (string.IsNullOrEmpty(di.Content)) di.Content = System.IO.File.ReadAllText(di.Path!);
            }
            finalExportItems.Add(di);
        }

        // Prepare exporter options
        meta.SkipDependencies = SkipDependencies.IsPresent;
        meta.SkipCommands = SkipCommands.IsPresent;
        meta.MaxCommands = MaxCommands;
        meta.HelpTimeoutSeconds = HelpTimeoutSeconds;
        meta.HelpAsCode = HelpAsCode.IsPresent;
        meta.Online = wantsRemote;
        meta.Mode = reqObj.Mode;
        meta.ShowDuplicates = ShowDuplicates.IsPresent;
        switch ((ExamplesMode ?? "Auto").ToLowerInvariant())
        {
            case "raw":  meta.ExamplesMode = PSMaintenance.ExamplesMode.Raw; break;
            case "maml": meta.ExamplesMode = PSMaintenance.ExamplesMode.Maml; break;
            default:      meta.ExamplesMode = PSMaintenance.ExamplesMode.Auto; break;
        }
        switch ((ExamplesLayout ?? "ProseFirst").ToLowerInvariant())
        {
            case "prosefirst": meta.ExamplesLayout = PSMaintenance.ExamplesLayout.ProseFirst; break;
            case "allascode": meta.ExamplesLayout = PSMaintenance.ExamplesLayout.AllAsCode; break;
            default:           meta.ExamplesLayout = PSMaintenance.ExamplesLayout.ProseFirst; break;
        }

        WriteVerbose(meta.SkipDependencies ? "Skipping dependency processing." : $"Dependencies discovered: {meta.Dependencies.Count}");
        WriteVerbose(meta.SkipCommands ? "Skipping Commands tab." : $"Commands will be rendered (max {meta.MaxCommands}, per-help timeout {meta.HelpTimeoutSeconds}s).");

        // Always export HTML (Word can be added later). If no path provided, write to temp.
        var html = new HtmlExporter();
        var open = !DoNotShow.IsPresent; // default is to open
        WriteVerbose("Rendering HTML...");
        var path = html.Export(meta, finalExportItems, OutputPath, open, s => { if (!string.IsNullOrEmpty(s) && s.StartsWith("[WARN]")) { WriteWarning(s.Substring(6)); } else { WriteVerbose(s); } });
        WriteVerbose($"HTML exported to {path}");
        WriteObject(path);
        return;
    }

    // (No settings file reader - intentionally omitted)

    // Ensure we don't iterate strings as char sequences
    private static System.Collections.Generic.IEnumerable<object?> EnumerateItems(object? value)
    {
        if (value == null) yield break;
        if (value is string s)
        {
            yield return s;
            yield break;
        }
        if (value is System.Collections.IEnumerable en)
        {
            foreach (var x in en) yield return x;
            yield break;
        }
        yield return value;
    }
}
