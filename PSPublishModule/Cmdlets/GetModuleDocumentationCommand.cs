// ReSharper disable All
using System;
using System.Collections;
using System.Collections.Generic;
using PowerForge;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// <para type="synopsis">Gets module documentation (README, CHANGELOG, LICENSE, Intro/Upgrade) and renders it in the console.</para>
/// <para type="description">Resolves documentation files from an installed module (root or Internals folder) and renders them with Spectre.Console. When local files are absent, it will backfill from the module's repository specified by <c>PrivateData.PSData.ProjectUri</c> (GitHub or Azure DevOps), using a token when necessary.</para>
/// <example>
///   <summary>Show default documentation in the console</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Get-ModuleDocumentation -Name PSPublishModule</code>
///   <para>Resolves README/CHANGELOG/LICENSE and renders them in a readable console layout.</para>
/// </example>
/// <example>
///   <summary>Show all standard documents</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Get-ModuleDocumentation -Name PSPublishModule -Type All</code>
///   <para>Includes Introduction text (if present), plus README, CHANGELOG, and LICENSE.</para>
/// </example>
/// <example>
///   <summary>List available files without rendering</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Get-ModuleDocumentation -Name EFAdminManager -List</code>
///   <para>Enumerates candidate files found in the module root and Internals, for quick inspection.</para>
/// </example>
/// <example>
///   <summary>Prefer Internals copies of specific documents</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Get-ModuleDocumentation -Name EFAdminManager -Readme -License -PreferInternals</code>
///   <para>When both root and Internals versions exist, selects the Internals variant for display.</para>
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
        PSObject? repository = null;
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
                    delivery = AsPsObject(this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.Delivery").Invoke(manifestCandidates[0]).FirstOrDefault());
                    repository = AsPsObject(this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.Repository").Invoke(manifestCandidates[0]).FirstOrDefault());
                    projectUri = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ProjectUri").Invoke(manifestCandidates[0]).FirstOrDefault()?.ToString();
                    var internalsRel = GetPsObjectString(delivery, "InternalsPath") ?? "Internals";
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
                delivery = AsPsObject(this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.Delivery").Invoke(manifestPath).FirstOrDefault());
                repository = AsPsObject(this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.Repository").Invoke(manifestPath).FirstOrDefault());
                projectUri = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.ProjectUri").Invoke(manifestPath).FirstOrDefault()?.ToString();
                var internalsRel = GetPsObjectString(delivery, "InternalsPath") ?? "Internals";
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

        // Pull repository defaults from manifest if not passed via parameters
        string? branchToUse = RepositoryBranch;
        string[]? pathsToUse = RepositoryPaths;
        try
        {
            if (string.IsNullOrWhiteSpace(branchToUse) && repository != null)
            {
                var b = GetPsObjectString(repository, "Branch");
                if (!string.IsNullOrWhiteSpace(b)) branchToUse = b;
            }
            if ((pathsToUse == null || pathsToUse.Length == 0) && repository != null)
            {
                var values = GetPsObjectStringArray(repository, "Paths");
                if (values.Length > 0) pathsToUse = values;
            }
        }
        catch { }

        var wantsRemote = !string.IsNullOrWhiteSpace(projectUri);

        var planner = new DocumentationPlanner(finder);
        var reqObj = new DocumentationPlanner.Request
        {
            RootBase = rootBase,
            InternalsBase = internalsBase,
            Delivery = delivery,
            ProjectUri = projectUri,
            RepositoryBranch = branchToUse,
            RepositoryToken = RepositoryToken,
            RepositoryPaths = pathsToUse,
            Online = wantsRemote,
            PreferInternals = PreferInternals,
            Readme = Readme,
            Changelog = Changelog,
            License = License,
            Intro = Intro,
            Upgrade = Upgrade,
            All = All,
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

    private static PSObject? AsPsObject(object? value)
    {
        if (value == null) return null;
        return value as PSObject ?? PSObject.AsPSObject(value);
    }

    private static object? GetPsObjectValue(PSObject? value, string name)
    {
        if (value == null || string.IsNullOrWhiteSpace(name)) return null;

        if (value.BaseObject is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key != null && string.Equals(entry.Key.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
        }

        try
        {
            var exact = value.Properties[name]?.Value;
            if (exact != null) return exact;
        }
        catch { }

        var property = value.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return property?.Value;
    }

    private static string? GetPsObjectString(PSObject? value, string name)
    {
        var raw = GetPsObjectValue(value, name);
        return raw?.ToString();
    }

    private static string[] GetPsObjectStringArray(PSObject? value, string name)
    {
        var raw = GetPsObjectValue(value, name);
        if (raw is string text)
            return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text };

        if (raw is IEnumerable enumerable)
        {
            var list = new List<string>();
            foreach (var item in enumerable)
            {
                var textItem = item?.ToString();
                if (!string.IsNullOrWhiteSpace(textItem)) list.Add(textItem!);
            }
            return list.ToArray();
        }

        return Array.Empty<string>();
    }
}
