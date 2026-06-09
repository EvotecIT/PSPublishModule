using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates matrix defaults and include/exclude filters for DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create matrix defaults</summary>
/// <code>New-ConfigurationDotNetMatrix -Runtimes 'win-x64','win-arm64' -Frameworks 'net10.0','net10.0-windows' -Styles PortableCompat,AotSpeed</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetMatrix")]
[OutputType(typeof(DotNetPublishMatrix))]
public sealed class NewConfigurationDotNetMatrixCommand : PSCmdlet
{
    /// <summary>
    /// Default runtimes.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Default frameworks.
    /// </summary>
    [Parameter]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Default styles.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Include rules.
    /// </summary>
    [Parameter]
    public DotNetPublishMatrixRule[]? Include { get; set; }

    /// <summary>
    /// Exclude rules.
    /// </summary>
    [Parameter]
    public DotNetPublishMatrixRule[]? Exclude { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishMatrix"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishMatrix
        {
            Runtimes = NormalizeStrings(Runtimes),
            Frameworks = NormalizeStrings(Frameworks),
            Styles = (Styles ?? Array.Empty<DotNetPublishStyle>()).Distinct().ToArray(),
            Include = (Include ?? Array.Empty<DotNetPublishMatrixRule>()).Where(i => i is not null).ToArray(),
            Exclude = (Exclude ?? Array.Empty<DotNetPublishMatrixRule>()).Where(i => i is not null).ToArray()
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

