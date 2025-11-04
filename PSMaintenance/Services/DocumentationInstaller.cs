using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// Installs (copies) module documentation to a destination according to layout
/// (Direct/Module/ModuleAndVersion) and overwrite/merge options.
/// Handles Internals content and selected root files (README/CHANGELOG/LICENSE).
/// </summary>
internal sealed class DocumentationInstaller
{
    private readonly PSCmdlet _cmdlet;
    private readonly DocumentationFinder _finder;

    public DocumentationInstaller(PSCmdlet cmdlet)
    {
        _cmdlet  = cmdlet;
        _finder  = new DocumentationFinder(cmdlet);
    }

    /// <summary>
    /// Computes the destination path for the given module and layout.
    /// </summary>
    public string PlanDestination(string moduleName, string moduleVersion, string path, DocumentationLayout layout)
    {
        return layout switch
        {
            DocumentationLayout.Direct => path,
            DocumentationLayout.Module => Path.Combine(path, moduleName),
            DocumentationLayout.ModuleAndVersion => Path.Combine(Path.Combine(path, moduleName), moduleVersion),
            _ => path
        };
    }

    /// <summary>
    /// Copies Internals and selected root files to the destination. Returns the destination path.
    /// </summary>
    public string Install(string moduleBase, string moduleName, string moduleVersion, string dest, OnExistsOption onExists, bool force, bool open, bool noIntro)
    {
        var root = moduleBase;
        // Resolve Internals path via manifest when available
        string? internals = null;
        var options = new DeliveryOptions();
        var manifestPath = Directory.GetFiles(moduleBase, "*.psd1", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrEmpty(manifestPath))
        {
            try
            {
                var del = _cmdlet.InvokeCommand.NewScriptBlock("(Test-ModuleManifest -Path $args[0]).PrivateData.PSData.PSPublishModuleDelivery").Invoke(manifestPath).FirstOrDefault() as PSObject;
                var internalsRel = del?.Properties["InternalsPath"]?.Value as string ?? "Internals";
                var cand = Path.Combine(root, internalsRel);
                if (Directory.Exists(cand)) internals = cand;
            }
            catch { }
        }
        if (internals == null)
        {
            var cand = Path.Combine(root, "Internals");
            if (Directory.Exists(cand)) internals = cand;
        }
        if (internals == null)
            throw new DirectoryNotFoundException($"Internals path '{options.InternalsPath}' not found under '{root}'.");

        if (Directory.Exists(dest))
        {
            switch (onExists)
            {
                case OnExistsOption.Skip:
                    return dest;
                case OnExistsOption.Stop:
                    throw new IOException($"Destination '{dest}' already exists.");
                case OnExistsOption.Overwrite:
                    Directory.Delete(dest, true);
                    break;
                case OnExistsOption.Merge:
                    // keep existing, merge below
                    break;
            }
        }

        PathHelper.EnsureDirectory(dest);
        CopyTree(internals, dest, overwrite: force);

        // Copy selected root files
        TryCopyIfMatch(root, "README*", dest, force);
        TryCopyIfMatch(root, "CHANGELOG*", dest, force);
        // Normalize license name if present
        var lic = new DirectoryInfo(root).GetFiles("LICENSE*").FirstOrDefault();
        if (lic != null)
        {
            var licTarget = Path.Combine(dest, "license.txt");
            if (!(File.Exists(licTarget) && !force))
            {
                lic.CopyTo(licTarget, overwrite: true);
            }
        }

        if (open)
        {
            try
            {
                var readme = new DirectoryInfo(dest).GetFiles("README*").FirstOrDefault();
                if (readme != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(readme.FullName) { UseShellExecute = true });
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
            }
            catch { }
        }

        return dest;
    }

    private static void CopyTree(string source, string dest, bool overwrite)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetDir = Path.Combine(dest, rel);
            PathHelper.EnsureDirectory(targetDir);
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && !overwrite)
            {
                // Merge semantics: keep existing file unless Force/overwrite specified
                continue;
            }
            File.Copy(file, target, true);
        }
    }

    private static void TryCopyIfMatch(string root, string pattern, string dest, bool force)
    {
        var di = new DirectoryInfo(root);
        foreach (var f in di.GetFiles(pattern))
        {
            var target = Path.Combine(dest, f.Name);
            if (File.Exists(target) && !force) continue; // keep existing unless -Force
            f.CopyTo(target, overwrite: true);
        }
    }
}
