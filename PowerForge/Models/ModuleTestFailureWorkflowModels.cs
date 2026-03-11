using System;

namespace PowerForge;

internal sealed class ModuleTestFailureWorkflowRequest
{
    internal bool UseTestResultsInput { get; set; }
    internal object? TestResults { get; set; }
    internal string? ExplicitPath { get; set; }
    internal string? ProjectPath { get; set; }
    internal string? ModuleBasePath { get; set; }
    internal string CurrentDirectory { get; set; } = Environment.CurrentDirectory;
}

internal sealed class ModuleTestFailureWorkflowResult
{
    internal ModuleTestFailureAnalysis? Analysis { get; set; }
    internal string[] WarningMessages { get; set; } = Array.Empty<string>();
}

internal sealed class ModuleTestFailurePathResolution
{
    internal string ProjectPath { get; set; } = string.Empty;
    internal string? ResultsPath { get; set; }
    internal string[] SearchedPaths { get; set; } = Array.Empty<string>();
    internal bool ExplicitPathProvided { get; set; }
}

internal sealed class ModuleTestFailureDisplayLine
{
    internal string Text { get; set; } = string.Empty;
    internal ConsoleColor? Color { get; set; }
}
