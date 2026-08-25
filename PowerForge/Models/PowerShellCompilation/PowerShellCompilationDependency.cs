using System;

namespace PowerForge;

/// <summary>Runtime dependency or content kind discovered for a PowerShell compilation input.</summary>
public enum PowerShellCompilationDependencyKind
{
    /// <summary>PowerShell script, script module, or manifest source.</summary>
    PowerShellSource,
    /// <summary>PowerShell module manifest.</summary>
    ModuleManifest,
    /// <summary>External PowerShell module requirement.</summary>
    RequiredModule,
    /// <summary>Managed .NET assembly.</summary>
    ManagedAssembly,
    /// <summary>Native dynamic library.</summary>
    NativeLibrary,
    /// <summary>JavaScript content.</summary>
    JavaScript,
    /// <summary>CSS content.</summary>
    StyleSheet,
    /// <summary>PowerShell type data.</summary>
    TypeData,
    /// <summary>PowerShell format data.</summary>
    FormatData,
    /// <summary>Other runtime content.</summary>
    Content
}

/// <summary>How a dependency entered the deterministic compilation graph.</summary>
public enum PowerShellCompilationDependencyDiscovery
{
    /// <summary>Resolved script/module compilation graph.</summary>
    SourceGraph,
    /// <summary>The selected module manifest itself.</summary>
    ModuleManifest,
    /// <summary>Manifest RootModule reference.</summary>
    RootModule,
    /// <summary>Manifest RequiredModules entry.</summary>
    RequiredModules,
    /// <summary>Manifest RequiredAssemblies entry.</summary>
    RequiredAssemblies,
    /// <summary>Manifest NestedModules entry.</summary>
    NestedModules,
    /// <summary>Manifest ScriptsToProcess entry.</summary>
    ScriptsToProcess,
    /// <summary>Manifest TypesToProcess entry.</summary>
    TypesToProcess,
    /// <summary>Manifest FormatsToProcess entry.</summary>
    FormatsToProcess,
    /// <summary>Manifest FileList entry.</summary>
    FileList,
    /// <summary>Conventional Resources or Resource directory.</summary>
    ConventionalResourceDirectory,
    /// <summary>Conventional Lib or Libraries directory.</summary>
    ConventionalLibraryDirectory,
    /// <summary>Conventional runtimes directory.</summary>
    ConventionalRuntimeDirectory
}

/// <summary>What the selected artifact shape does with a discovered dependency.</summary>
public enum PowerShellCompilationDependencyDisposition
{
    /// <summary>Source behavior is lowered into generated CLR code.</summary>
    Compiled,
    /// <summary>PowerShell source remains in the generated Hybrid module.</summary>
    PreservedScript,
    /// <summary>Content is embedded inside the produced file.</summary>
    Embedded,
    /// <summary>Content is embedded and extracted into a contained temporary layout at runtime.</summary>
    EmbeddedAndExtracted,
    /// <summary>Content is copied beside the generated module while retaining its relative path.</summary>
    CopiedAdjacent,
    /// <summary>The target PowerShell environment must resolve this external requirement.</summary>
    ExternalRequirement,
    /// <summary>The current artifact shape deliberately does not include this dependency.</summary>
    NotIncluded,
    /// <summary>A required local dependency was not present.</summary>
    Missing
}

/// <summary>One deterministic dependency or resource decision.</summary>
public sealed class PowerShellCompilationDependency
{
    /// <summary>Creates a dependency result.</summary>
    public PowerShellCompilationDependency(
        string name,
        string? sourcePath,
        string relativePath,
        PowerShellCompilationDependencyKind kind,
        PowerShellCompilationDependencyDiscovery discovery,
        PowerShellCompilationDependencyDisposition disposition,
        bool exists,
        long sizeBytes,
        string note)
    {
        Name = name ?? string.Empty;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath;
        RelativePath = relativePath ?? string.Empty;
        Kind = kind;
        Discovery = discovery;
        Disposition = disposition;
        Exists = exists;
        SizeBytes = sizeBytes;
        Note = note ?? string.Empty;
    }

    /// <summary>File name or external requirement name.</summary>
    public string Name { get; }
    /// <summary>Resolved local source path, or null for external requirements.</summary>
    public string? SourcePath { get; }
    /// <summary>Contained module-relative path or external identity.</summary>
    public string RelativePath { get; }
    /// <summary>Dependency content kind.</summary>
    public PowerShellCompilationDependencyKind Kind { get; }
    /// <summary>Discovery source.</summary>
    public PowerShellCompilationDependencyDiscovery Discovery { get; }
    /// <summary>Artifact delivery decision.</summary>
    public PowerShellCompilationDependencyDisposition Disposition { get; }
    /// <summary>Whether a local dependency existed during planning.</summary>
    public bool Exists { get; }
    /// <summary>Local content size in bytes.</summary>
    public long SizeBytes { get; }
    /// <summary>Concise support or runtime explanation.</summary>
    public string Note { get; }
}

/// <summary>Aggregated dependency/resource inventory for one product census.</summary>
public sealed class PowerShellCompilationDependencySummary
{
    /// <summary>Creates a dependency summary.</summary>
    public PowerShellCompilationDependencySummary(
        PowerShellCompilationDependencyKind kind,
        PowerShellCompilationDependencyDisposition disposition,
        int files,
        int missing,
        long sizeBytes)
    {
        Kind = kind;
        Disposition = disposition;
        Files = files;
        Missing = missing;
        SizeBytes = sizeBytes;
    }

    /// <summary>Dependency kind.</summary>
    public PowerShellCompilationDependencyKind Kind { get; }
    /// <summary>Artifact delivery decision.</summary>
    public PowerShellCompilationDependencyDisposition Disposition { get; }
    /// <summary>Number of dependencies.</summary>
    public int Files { get; }
    /// <summary>Number of required local dependencies not found.</summary>
    public int Missing { get; }
    /// <summary>Total discovered local bytes.</summary>
    public long SizeBytes { get; }
}
