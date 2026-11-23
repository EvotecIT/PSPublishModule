using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateInventoryRequest
{
    internal ModuleStateInventoryRequest(
        IEnumerable<ModuleStateModulePath>? modulePaths,
        IEnumerable<string>? names = null,
        string? version = null,
        string? scope = null)
    {
        ModulePaths = (modulePaths ?? Array.Empty<ModuleStateModulePath>()).Where(static path => path is not null).ToArray();
        Names = NormalizeNames(names);
        Version = NormalizeOptional(version);
        Scope = NormalizeOptional(scope);
    }

    internal ModuleStateModulePath[] ModulePaths { get; }

    internal string[] Names { get; }

    internal string? Version { get; }

    internal string? Scope { get; }

    private static string[] NormalizeNames(IEnumerable<string>? names)
        => (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
