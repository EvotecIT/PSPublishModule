using System.Collections.Specialized;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Creates configuration for PowerShell compatibility checking during module build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationCompatibility")]
public sealed class NewConfigurationCompatibilityCommand : PSCmdlet
{
    /// <summary>Enable PowerShell compatibility checking during build.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Fail the build if compatibility issues are found.</summary>
    [Parameter] public SwitchParameter FailOnIncompatibility { get; set; }

    /// <summary>Require PowerShell 5.1 compatibility.</summary>
    [Parameter] public SwitchParameter RequirePS51Compatibility { get; set; }

    /// <summary>Require PowerShell 7 compatibility.</summary>
    [Parameter] public SwitchParameter RequirePS7Compatibility { get; set; }

    /// <summary>Require cross-version compatibility (both PS 5.1 and PS 7).</summary>
    [Parameter] public SwitchParameter RequireCrossCompatibility { get; set; }

    /// <summary>Minimum percentage of files that must be cross-compatible. Default is 95.</summary>
    [Parameter] public int MinimumCompatibilityPercentage { get; set; } = 95;

    /// <summary>Directory names to exclude from compatibility analysis.</summary>
    [Parameter] public string[] ExcludeDirectories { get; set; } = new[] { "Artefacts", "Ignore", ".git", ".vs", "bin", "obj" };

    /// <summary>Export detailed compatibility report to the artifacts directory.</summary>
    [Parameter] public SwitchParameter ExportReport { get; set; }

    /// <summary>Custom filename for the compatibility report.</summary>
    [Parameter] public string ReportFileName { get; set; } = "PowerShellCompatibilityReport.csv";

    /// <summary>Emits compatibility-check configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var settings = new OrderedDictionary
        {
            ["Enable"] = Enable.IsPresent,
            ["FailOnIncompatibility"] = FailOnIncompatibility.IsPresent,
            ["RequirePS51Compatibility"] = RequirePS51Compatibility.IsPresent,
            ["RequirePS7Compatibility"] = RequirePS7Compatibility.IsPresent,
            ["RequireCrossCompatibility"] = RequireCrossCompatibility.IsPresent,
            ["MinimumCompatibilityPercentage"] = MinimumCompatibilityPercentage,
            ["ExcludeDirectories"] = ExcludeDirectories,
            ["ExportReport"] = ExportReport.IsPresent,
            ["ReportFileName"] = ReportFileName
        };

        var cfg = new OrderedDictionary
        {
            ["Type"] = "Compatibility",
            ["Settings"] = settings
        };

        WriteObject(cfg);
    }
}

