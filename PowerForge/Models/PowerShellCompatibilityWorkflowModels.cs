using System;

namespace PowerForge;

internal sealed class PowerShellCompatibilityWorkflowRequest
{
    internal string InputPath { get; set; } = string.Empty;
    internal string? ExportPath { get; set; }
    internal bool Recurse { get; set; }
    internal string[] ExcludeDirectories { get; set; } = Array.Empty<string>();
    internal bool Internal { get; set; }
}

internal sealed class PowerShellCompatibilityWorkflowResult
{
    internal PowerShellCompatibilityReport Report { get; set; } = new(
        new PowerShellCompatibilitySummary(CheckStatus.Pass, DateTime.Now, 0, 0, 0, 0, 0, 0, string.Empty, Array.Empty<string>()),
        Array.Empty<PowerShellCompatibilityFileResult>(),
        null);
    internal bool HasFiles => Report.Files.Length > 0;
}

internal sealed class PowerShellCompatibilityDisplayLine
{
    internal string Text { get; set; } = string.Empty;
    internal ConsoleColor? Color { get; set; }
}
