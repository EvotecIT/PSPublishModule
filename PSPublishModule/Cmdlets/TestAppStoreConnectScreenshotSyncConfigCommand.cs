using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Validates an App Store Connect screenshot sync configuration against local files.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "AppStoreConnectScreenshotSyncConfig")]
[OutputType(typeof(bool), typeof(AppStoreConnectScreenshotSyncValidationResult))]
public sealed class TestAppStoreConnectScreenshotSyncConfigCommand : PSCmdlet
{
    /// <summary>Path to the screenshot sync JSON configuration file.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Return the full validation result instead of a Boolean.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Suppress validation warnings.</summary>
    [Parameter]
    public SwitchParameter Quiet { get; set; }

    /// <summary>Validates the screenshot sync configuration.</summary>
    protected override void ProcessRecord()
    {
        var resolvedConfigPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        var json = File.ReadAllText(resolvedConfigPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<AppStoreConnectScreenshotSyncSpec>(json, options)
            ?? throw new InvalidOperationException($"Unable to deserialize screenshot sync config: {resolvedConfigPath}");

        var result = new AppStoreConnectScreenshotSyncConfigValidator().Validate(
            spec,
            Path.GetDirectoryName(resolvedConfigPath) ?? SessionState.Path.CurrentFileSystemLocation.Path,
            resolvedConfigPath);

        if (!Quiet.IsPresent)
        {
            foreach (var message in result.Messages)
                WriteWarning(message);
            foreach (var set in result.ScreenshotSets)
            {
                foreach (var message in set.Messages)
                    WriteWarning($"{set.ScreenshotDisplayType}: {message}");
            }
        }

        WriteObject(PassThru.IsPresent ? result : result.IsValid);
    }
}
