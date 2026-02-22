using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a named profile for DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create release profile</summary>
/// <code>New-ConfigurationDotNetProfile -Name 'release' -Default -Targets 'Service','Cli' -Runtimes 'win-x64','win-arm64'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetProfile")]
[OutputType(typeof(DotNetPublishProfile))]
public sealed class NewConfigurationDotNetProfileCommand : PSCmdlet
{
    /// <summary>
    /// Profile name.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Marks this profile as default.
    /// </summary>
    [Parameter]
    public SwitchParameter Default { get; set; }

    /// <summary>
    /// Optional target name filters.
    /// </summary>
    [Parameter]
    public string[]? Targets { get; set; }

    /// <summary>
    /// Optional runtime overrides.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional framework overrides.
    /// </summary>
    [Parameter]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Optional style override.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle? Style { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishProfile"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishProfile
        {
            Name = Name.Trim(),
            Default = Default.IsPresent,
            Targets = NormalizeStrings(Targets),
            Runtimes = NormalizeStrings(Runtimes),
            Frameworks = NormalizeStrings(Frameworks),
            Style = Style
        });
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
}

