using System;

namespace PowerForge;

internal sealed class ModuleStateMaintenanceReceiptModule
{
    internal ModuleStateMaintenanceReceiptModule(
        string name,
        string version,
        string? sourceRepository = null,
        string? scope = null,
        string? moduleRoot = null,
        string? powerShellEdition = null,
        string? profileName = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Receipt module name is required.", nameof(name))
            : name.Trim();
        Version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Receipt module version is required.", nameof(version))
            : version.Trim();
        SourceRepository = NormalizeOptional(sourceRepository);
        Scope = NormalizeOptional(scope);
        ModuleRoot = NormalizeOptional(moduleRoot);
        PowerShellEdition = NormalizeOptional(powerShellEdition);
        ProfileName = NormalizeOptional(profileName);
    }

    internal string Name { get; }

    internal string Version { get; }

    internal string? SourceRepository { get; }

    internal string? Scope { get; }

    internal string? ModuleRoot { get; }

    internal string? PowerShellEdition { get; }

    internal string? ProfileName { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
