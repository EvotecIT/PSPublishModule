using System;

namespace PowerForge;

internal sealed class ModuleStateModulePath
{
    internal ModuleStateModulePath(string path, string? powerShellEdition = null, string? scope = null)
    {
        Path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Module path is required.", nameof(path))
            : path.Trim();
        PowerShellEdition = NormalizeOptional(powerShellEdition);
        Scope = NormalizeOptional(scope);
    }

    internal string Path { get; }

    internal string? PowerShellEdition { get; }

    internal string? Scope { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
