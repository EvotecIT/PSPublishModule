using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Helps define custom placeholders replacing content within a script or module during the build process.
/// </summary>
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
