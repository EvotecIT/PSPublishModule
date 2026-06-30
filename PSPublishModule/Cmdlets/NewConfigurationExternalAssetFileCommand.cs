using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a file entry for an external asset bundle.
/// </summary>
/// <remarks>
/// <para>
/// File entries are passed to <c>New-ConfigurationExternalAsset</c>. Each entry declares where the build pipeline
/// obtains a file from, where it lands inside the output folder, and which runtime or architecture metadata should be
/// written to the generated manifest.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a web-download file entry</summary>
/// <code>New-ConfigurationExternalAssetFile -Runtime netcore -Architecture x64 -FileName tool.zip -Uri 'https://example.test/tool.zip'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationExternalAssetFile")]
[OutputType(typeof(ExternalAssetFileConfiguration))]
public sealed class NewConfigurationExternalAssetFileCommand : PSCmdlet
{
    /// <summary>Runtime or payload group, such as netcore, netfx, linux-x64, or win-x64.</summary>
    [Parameter(Mandatory = true)]
    public string? Runtime { get; set; }

    /// <summary>Optional architecture metadata written to the generated manifest.</summary>
    [Parameter]
    public string? Architecture { get; set; }

    /// <summary>Destination file name when Path is not specified.</summary>
    [Parameter(Mandatory = true)]
    public string? FileName { get; set; }

    /// <summary>Destination path relative to the bundle output directory. Defaults to FileName.</summary>
    [Parameter]
    public string? Path { get; set; }

    /// <summary>HTTP(S) URI, file URI, rooted local path, or project-relative local path for this file.</summary>
    [Parameter(Mandatory = true)]
    [Alias("Url")]
    public string? Uri { get; set; }

    /// <summary>Optional expected SHA256. When provided, mismatches fail the build.</summary>
    [Parameter]
    public string? Sha256 { get; set; }

    /// <summary>Emits the external asset file configuration object.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ExternalAssetFileConfiguration
        {
            Runtime = Runtime ?? string.Empty,
            Architecture = Architecture,
            FileName = FileName ?? string.Empty,
            Path = Path,
            Uri = Uri ?? string.Empty,
            Sha256 = Sha256
        });
    }
}

