using System.Collections.Generic;

namespace PowerGuardian;

internal enum ModuleDependencyKind { Required, External }

internal sealed class ModuleDependency
{
    public ModuleDependencyKind Kind { get; set; } = ModuleDependencyKind.Required;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Guid { get; set; }
    public List<ModuleDependency> Children { get; } = new List<ModuleDependency>();
}

internal sealed class ModuleInfoModel
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? PowerShellVersion { get; set; }
    public string? ProjectUri { get; set; }
    public string? IconUri { get; set; }
    public bool? RequireLicenseAcceptance { get; set; }
    public List<ModuleDependency> Dependencies { get; } = new List<ModuleDependency>();

    // Exporter options
    public bool SkipCommands { get; set; }
    public bool SkipDependencies { get; set; }
    public int MaxCommands { get; set; } = 100;
    public int HelpTimeoutSeconds { get; set; } = 3;
}
