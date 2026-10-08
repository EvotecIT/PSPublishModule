using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class BinaryDependencyPreflightService
{
    /// <summary>
    /// Analyzes a payload while allowing explicitly declared optional references
    /// from one exact DLL to another. Manifest-declared assemblies remain required.
    /// </summary>
    public BinaryDependencyPreflightResult Analyze(
        string moduleRoot,
        string powerShellEdition,
        string? manifestPath,
        IReadOnlyDictionary<string, string[]>? optionalDependencies)
    {
        var optional = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in optionalDependencies ?? new Dictionary<string, string[]>())
        {
            ValidateOptionalDependencyFileName(entry.Key);
            if (entry.Value is null || entry.Value.Length == 0)
                throw new ArgumentException("Each optional binary dependency declaration must contain at least one dependency DLL.", nameof(optionalDependencies));
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in entry.Value)
            {
                ValidateOptionalDependencyFileName(dependency);
                references.Add(dependency);
            }
            optional.Add(entry.Key, references);
        }

        var result = Analyze(moduleRoot, powerShellEdition, manifestPath);
        if (!result.HasIssues || optional.Count == 0)
            return result;

        var required = result.Issues.Where(issue =>
        {
            var referringFile = Path.GetFileName(issue.AssemblyFileName);
            if (!optional.TryGetValue(referringFile, out var dependencies) ||
                !dependencies.Contains(issue.MissingDependencyFileName))
                return true;

            _logger.Verbose($"Optional binary reference is absent ({powerShellEdition}): {issue.AssemblyFileName} -> {issue.MissingDependencyFileName}.");
            return false;
        }).ToArray();

        return new BinaryDependencyPreflightResult(
            result.PowerShellEdition, result.ModuleRoot, result.AssemblyRootPath,
            result.AssemblyRootRelativePath, required,
            required.Length == 0 ? "ok (declared optional references excluded)" : $"{required.Length} missing required dependencies");
    }

    private static void ValidateOptionalDependencyFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name.Length <= 4 || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '[', ']' }) >= 0 || name != name.Trim())
            throw new ArgumentException("Optional binary dependencies require exact DLL filenames without paths or wildcards.", nameof(name));
    }
}
