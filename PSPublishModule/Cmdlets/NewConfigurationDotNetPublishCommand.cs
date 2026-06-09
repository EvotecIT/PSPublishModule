using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates DotNet publish configuration using DSL objects from a settings script block.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the DSL root for DotNet publish authoring in PSPublishModule.
/// It accepts optional global options and merges child objects emitted by <c>-Settings</c> such as:
/// <c>New-ConfigurationDotNetTarget</c>, <c>New-ConfigurationDotNetInstaller</c>, and <c>New-ConfigurationDotNetSign</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a basic DotNet publish spec from DSL</summary>
/// <code>
/// New-ConfigurationDotNetPublish -IncludeSchema -ProjectRoot '.' -Configuration 'Release' -Settings {
///     New-ConfigurationDotNetTarget -Name 'PowerForge.Cli' -ProjectPath 'PowerForge.Cli/PowerForge.Cli.csproj' -Framework 'net10.0' -Runtimes 'win-x64' -Style PortableCompat -Zip
/// }
/// </code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetPublish")]
[OutputType(typeof(DotNetPublishSpec))]
public sealed class NewConfigurationDotNetPublishCommand : PSCmdlet
{
    /// <summary>
    /// Optional settings script block that emits DotNet publish DSL objects.
    /// </summary>
    [Parameter]
    public ScriptBlock? Settings { get; set; }

    /// <summary>
    /// When set, adds a relative schema reference to generated config.
    /// </summary>
    [Parameter]
    public SwitchParameter IncludeSchema { get; set; }

    /// <summary>
    /// Optional schema version value.
    /// </summary>
    [Parameter]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Optional active profile name.
    /// </summary>
    [Parameter]
    public string? Profile { get; set; }

    /// <summary>
    /// Optional project root.
    /// </summary>
    [Parameter]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Optional solution path used for restore/build/clean.
    /// </summary>
    [Parameter]
    public string? SolutionPath { get; set; }

    /// <summary>
    /// Build configuration used for build/publish.
    /// </summary>
    [Parameter]
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Default runtime identifiers.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Enables restore step.
    /// </summary>
    [Parameter]
    public bool Restore { get; set; } = true;

    /// <summary>
    /// Enables clean step.
    /// </summary>
    [Parameter]
    public bool Clean { get; set; }

    /// <summary>
    /// Enables build step.
    /// </summary>
    [Parameter]
    public bool Build { get; set; } = true;

    /// <summary>
    /// Uses --no-restore during publish.
    /// </summary>
    [Parameter]
    public bool NoRestoreInPublish { get; set; } = true;

    /// <summary>
    /// Uses --no-build during publish.
    /// </summary>
    [Parameter]
    public bool NoBuildInPublish { get; set; } = true;

    /// <summary>
    /// Optional JSON manifest output path.
    /// </summary>
    [Parameter]
    public string? ManifestJsonPath { get; set; }

    /// <summary>
    /// Optional text manifest output path.
    /// </summary>
    [Parameter]
    public string? ManifestTextPath { get; set; }

    /// <summary>
    /// Optional checksums output path.
    /// </summary>
    [Parameter]
    public string? ChecksumsPath { get; set; }

    /// <summary>
    /// Optional run report output path.
    /// </summary>
    [Parameter]
    public string? RunReportPath { get; set; }

    /// <summary>
    /// Additional targets to append.
    /// </summary>
    [Parameter]
    public DotNetPublishTarget[]? Targets { get; set; }

    /// <summary>
    /// Additional installers to append.
    /// </summary>
    [Parameter]
    public DotNetPublishInstaller[]? Installers { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishSpec"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var spec = DotNetPublishDslComposer.ComposeFromSettings(Settings, new DotNetPublishSpec(), message => WriteWarning(message));
        var bound = MyInvocation.BoundParameters;

        if (IncludeSchema.IsPresent)
            spec.Schema = "./Schemas/powerforge.dotnetpublish.schema.json";
        if (bound.ContainsKey(nameof(SchemaVersion)))
            spec.SchemaVersion = SchemaVersion <= 0 ? 1 : SchemaVersion;
        if (bound.ContainsKey(nameof(Profile)))
            spec.Profile = NormalizeNullable(Profile);

        spec.DotNet ??= new DotNetPublishDotNetOptions();
        if (bound.ContainsKey(nameof(ProjectRoot)))
            spec.DotNet.ProjectRoot = NormalizeNullable(ProjectRoot);
        if (bound.ContainsKey(nameof(SolutionPath)))
            spec.DotNet.SolutionPath = NormalizeNullable(SolutionPath);
        if (bound.ContainsKey(nameof(Configuration)))
            spec.DotNet.Configuration = string.IsNullOrWhiteSpace(Configuration) ? "Release" : Configuration!.Trim();
        if (bound.ContainsKey(nameof(Runtimes)))
            spec.DotNet.Runtimes = NormalizeStrings(Runtimes);
        if (bound.ContainsKey(nameof(Restore)))
            spec.DotNet.Restore = Restore;
        if (bound.ContainsKey(nameof(Clean)))
            spec.DotNet.Clean = Clean;
        if (bound.ContainsKey(nameof(Build)))
            spec.DotNet.Build = Build;
        if (bound.ContainsKey(nameof(NoRestoreInPublish)))
            spec.DotNet.NoRestoreInPublish = NoRestoreInPublish;
        if (bound.ContainsKey(nameof(NoBuildInPublish)))
            spec.DotNet.NoBuildInPublish = NoBuildInPublish;

        spec.Outputs ??= new DotNetPublishOutputs();
        if (bound.ContainsKey(nameof(ManifestJsonPath)))
            spec.Outputs.ManifestJsonPath = NormalizeNullable(ManifestJsonPath);
        if (bound.ContainsKey(nameof(ManifestTextPath)))
            spec.Outputs.ManifestTextPath = NormalizeNullable(ManifestTextPath);
        if (bound.ContainsKey(nameof(ChecksumsPath)))
            spec.Outputs.ChecksumsPath = NormalizeNullable(ChecksumsPath);
        if (bound.ContainsKey(nameof(RunReportPath)))
            spec.Outputs.RunReportPath = NormalizeNullable(RunReportPath);

        if (Targets is { Length: > 0 })
            spec.Targets = (spec.Targets ?? Array.Empty<DotNetPublishTarget>())
                .Concat(Targets.Where(t => t is not null))
                .ToArray();

        if (Installers is { Length: > 0 })
            spec.Installers = (spec.Installers ?? Array.Empty<DotNetPublishInstaller>())
                .Concat(Installers.Where(i => i is not null))
                .ToArray();

        WriteObject(spec);
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0) return Array.Empty<string>();
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
