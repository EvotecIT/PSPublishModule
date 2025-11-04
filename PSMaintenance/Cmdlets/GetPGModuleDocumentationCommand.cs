// ReSharper disable All
using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// <para type="synopsis">Gets module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) and renders it in the console.</para>
/// <para type="description">Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent or when requested, it can fetch files directly from the module's repository specified by <c>PrivateData.PSData.ProjectUri</c> (GitHub or Azure DevOps), optionally using a Personal Access Token.</para>
/// <example>
///   <code>Get-ModuleDocumentation -Name PSPublishModule</code>
/// </example>
/// <example>
///   <code>Get-ModuleDocumentation -Name PSPublishModule -Type All</code>
/// </example>
/// </summary>
[Cmdlet(VerbsCommon.Get, "ModuleDocumentation", DefaultParameterSetName = "ByName")]
public sealed partial class GetModuleDocumentationCommand : PSCmdlet
{
    /// <summary>
    /// Executes the cmdlet and writes formatted documentation to the console.
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
            case "none":       defLang = "";           break;
            default:            defLang = null;          break;
        }
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

        // Map -Type into fine-grained flags
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
                default: break;
            }
        }

        var planner = new DocumentationPlanner(finder);
        var reqObj = new DocumentationPlanner.Request
        {
            RootBase = rootBase,
            InternalsBase = internalsBase,
            Delivery = delivery,
            ProjectUri = projectUri,
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
        var plan = planner.Execute(reqObj);

        foreach (var di in plan.Items)
        {
            if (di.Kind == "FILE" && !string.IsNullOrEmpty(di.Path))
                renderer.ShowFile(di.Title, di.Path!, Raw);
            else
                renderer.ShowContent(di.Title, di.Content, Raw);
        }
    }
}
