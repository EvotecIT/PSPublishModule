using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Steps a version based on an expected version pattern (supports the legacy <c>X</c> placeholder).
/// </summary>
[Cmdlet("Step", "Version")]
[OutputType(typeof(string))]
[OutputType(typeof(ModuleVersionStepResult))]
public sealed class StepVersionCommand : PSCmdlet
{
    /// <summary>Optional module name used to resolve current version from PSGallery.</summary>
    [Parameter]
    public string? Module { get; set; }

    /// <summary>Expected version (exact or pattern like <c>0.1.X</c>).</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ExpectedVersion { get; set; } = string.Empty;

    /// <summary>When set, returns a typed result instead of only the version string.</summary>
    [Parameter]
    public SwitchParameter Advanced { get; set; }

    /// <summary>Optional local PSD1 path used to resolve current version.</summary>
    [Parameter]
    public string? LocalPSD1 { get; set; }

    /// <summary>Executes version stepping.</summary>
    protected override void ProcessRecord()
    {
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var stepper = new ModuleVersionStepper(logger);

        string? localPsd1Path = null;
        if (!string.IsNullOrWhiteSpace(LocalPSD1))
        {
            try { localPsd1Path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(LocalPSD1); }
            catch { localPsd1Path = LocalPSD1; }
        }

        var result = stepper.Step(ExpectedVersion, Module, localPsd1Path);

        // Preserve legacy behavior:
        // - exact version => return object
        // - pattern + -Advanced => return object
        // - pattern without -Advanced => return string
        if (!result.UsedAutoVersioning || Advanced.IsPresent)
        {
            WriteObject(result);
            return;
        }

        WriteObject(result.Version);
    }
}
