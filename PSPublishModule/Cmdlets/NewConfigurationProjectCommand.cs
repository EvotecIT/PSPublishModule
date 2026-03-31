using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a PowerShell-first project/release object for the unified PowerForge release engine.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProject")]
[OutputType(typeof(ConfigurationProject))]
public sealed class NewConfigurationProjectCommand : PSCmdlet
{
    /// <summary>
    /// Friendly project name.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional project root used for resolving relative paths.
    /// </summary>
    [Parameter]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Optional release-level defaults.
    /// </summary>
    [Parameter]
    public ConfigurationProjectRelease? Release { get; set; }

    /// <summary>
    /// Optional workspace defaults.
    /// </summary>
    [Parameter]
    public ConfigurationProjectWorkspace? Workspace { get; set; }

    /// <summary>
    /// Optional signing defaults.
    /// </summary>
    [Parameter]
    public ConfigurationProjectSigning? Signing { get; set; }

    /// <summary>
    /// Optional output defaults.
    /// </summary>
    [Parameter]
    public ConfigurationProjectOutput? Output { get; set; }

    /// <summary>
    /// Project targets.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateCount(1, int.MaxValue)]
    public ConfigurationProjectTarget[] Target { get; set; } = Array.Empty<ConfigurationProjectTarget>();

    /// <summary>
    /// Optional installers.
    /// </summary>
    [Parameter]
    public ConfigurationProjectInstaller[]? Installer { get; set; }

    /// <summary>
    /// Emits a <see cref="ConfigurationProject"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var targets = Target
            .Where(value => value is not null)
            .ToArray();
        if (targets.Length == 0)
            throw new PSArgumentException("At least one Target is required.");

        WriteObject(new ConfigurationProject
        {
            Name = Name.Trim(),
            ProjectRoot = NormalizeNullable(ProjectRoot),
            Release = Release ?? new ConfigurationProjectRelease(),
            Workspace = Workspace,
            Signing = Signing,
            Output = Output,
            Targets = targets,
            Installers = (Installer ?? Array.Empty<ConfigurationProjectInstaller>())
                .Where(value => value is not null)
                .ToArray()
        });
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
