// ReSharper disable All
#nullable enable
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
                rootBase = pso.Properties["ModuleBase"].Value?.ToString() ?? throw new InvalidOperationException("ModuleBase not found in manifest.");
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

        // Map -Type high-level selection into fine-grained flags when specified
        if (Type != DocumentationSelection.Default)
        {
            Readme = false; Changelog = false; License = false; Intro = false; Upgrade = false; All = false;
            switch (Type)
            {
                case DocumentationSelection.All: All = true; break;
                case DocumentationSelection.Readme: Readme = true; break;
                case DocumentationSelection.Changelog: Changelog = true; break;
                case DocumentationSelection.License: License = true; break;
                case DocumentationSelection.Intro: Intro = true; break;
                case DocumentationSelection.Upgrade: Upgrade = true; break;
                case DocumentationSelection.Links: /* handled later when rendering */ break;
                default: /* Default no-op */ break;
            }
        }

        // Fast mode maps to all skip flags
        if (Fast.IsPresent) { SkipRemote = true; SkipDependencies = true; SkipCommands = true; }

        WriteVerbose("Resolving module and manifest...");

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

        // Centralized plan: HTML/Word only (no console rendering)
        var planner = new DocumentationPlanner(finder);
        var reqObj = new DocumentationPlanner.Request
        {
            RootBase = rootBase,
            InternalsBase = internalsBase,
            Delivery = delivery,
            ProjectUri = SkipRemote.IsPresent ? null : projectUri,
            RepositoryBranch = RepositoryBranch,
            RepositoryToken = RepositoryToken,
            RepositoryPaths = RepositoryPaths,
            PreferInternals = PreferInternals,
            Readme = Readme,
            Changelog = Changelog,
            License = License,
            Intro = Intro,
            Upgrade = Upgrade,
            All = All,
            PreferRepository = PreferRepository,
            FromRepository = FromRepository,
            SingleFile = File,
            TitleName = titleName,
            TitleVersion = titleVersion
        };
        WriteVerbose("Planning documents (local + remote backfill if enabled)...");
        var plan = planner.Execute(reqObj);

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

        WriteVerbose(meta.SkipDependencies ? "Skipping dependency processing." : $"Dependencies discovered: {meta.Dependencies.Count}");
        WriteVerbose(meta.SkipCommands ? "Skipping Commands tab." : $"Commands will be rendered (max {meta.MaxCommands}, per-help timeout {meta.HelpTimeoutSeconds}s).");

        // Always export HTML (Word can be added later). If no path provided, write to temp.
        var html = new HtmlExporter();
        var open = !DoNotShow.IsPresent; // default is to open
        WriteVerbose("Rendering HTML...");
        var path = html.Export(meta, finalExportItems, OutputPath, open, s => WriteVerbose(s));
        WriteVerbose($"HTML exported to {path}");
        WriteObject(path);
        return;
    }

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
