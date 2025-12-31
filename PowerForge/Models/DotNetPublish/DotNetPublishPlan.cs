namespace PowerForge;

/// <summary>
/// Planned execution for a dotnet publish run (resolved paths + ordered steps).
/// </summary>
public sealed class DotNetPublishPlan
{
    /// <summary>Project root used for resolving relative paths.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Build configuration (Release/Debug).</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Optional resolved solution path.</summary>
    public string? SolutionPath { get; set; }

    /// <summary>When true, runs dotnet restore before publishing.</summary>
    public bool Restore { get; set; }

    /// <summary>When true, runs dotnet clean before publishing.</summary>
    public bool Clean { get; set; }

    /// <summary>When true, runs dotnet build before publishing.</summary>
    public bool Build { get; set; }

    /// <summary>When true, uses --no-restore during publish.</summary>
    public bool NoRestoreInPublish { get; set; }

    /// <summary>When true, uses --no-build during publish.</summary>
    public bool NoBuildInPublish { get; set; }

    /// <summary>Resolved MSBuild properties.</summary>
    public Dictionary<string, string> MsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolved targets (paths + publish options).</summary>
    public DotNetPublishTargetPlan[] Targets { get; set; } = Array.Empty<DotNetPublishTargetPlan>();

    /// <summary>Resolved output settings.</summary>
    public DotNetPublishOutputs Outputs { get; set; } = new();

    /// <summary>Ordered steps that will be executed.</summary>
    public DotNetPublishStep[] Steps { get; set; } = Array.Empty<DotNetPublishStep>();
}

/// <summary>
/// Resolved plan entry for a single publish target.
/// </summary>
public sealed class DotNetPublishTargetPlan
{
    /// <summary>Target name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target kind.</summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>Resolved project path (*.csproj).</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Resolved publish options.</summary>
    public DotNetPublishPublishOptions Publish { get; set; } = new();
}

/// <summary>
/// A single executable step in the dotnet publish pipeline.
/// </summary>
public sealed class DotNetPublishStep
{
    /// <summary>Stable step key used to map progress updates.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Step kind.</summary>
    public DotNetPublishStepKind Kind { get; set; }

    /// <summary>Human-friendly step title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional target name for publish steps.</summary>
    public string? TargetName { get; set; }

    /// <summary>Optional target framework for publish steps.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional runtime identifier for publish steps.</summary>       
    public string? Runtime { get; set; }
}
