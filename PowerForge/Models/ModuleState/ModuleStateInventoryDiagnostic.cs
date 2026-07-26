using System;

namespace PowerForge;

internal sealed class ModuleStateInventoryDiagnostic
{
    internal ModuleStateInventoryDiagnostic(
        ModuleStateConflictSeverity severity,
        string code,
        string message,
        string path,
        string? powerShellEdition = null,
        string? scope = null,
        string? profileName = null)
    {
        Severity = severity;
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Inventory diagnostic code is required.", nameof(code))
            : code.Trim();
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("Inventory diagnostic message is required.", nameof(message))
            : message.Trim();
        Path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Inventory diagnostic path is required.", nameof(path))
            : path.Trim();
        PowerShellEdition = NormalizeOptional(powerShellEdition);
        Scope = NormalizeOptional(scope);
        ProfileName = NormalizeOptional(profileName);
    }

    internal ModuleStateConflictSeverity Severity { get; }

    internal string Code { get; }

    internal string Message { get; }

    internal string Path { get; }

    internal string? PowerShellEdition { get; }

    internal string? Scope { get; }

    internal string? ProfileName { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
