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
/// with one actionable message when it is not, instead of failing later with a missing member.
/// </summary>
/// <remarks>
/// Compatible means the same major version and either a version at least as new as the packaged one or an
/// older patch release of the same major.minor (patch releases keep the public API). Strong-named assemblies
/// are skipped: .NET Framework loads different versions of those side by side.
/// </remarks>
public sealed class DependencyConflictCheck
{
    private readonly List<string> _warnings = new();
    private readonly List<string> _blocking = new();

    private DependencyConflictCheck() { }

    /// <summary>Messages the calling script should surface with Write-Warning.</summary>
    public string[] Warnings => _warnings.ToArray();

    /// <summary>One message describing every dependency that cannot be shared, or null when the import can continue.</summary>
    public string? Error { get; private set; }

    /// <summary>Checks the assemblies in the module's payload folders against assemblies already loaded in this process.</summary>
    public static DependencyConflictCheck Evaluate(string moduleName, string? moduleRoot, string[]? payloadDirectories, string[]? ignoredFileNames)
    {
        var check = new DependencyConflictCheck();
        var module = ModuleDescription.Describe(moduleName, moduleRoot);
        var ignored = new HashSet<string>((ignoredFileNames ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(SafeLocation(assembly)))
            .ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newMajorAdvice = false;
        var sameModuleLoaded = false;

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
                var name = packaged.Name;
                if (loadedVersion.Major == packagedVersion.Major &&
                    (loadedVersion >= packagedVersion || loadedVersion.Minor == packagedVersion.Minor))
                {
                    check._warnings.Add(
                        $"{module} uses {name} {Short(loadedVersion)} that {owner} already loaded, instead of its own {Short(packagedVersion)}. " +
                        "Windows PowerShell holds one version of an assembly per session; PowerShell 7 keeps each module's copy separate.");
                    continue;
                }

                newMajorAdvice |= loadedVersion.Major != packagedVersion.Major;
                sameModuleLoaded |= string.Equals(ownerName, moduleName, StringComparison.OrdinalIgnoreCase);
                check._blocking.Add($"{name} {Short(packagedVersion)} is needed, but {owner} already loaded {name} {Short(loadedVersion)}");
            }
        }

        if (check._blocking.Count > 0)
        {
            var advice = sameModuleLoaded
                ? $"Another version of {moduleName} is already loaded in this session. Start a new Windows PowerShell session to use this version, or use PowerShell 7, which can load both."
                : newMajorAdvice
                ? "These versions are not compatible, so the modules cannot be used together in one Windows PowerShell session. Start a new session for each, or use PowerShell 7, which loads each module's dependencies separately."
                : $"Start a new Windows PowerShell session and import {moduleName} first (the other module can then reuse the newer version), or use PowerShell 7, which loads each module's dependencies separately.";
            check.Error = $"{module} cannot load in this Windows PowerShell session: {string.Join("; ", check._blocking)}. {advice}";
        }

        return check;
    }

    private static string Short(Version version)
        => version.Revision > 0 ? version.ToString() : version.Build >= 0 ? version.ToString(3) : version.ToString();

    private static string SafeLocation(Assembly assembly)
    {
        try { return assembly.Location ?? string.Empty; }
        catch { return string.Empty; }
    }
}
