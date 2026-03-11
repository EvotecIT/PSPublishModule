using System;

namespace PowerForge;

internal sealed class ProjectCleanupDisplayLine
{
    internal string Text { get; set; } = string.Empty;
    internal ConsoleColor? Color { get; set; }
    internal bool IsWarning { get; set; }
}
