using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PSPublishModule;

/// <summary>
/// Gets module manifest information from a project directory.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ModuleInformation")]
public sealed class GetModuleInformationCommand : PSCmdlet
{
    /// <summary>The path to the directory containing the module manifest file.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Executes the manifest discovery and returns a module-information object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Path '{root}' does not exist or is not a directory");

        var manifests = FindPsd1Candidates(root).ToArray();
        if (manifests.Length == 0)
            throw new FileNotFoundException($"Path '{root}' doesn't contain PSD1 files");

        if (manifests.Length != 1)
        {
            var foundFiles = string.Join(", ", manifests);
            throw new InvalidOperationException($"More than one PSD1 file detected in '{root}': {foundFiles}");
        }

        var manifestPath = manifests[0];
        WriteVerbose($"Loading module manifest from: {manifestPath}");

        Hashtable? psdInformation = null;
        using (var ps = PowerShell.Create(RunspaceMode.CurrentRunspace))
        {
            ps.AddCommand("Import-PowerShellDataFile")
                .AddParameter("LiteralPath", manifestPath);

            var results = ps.Invoke();
            if (ps.HadErrors)
            {
                var msg = string.Join("; ", ps.Streams.Error.Select(e => e.Exception?.Message ?? e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrWhiteSpace(msg)) msg = "Import-PowerShellDataFile failed";
                throw new InvalidOperationException(msg);
            }

            psdInformation = results.Count > 0 ? results[0].BaseObject as Hashtable : null;
        }

        if (psdInformation is null)
            throw new InvalidOperationException($"Failed to load module manifest from '{manifestPath}'");

        var moduleName = System.IO.Path.GetFileNameWithoutExtension(manifestPath) ?? string.Empty;

        object? moduleVersion = psdInformation.ContainsKey("ModuleVersion") ? psdInformation["ModuleVersion"] : null;
        object? requiredModules = psdInformation.ContainsKey("RequiredModules") ? psdInformation["RequiredModules"] : null;
        object? rootModule = psdInformation.ContainsKey("RootModule") ? psdInformation["RootModule"] : null;
        object? powerShellVersion = psdInformation.ContainsKey("PowerShellVersion") ? psdInformation["PowerShellVersion"] : null;

        var output = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["ModuleName"] = moduleName,
            ["ManifestPath"] = manifestPath,
            ["ModuleVersion"] = moduleVersion,
            ["RequiredModules"] = requiredModules,
            ["RootModule"] = rootModule,
            ["PowerShellVersion"] = powerShellVersion,
            ["ManifestData"] = psdInformation,
            ["ProjectPath"] = root
        };

        WriteObject(output);
    }

    private static IEnumerable<string> FindPsd1Candidates(string root)
    {
        IEnumerable<string> top = Array.Empty<string>();
        try
        {
            top = Directory.EnumerateFiles(root, "*.psd1", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            // ignore and let the caller handle missing files
        }

        foreach (var f in top) yield return f;

        IEnumerable<string> subdirs = Array.Empty<string>();
        try
        {
            subdirs = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in subdirs)
        {
            IEnumerable<string> inner = Array.Empty<string>();
            try
            {
                inner = Directory.EnumerateFiles(dir, "*.psd1", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var f in inner) yield return f;
        }
    }
}
