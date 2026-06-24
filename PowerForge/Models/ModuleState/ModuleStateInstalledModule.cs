using System;

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
        bool isEffectiveImportCandidate = false)
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
    }

    internal string Name { get; }

    internal string Version { get; }

    internal string? PowerShellEdition { get; }

    internal string? Scope { get; }

    internal string? Path { get; }

    internal string? SourceRepository { get; }

    internal bool IsLoaded { get; }

    internal bool IsEffectiveImportCandidate { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
