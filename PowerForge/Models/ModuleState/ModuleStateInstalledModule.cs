using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleStateInstalledModule
{
    internal ModuleStateInstalledModule(
        string name,
        string version,
        string? powerShellEdition = null,
        string? scope = null,
        string? path = null,
        string? sourceRepository = null,
        bool isLoaded = false,
        bool isEffectiveImportCandidate = false,
        IEnumerable<string>? exportedCommands = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Module name is required.", nameof(name))
            : name.Trim();
        Version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Module version is required.", nameof(version))
            : version.Trim();
        PowerShellEdition = NormalizeOptional(powerShellEdition);
        Scope = NormalizeOptional(scope);
        Path = NormalizeOptional(path);
        SourceRepository = NormalizeOptional(sourceRepository);
        IsLoaded = isLoaded;
        IsEffectiveImportCandidate = isEffectiveImportCandidate;
        ExportedCommands = (exportedCommands ?? Array.Empty<string>())
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Select(static command => command.Trim())
            .Where(static command => !string.Equals(command, "*", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static command => command, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal string Name { get; }

    internal string Version { get; }

    internal string? PowerShellEdition { get; }

    internal string? Scope { get; }

    internal string? Path { get; }

    internal string? SourceRepository { get; }

    internal bool IsLoaded { get; }

    internal bool IsEffectiveImportCandidate { get; }

    internal string[] ExportedCommands { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
