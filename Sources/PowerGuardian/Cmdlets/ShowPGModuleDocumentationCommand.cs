// ReSharper disable All
#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PowerGuardian;

[Cmdlet(VerbsCommon.Show, "PGModuleDocumentation", DefaultParameterSetName = "ByName")]
public sealed class ShowPGModuleDocumentationCommand : PSCmdlet
{
    [Parameter(ParameterSetName = "ByName", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    public string Name { get; set; }

    [Parameter(ParameterSetName = "ByModule", ValueFromPipeline = true)]
    [Alias("InputObject", "ModuleInfo")]
    public PSModuleInfo Module { get; set; }

    public Version RequiredVersion { get; set; }

    [Parameter(ParameterSetName = "ByPath")]
    public string DocsPath { get; set; }

    [Parameter(ParameterSetName = "ByBase")]
    public string ModuleBase { get; set; }

    [Parameter] public SwitchParameter Readme { get; set; }
    [Parameter] public SwitchParameter Changelog { get; set; }
    [Parameter] public SwitchParameter License { get; set; }
    [Parameter] public SwitchParameter Intro { get; set; }
    [Parameter] public SwitchParameter Upgrade { get; set; }
    [Parameter] public string File { get; set; }
    [Parameter] public SwitchParameter PreferInternals { get; set; }
    [Parameter] public SwitchParameter List { get; set; }
    [Parameter] public SwitchParameter Raw { get; set; }
    [Parameter] public SwitchParameter Open { get; set; }

    protected override void ProcessRecord()
    {
        var renderer = new Renderer();
        var finder   = new DocumentationFinder(this);
        string rootBase;
        string internalsBase;
        string titleName = null;
        string titleVersion = null;

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
                    var del = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestCandidates[0]).FirstOrDefault() as PSObject;
                    var internalsRel = del?.Properties["InternalsPath"]?.Value as string ?? "Internals";
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
                var del = this.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestPath).FirstOrDefault() as PSObject;
                var internalsRel = del?.Properties["InternalsPath"]?.Value as string ?? "Internals";
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

        string? target = null;
        if (!string.IsNullOrEmpty(File))
        {
            if (Path.IsPathRooted(File))
            {
                if (!System.IO.File.Exists(File)) throw new FileNotFoundException($"File '{File}' not found.");
                target = File;
            }
            else
            {
                var t1 = Path.Combine(rootBase, File);
                var t2 = internalsBase != null ? Path.Combine(internalsBase, File) : null;
                if (System.IO.File.Exists(t1)) target = t1;
                else if (t2 != null && System.IO.File.Exists(t2)) target = t2;
                else throw new FileNotFoundException($"File '{File}' not found under root or Internals.");
            }
        }
        else if (Readme)
        {
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Readme, PreferInternals);
            if (f != null) target = f.FullName; else throw new FileNotFoundException("README not found.");
        }
        else if (Changelog)
        {
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Changelog, PreferInternals);
            if (f != null) target = f.FullName; else throw new FileNotFoundException("CHANGELOG not found.");
        }
        else if (License)
        {
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.License, PreferInternals);
            if (f != null) target = f.FullName; else throw new FileNotFoundException("LICENSE not found.");
        }
        else
        {
            // default: README else CHANGELOG
            var f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Readme, PreferInternals);
            if (f == null) f = finder.ResolveDocument((rootBase, internalsBase, new DeliveryOptions()), DocumentKind.Changelog, PreferInternals);
            if (f != null) target = f.FullName; else throw new FileNotFoundException("No README or CHANGELOG found.");
        }

        if (Open)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target!) { UseShellExecute = true });
            return;
        }

        var title = !string.IsNullOrEmpty(titleName) ? $"{titleName} {titleVersion} — {System.IO.Path.GetFileName(target)}" : System.IO.Path.GetFileName(target);
        renderer.ShowFile(title, target!, Raw);
    }
}
