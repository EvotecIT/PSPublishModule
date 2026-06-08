using System;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

public sealed partial class InvokePowerForgeReleaseCommand
{
    /// <summary>
    /// Submits generated Winget manifests with wingetcreate after release assets are available.
    /// </summary>
    [Parameter]
    public SwitchParameter SubmitWinget { get; set; }

    /// <summary>
    /// Disables Winget submission even when enabled by release configuration.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipWingetSubmit { get; set; }

    /// <summary>
    /// Winget submission mode used by wingetcreate.
    /// </summary>
    [Parameter]
    [ValidateSet("Manifest", "Update")]
    public string? WingetSubmitMode { get; set; }

    /// <summary>
    /// Optional wingetcreate executable path.
    /// </summary>
    [Parameter]
    public string? WingetToolPath { get; set; }

    /// <summary>
    /// Environment variable containing the GitHub token for wingetcreate.
    /// </summary>
    [Parameter]
    public string? WingetTokenEnvName { get; set; }

    /// <summary>
    /// File containing the GitHub token for wingetcreate.
    /// </summary>
    [Parameter]
    public string? WingetTokenFilePath { get; set; }

    /// <summary>
    /// Pull request title template passed to wingetcreate.
    /// </summary>
    [Parameter]
    public string? WingetPullRequestTitle { get; set; }

    /// <summary>
    /// Allows wingetcreate to open the submitted pull request in a browser.
    /// </summary>
    [Parameter]
    public SwitchParameter WingetOpenBrowser { get; set; }

    /// <summary>
    /// Enables wingetcreate replacement mode.
    /// </summary>
    [Parameter]
    public SwitchParameter WingetReplace { get; set; }

    /// <summary>
    /// Optional version passed with wingetcreate replacement mode.
    /// </summary>
    [Parameter]
    public string? WingetReplaceVersion { get; set; }

    /// <summary>
    /// Allows wingetcreate to prompt for GitHub authentication when no token is resolved.
    /// </summary>
    [Parameter]
    public SwitchParameter WingetAllowInteractiveAuthentication { get; set; }

    /// <summary>
    /// Timeout in seconds for each wingetcreate invocation.
    /// </summary>
    [Parameter]
    public int? WingetTimeoutSeconds { get; set; }

    private static bool? ResolveWingetSubmitFlag(IDictionary<string, object>? boundParameters)
    {
        if (boundParameters?.ContainsKey(nameof(SkipWingetSubmit)) == true)
            return false;

        return ResolveRequestedFlag(boundParameters, nameof(SubmitWinget));
    }

    private static PowerForgeWingetSubmissionMode? ParseWingetSubmitMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse(value, ignoreCase: true, out PowerForgeWingetSubmissionMode mode))
            return mode;

        throw new PSArgumentException(
            $"Unknown Winget submit mode '{value}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(PowerForgeWingetSubmissionMode)))}");
    }
}
