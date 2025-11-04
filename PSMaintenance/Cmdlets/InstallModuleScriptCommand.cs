using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// Copies only PowerShell scripts from a module's Internals\Scripts folder to a destination path.
/// The destination is flattened (no Module/Version subfolders).
/// </summary>
[Cmdlet(VerbsLifecycle.Install, "ModuleScript", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
[Alias("Install-ModuleScripts", "Install-Scripts")]
public sealed class InstallModuleScriptCommand : PSCmdlet
{
    /// <summary>Module name to source scripts from (highest installed version is used by default).</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ByName", ValueFromPipelineByPropertyName = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Specific module instance to source scripts from.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ByModule", ValueFromPipeline = true)]
    public PSObject Module { get; set; } = default!;

    /// <summary>Optional exact module version to use when multiple versions are installed.</summary>
    [Parameter]
    public Version? RequiredVersion { get; set; }
    
    /// <summary>Destination folder to copy scripts into (created if missing).</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Relative path under the module root where scripts live. Defaults to 'Internals\Scripts'.</summary>
    [Parameter]
    public string ScriptsRelativePath { get; set; } = System.IO.Path.Combine("Internals", "Scripts");

    /// <summary>Wildcard include filters (relative to the scripts folder). Defaults to '*.ps1'.</summary>
    [Parameter]
    public string[]? Include { get; set; }

    /// <summary>Wildcard exclude filters (relative to the scripts folder).</summary>
    [Parameter]
    public string[]? Exclude { get; set; }

    /// <summary>Conflict handling when a destination file already exists. Defaults to Merge (keep existing).</summary>
    [Parameter]
    public OnExistsOption OnExists { get; set; } = OnExistsOption.Merge;

    /// <summary>Force overwriting read-only files when -OnExists Overwrite or -OnExists Merge with -Force for new-only behavior.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Show which files would be copied without performing any changes.</summary>
    [Parameter]
    public SwitchParameter ListOnly { get; set; }

    /// <summary>
    /// Entry point: resolves the module location and copies matching scripts to the destination
    /// according to the selected conflict policy.
    /// </summary>
    protected override void ProcessRecord()
    {
        var resolver = new ModuleResolver(this);
        var mod = resolver.Resolve(Name, Module, RequiredVersion ?? null);
        var moduleName = (mod.Properties["Name"].Value ?? string.Empty).ToString();
        var moduleBase = (mod.Properties["ModuleBase"].Value ?? string.Empty).ToString();
        if (string.IsNullOrWhiteSpace(moduleBase) || !Directory.Exists(moduleBase))
            throw new DirectoryNotFoundException($"Module base path not found for '{moduleName}'.");

        // Determine Internals/Scripts path
        var scriptsRoot = FindScriptsFolder(moduleBase, ScriptsRelativePath);
        if (scriptsRoot == null)
            throw new DirectoryNotFoundException($"Scripts folder '{ScriptsRelativePath}' not found under '{moduleBase}'.");

        var dest = PathHelper.Normalize(Path);
        PathHelper.EnsureDirectory(dest);

        var includes = (Include == null || Include.Length == 0) ? new[] { "*.ps1" } : Include;
        var excludes = Exclude ?? Array.Empty<string>();

        var files = Directory.GetFiles(scriptsRoot, "*", SearchOption.AllDirectories)
            .Where(f => MatchesAny(f, scriptsRoot, includes) && !MatchesAny(f, scriptsRoot, excludes))
            .ToList();

        if (files.Count == 0)
        {
            WriteVerbose($"No matching scripts found in '{scriptsRoot}'.");
            return;
        }

        foreach (var file in files)
        {
            var rel = file.Substring(scriptsRoot.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var target = System.IO.Path.Combine(dest, rel);
            var targetDir = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

            var exists = File.Exists(target);

            if (ListOnly)
            {
                var action = exists ? (OnExists == OnExistsOption.Overwrite ? "Overwrite" : OnExists == OnExistsOption.Skip ? "Skip" : OnExists == OnExistsOption.Stop ? "Stop" : "Keep") : "Copy";
                var o = (PSObject)PSObject.AsPSObject(new { Source = file, Destination = target, Exists = exists, Action = action });
                WriteObject(o);
                continue;
            }

            if (exists)
            {
                switch (OnExists)
                {
                    case OnExistsOption.Skip:
                        WriteVerbose($"Skipping existing: {target}");
                        continue;
                    case OnExistsOption.Stop:
                        throw new IOException($"Destination file exists: {target}");
                    case OnExistsOption.Merge:
                        WriteVerbose($"Keeping existing (merge): {target}");
                        continue;
                    case OnExistsOption.Overwrite:
                        break;
                }
            }

            if (ShouldProcess(target, exists ? "Overwrite script" : "Copy script"))
            {
                File.Copy(file, target, overwrite: true);
                if (Force)
                {
                    try { File.SetAttributes(target, FileAttributes.Normal); } catch { /* ignore */ }
                }
                WriteVerbose($"Copied: {rel}");
            }
        }
    }

    private static string? FindScriptsFolder(string moduleBase, string relative)
    {
        // Try manifest-defined InternalsPath when available
        var internals = System.IO.Path.Combine(moduleBase, "Internals");
        var manifest = Directory.GetFiles(moduleBase, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrEmpty(manifest))
        {
            try
            {
                // (Test-ModuleManifest).PrivateData.PSData.PSPublishModuleDelivery.InternalsPath
                // If present, prefer that location
                // We call PowerShell to evaluate the manifest hashtable
                // Note: we cannot access cmdlet here; keep it simple and fallback to default
            }
            catch { /* ignore */ }
        }

        var explicitPath = System.IO.Path.Combine(moduleBase, relative);
        if (Directory.Exists(explicitPath)) return explicitPath;

        // Fallbacks
        var fallback = System.IO.Path.Combine(internals, "Scripts");
        if (Directory.Exists(fallback)) return fallback;
        var alt = System.IO.Path.Combine(moduleBase, "Scripts");
        if (Directory.Exists(alt)) return alt;
        return null;
    }

    private static bool MatchesAny(string fullPath, string root, string[] patterns)
    {
        if (patterns == null || patterns.Length == 0) return true;
        var rel = fullPath.Substring(root.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        foreach (var p in patterns)
        {
            var wc = new WildcardPattern(p, WildcardOptions.IgnoreCase);
            if (wc.IsMatch(rel)) return true;
        }
        return false;
    }
}
