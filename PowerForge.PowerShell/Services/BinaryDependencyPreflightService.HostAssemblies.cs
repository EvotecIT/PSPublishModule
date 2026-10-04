using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PowerForge;

public sealed partial class BinaryDependencyPreflightService
{
    private static readonly string[] WellKnownFrameworkAssemblyNames =
    {
        "mscorlib",
        "netstandard",
        "System",
        "System.Configuration",
        "System.Core",
        "System.Data",
        "System.Drawing",
        "System.Management",
        "System.Management.Automation",
        "System.Runtime",
        "System.Security",
        "System.ValueTuple",
        "System.Web",
        "System.Windows.Forms",
        "System.Xml",
        "System.Xml.Linq",
        "WindowsBase",
        "Microsoft.CSharp",
        "Microsoft.VisualBasic",
        "PresentationCore",
        "PresentationFramework",
        "UIAutomationClient",
        "UIAutomationProvider",
        "UIAutomationTypes"
    };

    // Assemblies PowerShell 7 commonly provides at runtime but a Desktop validation host may not expose.
    private static readonly string[] WellKnownCoreRuntimeAssemblyNames =
    {
        "System.Buffers",
        "System.Collections.Immutable",
        "System.Memory",
        "System.Net.ServicePoint",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Text.Encoding.CodePages",
        "System.Threading.Tasks.Extensions"
    };

    private IReadOnlyCollection<string> ResolveHostProvidedAssemblyNames(string edition)
        => string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(typeof(object).Assembly.GetName().Name, "mscorlib", StringComparison.OrdinalIgnoreCase)
            ? ResolveCoreHostAssemblyNames()
            : GetHostProvidedAssemblyNames(edition);

    // A Desktop CLR cannot describe the runtime assemblies a Core payload relies on.
    // Ask the same host resolver used for imports, then reuse its inventory for this build.
    internal IReadOnlyCollection<string> ResolveCoreHostAssemblyNames()
    {
        if (_coreHostAssemblyNames is not null)
            return _coreHostAssemblyNames;

        var result = _runner.Run(PowerShellRunRequest.ForCompatibleCommand(
            "[Console]::WriteLine($PSHOME)", TimeSpan.FromSeconds(30), requiredRuntimeMajor: 1));
        var directory = result.StdOut.Trim();
        if (result.ExitCode != 0 || !Path.IsPathRooted(directory) || !Directory.Exists(directory))
            throw new InvalidOperationException("Unable to discover the PowerShell Core runtime for binary dependency validation. Ensure a compatible pwsh host is available.");

        var names = new HashSet<string>(WellKnownFrameworkAssemblyNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in WellKnownCoreRuntimeAssemblyNames)
            names.Add(name);
        AddAssembliesFromDirectory(names, directory);
        _coreHostAssemblyNames = names;
        return names;
    }

    private static IReadOnlyCollection<string> GetHostProvidedAssemblyNames(string edition)
    {
        var set = new HashSet<string>(WellKnownFrameworkAssemblyNames, StringComparer.OrdinalIgnoreCase);

        if (string.Equals(edition, "Desktop", StringComparison.OrdinalIgnoreCase))
        {
            if (Path.DirectorySeparatorChar != '\\')
                return set;

            foreach (var dir in GetDesktopReferenceAssemblyDirectories())
                AddAssembliesFromDirectory(set, dir);

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                AddAssembliesFromDirectory(set, Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0"));
                AddAssembliesFromDirectory(set, Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "Modules"), recursive: true);
                AddSpecificAssemblyIfPresent(
                    set,
                    Path.Combine(windir, "Microsoft.NET", "assembly"),
                    "System.Management.Automation.dll");
                // Windows PowerShell supplies CIM through the GAC, outside its module directory.
                AddSpecificAssemblyIfPresent(
                    set,
                    Path.Combine(windir, "Microsoft.NET", "assembly"),
                    "Microsoft.Management.Infrastructure.dll");
                AddSpecificAssemblyIfPresent(
                    set,
                    Path.Combine(windir, "Microsoft.NET", "assembly"),
                    "System.Runtime.WindowsRuntime.dll");
            }

            return set;
        }

        // Core validation may run from a Desktop host, so seed assemblies PowerShell 7 provides at runtime.
        foreach (var name in WellKnownCoreRuntimeAssemblyNames)
            set.Add(name);

        AddAssembliesFromDirectory(set, Path.GetDirectoryName(typeof(object).Assembly.Location));
        AddAssembliesFromDirectory(set, Path.GetDirectoryName(typeof(PSObject).Assembly.Location));

        return set;
    }

    private static IEnumerable<string> GetDesktopReferenceAssemblyDirectories()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        }
        .Where(static p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework");
            foreach (var version in new[] { "v4.7.2", "v4.8", "v4.8.1" })
            {
                var path = Path.Combine(baseDir, version);
                if (Directory.Exists(path)) yield return path;

                var facades = Path.Combine(path, "Facades");
                if (Directory.Exists(facades)) yield return facades;
            }
        }
    }

    private static void AddAssembliesFromDirectory(ISet<string> set, string? directory, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll", searchOption))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }
        }
        catch
        {
            // best effort only
        }
    }

    private static void AddSpecificAssemblyIfPresent(ISet<string> set, string? rootDirectory, string assemblyFileName)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return;
        if (string.IsNullOrWhiteSpace(assemblyFileName)) return;

        try
        {
            var path = Directory.EnumerateFiles(rootDirectory, assemblyFileName, SearchOption.AllDirectories).FirstOrDefault();
            if (path is null) return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (name is { Length: > 0 })
                set.Add(name);
        }
        catch
        {
            // best effort only
        }
    }

}
