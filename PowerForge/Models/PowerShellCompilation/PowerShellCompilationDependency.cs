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
    ConventionalRuntimeDirectory,
    /// <summary>An explicit IncludeResource pattern.</summary>
    ExplicitResourceInclude,
    /// <summary>A contained literal path rooted at PSScriptRoot.</summary>
    InferredLiteralResource,
    /// <summary>Optional module-root content without a stronger declaration.</summary>
    OptionalPayload
}

/// <summary>Why a local dependency is required, included, excluded, or only inventoried.</summary>
public enum PowerShellCompilationDependencySelection
{
    /// <summary>Authored source or manifest input rather than optional payload.</summary>
    Source,
    /// <summary>Required by a module manifest and therefore not excludable.</summary>
    Required,
    /// <summary>Included by an explicit resource pattern.</summary>
    ExplicitInclude,
    /// <summary>Inferred from a high-confidence contained PSScriptRoot literal.</summary>
    Inferred,
    /// <summary>Included because CompleteModule resource mode was selected.</summary>
    PolicyInclude,
    /// <summary>Optional content intentionally excluded by configuration.</summary>
    Excluded,
    /// <summary>Optional content inventoried but not selected for delivery.</summary>
    Unclassified,
    /// <summary>An external runtime requirement rather than local payload.</summary>
    External
}

/// <summary>Policy used to select optional payload in addition to manifest-required content.</summary>
public enum PowerShellCompilationResourceMode
{
    /// <summary>Include manifest-required, explicitly included, and safely inferred resources.</summary>
    Declared,
    /// <summary>Include every contained optional module file except explicit exclusions.</summary>
    CompleteModule,
    /// <summary>Include only manifest-required and explicitly included resources.</summary>
    None
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
        string note,
        PowerShellCompilationDependencySelection selection = PowerShellCompilationDependencySelection.Source)
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
        Selection = selection;
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
    /// <summary>Resource-selection reason used by analysis and artifact planning.</summary>
    public PowerShellCompilationDependencySelection Selection { get; }
}

/// <summary>Resource-selection totals for analysis, census, and artifact evidence.</summary>
public sealed class PowerShellCompilationResourceSummary
{
    /// <summary>Creates totals from dependency/resource decisions.</summary>
    public static PowerShellCompilationResourceSummary Create(IEnumerable<PowerShellCompilationDependency> dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var local = dependencies.Where(static dependency => dependency.SourcePath is not null).ToArray();
        var included = local.Where(static dependency => dependency.Exists &&
            (dependency.Selection is PowerShellCompilationDependencySelection.Required or
                PowerShellCompilationDependencySelection.ExplicitInclude or
                PowerShellCompilationDependencySelection.Inferred or
                PowerShellCompilationDependencySelection.PolicyInclude) &&
            (dependency.Disposition is PowerShellCompilationDependencyDisposition.CopiedAdjacent or
                PowerShellCompilationDependencyDisposition.Embedded or
                PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted or
                PowerShellCompilationDependencyDisposition.Compiled or
                PowerShellCompilationDependencyDisposition.PreservedScript)).ToArray();
        var required = local.Where(static dependency => dependency.Selection == PowerShellCompilationDependencySelection.Required).ToArray();
        var inferred = local.Where(static dependency => dependency.Selection == PowerShellCompilationDependencySelection.Inferred).ToArray();
        var excluded = local.Where(static dependency => dependency.Selection == PowerShellCompilationDependencySelection.Excluded).ToArray();
        var unclassified = local.Where(static dependency => dependency.Selection == PowerShellCompilationDependencySelection.Unclassified).ToArray();
        return new PowerShellCompilationResourceSummary
        {
            IncludedFiles = included.Length,
            IncludedBytes = included.Sum(static dependency => dependency.SizeBytes),
            RequiredFiles = required.Length,
            RequiredBytes = required.Sum(static dependency => dependency.SizeBytes),
            InferredFiles = inferred.Length,
            InferredBytes = inferred.Sum(static dependency => dependency.SizeBytes),
            ExcludedFiles = excluded.Length,
            ExcludedBytes = excluded.Sum(static dependency => dependency.SizeBytes),
            UnclassifiedFiles = unclassified.Length,
            UnclassifiedBytes = unclassified.Sum(static dependency => dependency.SizeBytes)
        };
    }

    /// <summary>All local payload files selected for delivery.</summary>
    public int IncludedFiles { get; set; }
    /// <summary>Total size of selected local payload.</summary>
    public long IncludedBytes { get; set; }
    /// <summary>Manifest-required local files, which are also included.</summary>
    public int RequiredFiles { get; set; }
    /// <summary>Total size of manifest-required local files.</summary>
    public long RequiredBytes { get; set; }
    /// <summary>Safely inferred local resource files, which are also included.</summary>
    public int InferredFiles { get; set; }
    /// <summary>Total size of safely inferred local resource files.</summary>
    public long InferredBytes { get; set; }
    /// <summary>Optional files excluded by configuration.</summary>
    public int ExcludedFiles { get; set; }
    /// <summary>Total size of optional files excluded by configuration.</summary>
    public long ExcludedBytes { get; set; }
    /// <summary>Inventoried optional files without an inclusion decision.</summary>
    public int UnclassifiedFiles { get; set; }
    /// <summary>Total size of inventoried optional files without an inclusion decision.</summary>
    public long UnclassifiedBytes { get; set; }
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
