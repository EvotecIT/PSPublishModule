using System;
using PowerForge;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace PSPublishModule;

/// <summary>
/// Copies only PowerShell scripts from a module's Internals\Scripts folder to a destination path.
/// The destination is flattened (no Module/Version subfolders).
/// </summary>
/// <example>
///   <summary>Copy PowerShell scripts from Internals\Scripts to a tools folder</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Install-ModuleScript -Name EFAdminManager -Path 'C:\Tools' -Verbose</code>
///   <para>Copies PowerShell script files under Internals\Scripts recursively into C:\Tools, preserving subfolders. Shows each copied file.</para>
/// </example>
/// <example>
///   <summary>Copy only specific scripts, unblocking and overwriting</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Install-ModuleScript -Name EFAdminManager -Path 'C:\Tools' -Include 'Repair-*' -Unblock -OnExists Overwrite</code>
///   <para>Limits to files that start with Repair-, removes Windows Zone.Identifier (on Windows), and overwrites existing files.</para>
/// </example>
/// <example>
///   <summary>Preview planned actions without copying</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Get-Module -ListAvailable EFAdminManager | Install-ModuleScript -Path 'C:\Tools' -ListOnly</code>
///   <para>Shows Source, Destination, and chosen action for each file without writing anything.</para>
/// </example>
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

    /// <summary>When set, attempts to remove the Windows Zone.Identifier (unblock) on copied files.</summary>
    [Parameter]
    public SwitchParameter Unblock { get; set; }

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
        var scriptsRoot = FindScriptsFolder(
            moduleBase,
            ScriptsRelativePath,
            MyInvocation.BoundParameters.ContainsKey(nameof(ScriptsRelativePath)),
            this);
        if (scriptsRoot == null)
            throw new DirectoryNotFoundException($"Scripts folder '{ScriptsRelativePath}' not found under '{moduleBase}'.");

        var dest = PathHelper.Normalize(Path);
        PathHelper.EnsureDirectory(dest);

        var includes = (Include == null || Include.Length == 0) ? new[] { "*.ps1" } : Include;
        var excludes = Exclude ?? Array.Empty<string>();

        WriteVerbose($"Scanning scripts root: {scriptsRoot}");
        WriteVerbose($"Include: {string.Join(", ", includes)}; Exclude: {(excludes.Length>0?string.Join(", ", excludes):"<none>")}");

        // Fast path: default include (*.ps1) with no excludes -> use filesystem filter
        var files = new System.Collections.Generic.List<string>();
        bool defaultAllOnly = (Include == null || Include.Length == 0) && (Exclude == null || Exclude.Length == 0);
        if (defaultAllOnly)
        {
            files.AddRange(Directory.GetFiles(scriptsRoot, "*.ps1", SearchOption.AllDirectories));
        }
        else
        {
            files = Directory.GetFiles(scriptsRoot, "*.ps1", SearchOption.AllDirectories)
                .Where(f => MatchesAny(f, scriptsRoot, includes) && !MatchesAny(f, scriptsRoot, excludes))
                .ToList();
        }

        if (files.Count == 0)
        {
            var total = Directory.GetFiles(scriptsRoot, "*", SearchOption.AllDirectories).Length;
            WriteVerbose($"No matching scripts found in '{scriptsRoot}' (total files under root: {total}).");
            if (total > 0)
            {
                foreach (var sample in Directory.GetFiles(scriptsRoot, "*", SearchOption.AllDirectories).Take(10))
                {
                    WriteVerbose($"  • Found file: {System.IO.Path.GetFileName(sample)}");
                }
            }
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
                if (exists && Force)
                {
                    try { File.SetAttributes(target, FileAttributes.Normal); } catch { /* ignore */ }
                }
                File.Copy(file, target, overwrite: true);
                if (Force)
                {
                    try { File.SetAttributes(target, FileAttributes.Normal); } catch { /* ignore */ }
                }
                if (Unblock && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        InvokeCommand.InvokeScript("param($p) Unblock-File -LiteralPath $p -ErrorAction SilentlyContinue", new object[] { target });
                    }
                    catch { /* ignore */ }
                }
                WriteVerbose($"Copied: {rel}");
            }
        }
    }

    private static string? FindScriptsFolder(string moduleBase, string relative, bool explicitRelativePath, PSCmdlet cmdlet)
    {
        var internals = System.IO.Path.Combine(moduleBase, "Internals");
        var manifest = Directory.GetFiles(moduleBase, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrEmpty(manifest) && !explicitRelativePath)
        {
            try
            {
                var deliveryValue = cmdlet.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.Delivery").Invoke(manifest).FirstOrDefault();
                var delivery = deliveryValue == null ? null : PSObject.AsPSObject(deliveryValue);
                var scriptsPath = GetDeliveryString(delivery, "ScriptsPath");
                if (!string.IsNullOrWhiteSpace(scriptsPath))
                {
                    var scriptsCandidate = System.IO.Path.Combine(moduleBase, scriptsPath!);
                    if (Directory.Exists(scriptsCandidate)) return scriptsCandidate;
                }

                var internalsPath = GetDeliveryString(delivery, "InternalsPath");
                if (!string.IsNullOrWhiteSpace(internalsPath))
                {
                    internals = System.IO.Path.Combine(moduleBase, internalsPath!);
                    var scriptsCandidate = System.IO.Path.Combine(internals, "Scripts");
                    if (Directory.Exists(scriptsCandidate)) return scriptsCandidate;
                }
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

    private static string? GetDeliveryString(PSObject? delivery, string name)
    {
        if (delivery == null) return null;
        if (delivery.BaseObject is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key != null && string.Equals(entry.Key.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    var dictionaryValue = entry.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(dictionaryValue)) return dictionaryValue;
                }
            }
        }

        try
        {
            var direct = delivery.Properties[name]?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
        }
        catch { /* ignore */ }

        try
        {
            var prop = delivery.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var value = prop?.Value?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch { return null; }
    }

    private static bool MatchesAny(string fullPath, string root, string[] patterns)
    {
        if (patterns == null || patterns.Length == 0) return true;
        var rel = fullPath.Substring(root.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(fullPath);
        foreach (var p in patterns)
        {
            var wc = new WildcardPattern(p, WildcardOptions.IgnoreCase);
            // Match either the relative path or just the file name to be friendly
            if (wc.IsMatch(rel) || wc.IsMatch(name)) return true;
        }
        return false;
    }
}
