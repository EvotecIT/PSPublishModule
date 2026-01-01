using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Helps define custom placeholders replacing content within a script or module during the build process.
/// </summary>
/// <remarks>
/// <para>
/// Placeholders are applied during merge/packaging so you can inject build-time values (versions, build IDs, timestamps)
/// without hardcoding them into source files.
/// </para>
/// </remarks>
/// <example>
/// <summary>Replace a single placeholder token</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationPlaceHolder -Find '{{ModuleVersion}}' -Replace '1.2.3'</code>
/// <para>Replaces all occurrences of <c>{{ModuleVersion}}</c> in merged content.</para>
/// </example>
/// <example>
/// <summary>Provide multiple replacements</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationPlaceHolder -CustomReplacement @{ Find='{{Company}}'; Replace='Evotec' }, @{ Find='{{Year}}'; Replace='2025' }</code>
/// <para>Emits multiple placeholder replacement segments in one call.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationPlaceHolder", DefaultParameterSetName = "FindAndReplace")]
public sealed class NewConfigurationPlaceHolderCommand : PSCmdlet
{
    /// <summary>Custom placeholder replacements. Accepts legacy hashtable array (@{ Find='..'; Replace='..' }) or <see cref="PlaceHolderReplacement"/>[].</summary>
    [Parameter(Mandatory = true, ParameterSetName = "CustomReplacement")]
    [PlaceHolderReplacementsTransformation]
    public PlaceHolderReplacement[]? CustomReplacement { get; set; }

    /// <summary>The string to find in the script or module content.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "FindAndReplace")]
    public string? Find { get; set; }

    /// <summary>The string to replace the Find string in the script or module content.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "FindAndReplace")]
    public string? Replace { get; set; }

    /// <summary>Emits placeholder replacement configuration.</summary>
    protected override void ProcessRecord()
    {
        if (CustomReplacement is not null)
        {
            foreach (var repl in CustomReplacement)
            {
                if (repl is null || string.IsNullOrWhiteSpace(repl.Find) || repl.Replace is null)
                    throw new PSArgumentException("CustomReplacement entries must contain two keys: Find and Replace.");

                WriteObject(new ConfigurationPlaceHolderSegment { Configuration = repl });
            }
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(Find)) && MyInvocation.BoundParameters.ContainsKey(nameof(Replace)))
        {
            WriteObject(new ConfigurationPlaceHolderSegment
            {
                Configuration = new PlaceHolderReplacement
                {
                    Find = Find ?? string.Empty,
                    Replace = Replace ?? string.Empty
                }
            });
        }
    }

}
