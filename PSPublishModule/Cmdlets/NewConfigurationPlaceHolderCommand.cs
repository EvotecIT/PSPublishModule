using System.Collections;
using System.Collections.Specialized;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Helps define custom placeholders replacing content within a script or module during the build process.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationPlaceHolder", DefaultParameterSetName = "FindAndReplace")]
public sealed class NewConfigurationPlaceHolderCommand : PSCmdlet
{
    /// <summary>Hashtable array with custom placeholders to replace.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "CustomReplacement")]
    public IDictionary[]? CustomReplacement { get; set; }

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
                var cfg = new OrderedDictionary
                {
                    ["Type"] = "PlaceHolder",
                    ["Configuration"] = repl
                };
                WriteObject(cfg);
            }
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(Find)) && MyInvocation.BoundParameters.ContainsKey(nameof(Replace)))
        {
            var cfg = new OrderedDictionary
            {
                ["Type"] = "PlaceHolder",
                ["Configuration"] = new Hashtable
                {
                    ["Find"] = Find,
                    ["Replace"] = Replace
                }
            };
            WriteObject(cfg);
        }
    }
}

