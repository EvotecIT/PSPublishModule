using System;

namespace PowerForge;

internal sealed class ModuleStateMaintenanceReceiptModule
{
    internal ModuleStateMaintenanceReceiptModule(
        string name,
        string version,
        string? sourceRepository = null,
        string? scope = null)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Receipt module name is required.", nameof(name))
            : name.Trim();
        Version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Receipt module version is required.", nameof(version))
            : version.Trim();
        SourceRepository = NormalizeOptional(sourceRepository);
        Scope = NormalizeOptional(scope);
    }

    internal string Name { get; }

    internal string Version { get; }

    internal string? SourceRepository { get; }

    internal string? Scope { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
