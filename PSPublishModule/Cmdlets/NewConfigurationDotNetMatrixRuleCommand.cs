using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a matrix include/exclude rule for DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create a matrix rule</summary>
/// <code>New-ConfigurationDotNetMatrixRule -Targets 'Service*' -Runtime 'win-*' -Framework 'net10.0*' -Style 'Portable*'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetMatrixRule")]
[OutputType(typeof(DotNetPublishMatrixRule))]
public sealed class NewConfigurationDotNetMatrixRuleCommand : PSCmdlet
{
    /// <summary>
    /// Optional target name patterns.
    /// </summary>
    [Parameter]
    public string[]? Targets { get; set; }

    /// <summary>
    /// Optional runtime wildcard pattern.
    /// </summary>
    [Parameter]
    public string? Runtime { get; set; }

    /// <summary>
    /// Optional framework wildcard pattern.
    /// </summary>
    [Parameter]
    public string? Framework { get; set; }

    /// <summary>
    /// Optional style wildcard pattern.
    /// </summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishMatrixRule"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishMatrixRule
        {
            Targets = NormalizeStrings(Targets),
            Runtime = NormalizeNullable(Runtime),
            Framework = NormalizeNullable(Framework),
            Style = NormalizeNullable(Style)
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

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

