using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PowerForge.ModuleLoader.V1;

/// <summary>
/// Windows PowerShell keeps every module in one AppDomain, and .NET Framework binds an assembly without a
/// strong name by its simple name only: once any module loads ChartForgeX 3.0.1, every later request for
/// ChartForgeX gets that copy, whatever version the next module ships. This check runs before a module loads
/// its payload. It reuses an already loaded copy when that is compatible and says so, and stops the import
/// with one actionable message when it is not, instead of failing later with a missing type or member.
/// </summary>
/// <remarks>
/// Compatible means the same major.minor release, at least as new as the packaged copy. Minor releases of our
/// libraries do change public API (OfficeIMO 3.6.0 renamed a public enum 3.5.1 used), and an older patch can lack
/// members the module calls. Strong-named assemblies are skipped: .NET Framework loads versions of those side by side.
/// </remarks>
public sealed class DependencyConflictCheck
{
    private readonly List<string> _warnings = new();
    private readonly List<Reuse> _reused = new();
    private string _module = string.Empty;

    private DependencyConflictCheck() { }

    /// <summary>Messages the calling script should surface with Write-Warning (one per module whose copies are reused).</summary>
    public string[] Warnings => _warnings.ToArray();

    /// <summary>One message describing why the import cannot continue, or null when it can.</summary>
    public string? Error { get; private set; }

    /// <summary>Checks the assemblies in the module's payload folders against assemblies already loaded in this process.</summary>
    public static DependencyConflictCheck Evaluate(string moduleName, string? moduleRoot, string[]? payloadDirectories, string[]? ignoredFileNames)
    {
        var check = new DependencyConflictCheck { _module = ModuleDescription.Describe(moduleName, moduleRoot) };
        var ignored = new HashSet<string>((ignoredFileNames ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(SafeLocation(assembly)))
            .ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocking = new List<Reuse>();

        foreach (var directory in (payloadDirectories ?? Array.Empty<string>()).Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (ignored.Contains(Path.GetFileName(path))) continue;
                AssemblyName packaged;
                try { packaged = AssemblyName.GetAssemblyName(path); }
                catch { continue; } // native or unreadable files are not managed dependencies
                if (packaged.GetPublicKeyToken() is { Length: > 0 } || !seen.Add(packaged.Name!)) continue;

                var fullPath = Path.GetFullPath(path);
                var copies = loaded.Where(assembly => string.Equals(assembly.GetName().Name, packaged.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (copies.Any(copy => string.Equals(Path.GetFullPath(SafeLocation(copy)), fullPath, StringComparison.OrdinalIgnoreCase))) continue;
                var existing = copies.FirstOrDefault();
                if (existing is null) continue;

                var loadedVersion = existing.GetName().Version ?? new Version(0, 0);
                var packagedVersion = packaged.Version ?? new Version(0, 0);
                if (loadedVersion == packagedVersion) continue;

                var (ownerName, owner) = ModuleDescription.DescribeOwner(SafeLocation(existing));
                var entry = new Reuse(packaged.Name!, loadedVersion, packagedVersion, ownerName, owner);
                if (loadedVersion.Major == packagedVersion.Major && loadedVersion.Minor == packagedVersion.Minor && loadedVersion > packagedVersion)
                    check._reused.Add(entry);
                else
                    blocking.Add(entry);
            }
        }

        if (blocking.Count > 0)
        {
            check.Error = Refusal(check._module, moduleName, blocking);
            return check;
        }

        foreach (var group in check._reused.GroupBy(entry => entry.Owner, StringComparer.Ordinal))
        {
            var libraries = group.ToArray();
            var subject = libraries.Length == 1 ? "a library" : libraries.Length + " libraries";
            check._warnings.Add(
                $"{check._module} uses {subject} that {group.Key} already loaded: {string.Join(", ", libraries.Select(entry => entry.Describe()))}. " +
                "Windows PowerShell holds one version of an assembly per session; PowerShell 7 keeps each module's copy separate.");
        }

        return check;
    }

    /// <summary>
    /// Explains a load failure that happened after this check let the module reuse copies another module loaded,
    /// or returns null when the failure is unrelated. The bootstrapper uses it to replace a raw TypeLoadException
    /// with the same actionable message a refusal gives.
    /// </summary>
    public string? ExplainLoadFailure(Exception? failure)
    {
        if (_reused.Count == 0 || failure is null) return null;
        string? detail = null;
        for (var current = failure; current is not null && detail is null; current = current.InnerException)
        {
            detail = current switch
            {
                ReflectionTypeLoadException typeLoad => typeLoad.LoaderExceptions?.FirstOrDefault(inner => inner is not null)?.Message ?? typeLoad.Message,
                TypeLoadException or MissingMemberException or FileLoadException or BadImageFormatException => current.Message,
                _ => null
            };
        }

        if (detail is null) return null;
        var owners = string.Join(" and ", _reused.Select(entry => entry.Owner).Distinct(StringComparer.Ordinal));
        return $"{_module} cannot load in this Windows PowerShell session: it does not work with the library versions {owners} already loaded " +
               $"({string.Join(", ", _reused.Select(entry => entry.Describe()))}). {detail} " +
               "Start a separate Windows PowerShell session for each module, or use PowerShell 7, which loads each module's dependencies separately.";
    }

    private static string Refusal(string module, string moduleName, List<Reuse> blocking)
    {
        var sameModule = blocking.FirstOrDefault(entry => string.Equals(entry.OwnerName, moduleName, StringComparison.OrdinalIgnoreCase));
        if (sameModule is not null)
        {
            var loadedModule = sameModule.Owner.StartsWith("module ", StringComparison.Ordinal) ? sameModule.Owner.Substring("module ".Length) : sameModule.Owner;
            return $"{module} cannot load: {loadedModule} is already loaded in this Windows PowerShell session, and its assemblies stay loaded after Remove-Module. " +
                   $"Start a new Windows PowerShell session to use {module}.";
        }

        var details = string.Join("; ", blocking.Select(entry => $"{entry.Name} {Short(entry.Packaged)} is needed, but {entry.Owner} already loaded {entry.Name} {Short(entry.Loaded)}"));
        // Only an older patch release of the same major.minor can be fixed by import order: imported first, this module
        // loads the newer patch and the other module can reuse it. Any other difference refuses in both orders.
        var advice = blocking.All(entry => entry.Loaded.Major == entry.Packaged.Major && entry.Loaded.Minor == entry.Packaged.Minor && entry.Loaded < entry.Packaged)
            ? $"Start a new Windows PowerShell session and import {moduleName} first (the other module can then reuse the newer patch release), or use PowerShell 7, which loads each module's dependencies separately."
            : "These versions are not compatible, so the modules cannot be used together in one Windows PowerShell session. Start a separate session for each, or use PowerShell 7, which loads each module's dependencies separately.";
        return $"{module} cannot load in this Windows PowerShell session: {details}. {advice}";
    }

    private static string Short(Version version)
        => version.Revision > 0 ? version.ToString() : version.Build >= 0 ? version.ToString(3) : version.ToString();

    private static string SafeLocation(Assembly assembly)
    {
        try { return assembly.Location ?? string.Empty; }
        catch { return string.Empty; }
    }

    private sealed class Reuse
    {
        public Reuse(string name, Version loaded, Version packaged, string? ownerName, string owner)
        {
            Name = name;
            Loaded = loaded;
            Packaged = packaged;
            OwnerName = ownerName;
            Owner = owner;
        }

        public string Name { get; }
        public Version Loaded { get; }
        public Version Packaged { get; }
        public string? OwnerName { get; }
        public string Owner { get; }

        public string Describe() => $"{Name} {Short(Loaded)} (ships {Short(Packaged)})";
    }
}
