namespace PowerForge;

/// <summary>
/// Typed specification for running a dotnet publish workflow driven by JSON configuration.
/// Intended for producing small, reproducible distributable outputs (single-file, self-contained, AOT optional).
/// </summary>
public sealed class DotNetPublishSpec
{
    /// <summary>
    /// Optional schema version for external tooling.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Global dotnet settings (restore/build behavior, solution path, default runtimes).
    /// </summary>
    public DotNetPublishDotNetOptions DotNet { get; set; } = new();

    /// <summary>
    /// Publish targets.
    /// </summary>
    public DotNetPublishTarget[] Targets { get; set; } = Array.Empty<DotNetPublishTarget>();

    /// <summary>
    /// Optional manifest output configuration.
    /// </summary>
    public DotNetPublishOutputs Outputs { get; set; } = new();
}

/// <summary>
/// Global options for dotnet restore/build/publish.
/// </summary>
public sealed class DotNetPublishDotNetOptions
{
    /// <summary>
    /// Optional project root. When omitted, the directory containing the config file is used.
    /// All relative paths in the spec are resolved against this root.
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Optional solution path to restore/clean/build before publishing. When omitted, the pipeline restores/builds each target project.
    /// </summary>
    public string? SolutionPath { get; set; }

    /// <summary>
    /// Build configuration (Release/Debug). Default: Release.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// When true, runs <c>dotnet restore</c> before building/publishing.
    /// </summary>
    public bool Restore { get; set; } = true;

    /// <summary>
    /// When true, runs <c>dotnet clean</c> before building/publishing.
    /// </summary>
    public bool Clean { get; set; }

    /// <summary>
    /// When true, runs <c>dotnet build</c> before publishing and uses <c>--no-build</c> in publish by default.
    /// </summary>
    public bool Build { get; set; } = true;

    /// <summary>
    /// When true, publishes with <c>--no-restore</c> (recommended when <see cref="Restore"/> is true).
    /// </summary>
    public bool NoRestoreInPublish { get; set; } = true;

    /// <summary>
    /// When true, publishes with <c>--no-build</c> (recommended when <see cref="Build"/> is true).
    /// </summary>
    public bool NoBuildInPublish { get; set; } = true;

    /// <summary>
    /// Default runtime identifiers to publish for (when a target does not specify its own runtimes).
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional MSBuild properties passed to build/publish (as <c>/p:Name=Value</c>).
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// Output settings for dotnet publish pipeline manifests.
/// </summary>
public sealed class DotNetPublishOutputs
{
    /// <summary>
    /// Optional path for a JSON manifest file that summarizes produced artefacts.
    /// When omitted, defaults to <c>Artifacts/DotNetPublish/manifest.json</c> under the project root.
    /// </summary>
    public string? ManifestJsonPath { get; set; }

    /// <summary>
    /// Optional path for a text manifest file that summarizes produced artefacts.
    /// When omitted, defaults to <c>Artifacts/DotNetPublish/manifest.txt</c> under the project root.
    /// </summary>
    public string? ManifestTextPath { get; set; }
}

