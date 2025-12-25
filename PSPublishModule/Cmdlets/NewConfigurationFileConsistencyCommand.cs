using System.Collections.Specialized;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Creates configuration for file consistency checking (encoding and line endings) during module build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationFileConsistency")]
public sealed class NewConfigurationFileConsistencyCommand : PSCmdlet
{
    /// <summary>Enable file consistency checking during build.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Fail the build if consistency issues are found.</summary>
    [Parameter] public SwitchParameter FailOnInconsistency { get; set; }

    /// <summary>Required file encoding.</summary>
    [Parameter] public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;

    /// <summary>Required line ending style.</summary>
    [Parameter] public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;

    /// <summary>Automatically fix encoding and line ending issues during build.</summary>
    [Parameter] public SwitchParameter AutoFix { get; set; }

    /// <summary>Create backup files before applying automatic fixes.</summary>
    [Parameter] public SwitchParameter CreateBackups { get; set; }

    /// <summary>Maximum percentage of files that can have consistency issues. Default is 5.</summary>
    [Parameter] public int MaxInconsistencyPercentage { get; set; } = 5;

    /// <summary>Directory names to exclude from consistency analysis.</summary>
    [Parameter] public string[] ExcludeDirectories { get; set; } = new[] { "Artefacts", "Ignore", ".git", ".vs", "bin", "obj" };

    /// <summary>Export detailed consistency report to the artifacts directory.</summary>
    [Parameter] public SwitchParameter ExportReport { get; set; }

    /// <summary>Custom filename for the consistency report.</summary>
    [Parameter] public string ReportFileName { get; set; } = "FileConsistencyReport.csv";

    /// <summary>Check for files with mixed line endings.</summary>
    [Parameter] public SwitchParameter CheckMixedLineEndings { get; set; }

    /// <summary>Check for files missing final newlines.</summary>
    [Parameter] public SwitchParameter CheckMissingFinalNewline { get; set; }

    /// <summary>Emits file-consistency configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var settings = new OrderedDictionary
        {
            ["Enable"] = Enable.IsPresent,
            ["FailOnInconsistency"] = FailOnInconsistency.IsPresent,
            ["RequiredEncoding"] = RequiredEncoding.ToString(),
            ["RequiredLineEnding"] = RequiredLineEnding.ToString(),
            ["AutoFix"] = AutoFix.IsPresent,
            ["CreateBackups"] = CreateBackups.IsPresent,
            ["MaxInconsistencyPercentage"] = MaxInconsistencyPercentage,
            ["ExcludeDirectories"] = ExcludeDirectories,
            ["ExportReport"] = ExportReport.IsPresent,
            ["ReportFileName"] = ReportFileName,
            ["CheckMixedLineEndings"] = CheckMixedLineEndings.IsPresent,
            ["CheckMissingFinalNewline"] = CheckMissingFinalNewline.IsPresent
        };

        var cfg = new OrderedDictionary
        {
            ["Type"] = "FileConsistency",
            ["Settings"] = settings
        };

        WriteObject(cfg);
    }
}

