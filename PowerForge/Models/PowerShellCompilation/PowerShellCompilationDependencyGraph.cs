namespace PowerForge;

/// <summary>Semantic roles served by one stable dependency-graph node.</summary>
[Flags]
public enum PowerShellCompilationDependencyGraphRole
{
    /// <summary>No graph role was assigned.</summary>
    None = 0,
    /// <summary>The node participates in semantic analysis.</summary>
    Semantic = 1,
    /// <summary>The node participates in dependency resolution.</summary>
    Dependency = 2,
    /// <summary>The node participates in artifact deployment.</summary>
    Deployment = 4
}

/// <summary>Exact technical class of a dependency-graph node.</summary>
public enum PowerShellCompilationDependencyNodeKind
{
    /// <summary>PowerShell script.</summary>
    Script,
    /// <summary>PowerShell script module.</summary>
    ScriptModule,
    /// <summary>PowerShell module manifest.</summary>
    ModuleManifest,
    /// <summary>PowerShell binary module.</summary>
    BinaryModule,
    /// <summary>Module containing both script and binary entry points.</summary>
    MixedModule,
    /// <summary>CDXML/CIM module.</summary>
    CdxmlModule,
    /// <summary>Implicit-remoting or generated dynamic proxy.</summary>
    DynamicProxyModule,
    /// <summary>Managed CLR library.</summary>
    ManagedLibrary,
    /// <summary>Native library.</summary>
    NativeLibrary,
    /// <summary>External process target.</summary>
    ExternalProcess,
    /// <summary>Windows COM activation contract.</summary>
    ComObject,
    /// <summary>PowerShell type data.</summary>
    TypeData,
    /// <summary>PowerShell format data.</summary>
    FormatData,
    /// <summary>Other artifact content.</summary>
    Content,
    /// <summary>External PowerShell module identity.</summary>
    ExternalModule
}

/// <summary>Static reason for one graph edge.</summary>
public enum PowerShellCompilationDependencyEdgeKind
{
    /// <summary>Explicit compilation input.</summary>
    BuildInput,
    /// <summary>Module-manifest root module.</summary>
    RootModule,
    /// <summary>Module-manifest RequiredModules entry.</summary>
    RequiredModule,
    /// <summary>Module-manifest NestedModules entry.</summary>
    NestedModule,
    /// <summary>Module-manifest RequiredAssemblies entry.</summary>
    RequiredAssembly,
    /// <summary>Module initialization hook such as ScriptsToProcess.</summary>
    ModuleInitialization,
    /// <summary>Type or format metadata.</summary>
    Metadata,
    /// <summary>Literal dot-source expression.</summary>
    DotSource,
    /// <summary>Using-module statement.</summary>
    UsingModule,
    /// <summary>Using-assembly statement.</summary>
    UsingAssembly,
    /// <summary>#requires module declaration.</summary>
    RequiresModule,
    /// <summary>Literal Import-Module invocation.</summary>
    ImportModule,
    /// <summary>Literal managed CLR reference.</summary>
    ManagedReference,
    /// <summary>Literal native-library load.</summary>
    NativeLoad,
    /// <summary>Literal external-process target.</summary>
    ProcessTarget,
    /// <summary>Runtime asset or ordinary payload.</summary>
    RuntimeAsset
}

/// <summary>Artifact treatment assigned to every dependency graph node.</summary>
public enum PowerShellCompilationDependencyGraphDisposition
{
    /// <summary>Behavior is compiled into generated CLR code.</summary>
    Compiled,
    /// <summary>The artifact references the dependency.</summary>
    Referenced,
    /// <summary>The dependency remains hosted by PowerShell or the operating system.</summary>
    Hosted,
    /// <summary>The dependency is carried in the artifact.</summary>
    Bundled,
    /// <summary>The dependency must be restored into a private artifact closure.</summary>
    PrivateRestored,
    /// <summary>The target environment must provide the dependency.</summary>
    External,
    /// <summary>The selected artifact contract rejects the dependency.</summary>
    Rejected
}

/// <summary>Exact, non-executing dependency identity captured for locking.</summary>
public sealed class PowerShellCompilationDependencyIdentity
{
    /// <summary>Logical name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Exact or constrained version text.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>SHA-256 for a local dependency.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>Resolved source path or repository/source identity.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>PowerShell edition requirement.</summary>
    public string Edition { get; set; } = string.Empty;
    /// <summary>Target framework identity.</summary>
    public string TargetFramework { get; set; } = string.Empty;
    /// <summary>Runtime identifier.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;
    /// <summary>Processor architecture.</summary>
    public string Architecture { get; set; } = string.Empty;
    /// <summary>Provenance statement.</summary>
    public string Provenance { get; set; } = string.Empty;
}

/// <summary>Non-technical delivery constraints kept separate from dependency resolution.</summary>
public sealed class PowerShellCompilationDependencyPolicy
{
    /// <summary>Whether redistribution was explicitly allowed, denied, or remains unknown.</summary>
    public string Redistribution { get; set; } = "Unknown";
    /// <summary>Publisher identity when known.</summary>
    public string Publisher { get; set; } = string.Empty;
    /// <summary>Signature state when known.</summary>
    public string Signature { get; set; } = "Unknown";
    /// <summary>Servicing owner or policy.</summary>
    public string Servicing { get; set; } = string.Empty;
    /// <summary>License expression or identity when known.</summary>
    public string License { get; set; } = string.Empty;
}

/// <summary>One stable node shared by semantic, dependency, and deployment graph views.</summary>
public sealed class PowerShellCompilationDependencyNode
{
    /// <summary>Relocation-stable content or external-identity key.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Technical node kind.</summary>
    public PowerShellCompilationDependencyNodeKind Kind { get; set; }
    /// <summary>Graph views using this node.</summary>
    public PowerShellCompilationDependencyGraphRole Roles { get; set; }
    /// <summary>Exact lock identity.</summary>
    public PowerShellCompilationDependencyIdentity Identity { get; set; } = new();
    /// <summary>Artifact disposition.</summary>
    public PowerShellCompilationDependencyGraphDisposition Disposition { get; set; }
    /// <summary>Whether the local target existed during read-only resolution.</summary>
    public bool Exists { get; set; }
    /// <summary>Technical resolution note.</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Delivery and legal policy, separate from technical resolution.</summary>
    public PowerShellCompilationDependencyPolicy Policy { get; set; } = new();
}

/// <summary>One ordered static edge in the locked dependency graph.</summary>
public sealed class PowerShellCompilationDependencyEdge
{
    /// <summary>Stable edge identity.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Origin node identity.</summary>
    public string FromId { get; set; } = string.Empty;
    /// <summary>Target node identity.</summary>
    public string ToId { get; set; } = string.Empty;
    /// <summary>Static discovery reason.</summary>
    public PowerShellCompilationDependencyEdgeKind Kind { get; set; }
    /// <summary>Deterministic order within the origin.</summary>
    public int Order { get; set; }
    /// <summary>Source or manifest evidence.</summary>
    public string Evidence { get; set; } = string.Empty;
}

/// <summary>Deterministic dependency graph and lock evidence used by analysis and artifact production.</summary>
public sealed class PowerShellCompilationDependencyGraph
{
    /// <summary>Graph schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Root input node identity.</summary>
    public string RootNodeId { get; set; } = string.Empty;
    /// <summary>Stable graph nodes.</summary>
    public PowerShellCompilationDependencyNode[] Nodes { get; set; } = Array.Empty<PowerShellCompilationDependencyNode>();
    /// <summary>Stable ordered graph edges.</summary>
    public PowerShellCompilationDependencyEdge[] Edges { get; set; } = Array.Empty<PowerShellCompilationDependencyEdge>();
    /// <summary>Normalized SHA-256 over node and edge lock identities.</summary>
    public string LockSha256 { get; set; } = string.Empty;
    /// <summary>Detected dependency cycles.</summary>
    public string[][] Cycles { get; set; } = Array.Empty<string[]>();
    /// <summary>Detected incompatible exact-identity conflicts.</summary>
    public string[] Conflicts { get; set; } = Array.Empty<string>();
}
