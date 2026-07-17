using System;

namespace PowerForge;

internal sealed class ModuleStateModulePath
{
    internal ModuleStateModulePath(
        string path,
        string? powerShellEdition = null,
        string? scope = null,
        string? profileName = null,
        bool isRequired = false,
        bool wasAvailable = false)
    {
        Path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Module path is required.", nameof(path))
            : path.Trim();
        PowerShellEdition = NormalizeOptional(powerShellEdition);
        Scope = NormalizeOptional(scope);
        ProfileName = NormalizeOptional(profileName);
        IsRequired = isRequired;
        WasAvailable = wasAvailable;
    }

    internal string Path { get; }

    internal string? PowerShellEdition { get; }

    internal string? Scope { get; }

    internal string? ProfileName { get; }

    internal bool IsRequired { get; }

    internal bool WasAvailable { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
