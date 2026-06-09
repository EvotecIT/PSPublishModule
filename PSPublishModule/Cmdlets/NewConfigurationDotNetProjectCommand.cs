using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a project catalog entry for DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create a project catalog item</summary>
/// <code>New-ConfigurationDotNetProject -Id 'service' -Path 'src/My.Service/My.Service.csproj' -Group 'apps'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetProject")]
[OutputType(typeof(DotNetPublishProject))]
public sealed class NewConfigurationDotNetProjectCommand : PSCmdlet
{
    /// <summary>
    /// Project identifier used by targets and installers.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Path to project file.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional grouping label.
    /// </summary>
    [Parameter]
    public string? Group { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishProject"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishProject
        {
            Id = Id.Trim(),
            Path = Path.Trim(),
            Group = NormalizeNullable(Group)
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

