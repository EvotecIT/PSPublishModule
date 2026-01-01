using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Steps a version based on an expected version pattern (supports the legacy <c>X</c> placeholder).
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet supports two common workflows:
/// </para>
/// <list type="bullet">
/// <item><description>Local stepping using a module manifest (<c>-LocalPSD1</c>)</description></item>
/// <item><description>Remote stepping based on the latest published module version (<c>-Module</c>)</description></item>
/// </list>
/// <para>
/// When <c>-ExpectedVersion</c> contains an <c>X</c> placeholder (e.g. <c>1.2.X</c>),
/// the cmdlet resolves the next patch version. When an exact version is provided, it is returned as-is.
/// </para>
/// </remarks>
/// <example>
/// <summary>Step a version using a local module manifest</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Step-Version -ExpectedVersion '1.0.X' -LocalPSD1 'C:\Git\MyModule\MyModule.psd1'</code>
/// <para>Reads the current version from the PSD1 and returns the next patch version.</para>
/// </example>
/// <example>
/// <summary>Return the full step result object</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Step-Version -ExpectedVersion '1.0.X' -LocalPSD1 '.\MyModule.psd1' -Advanced</code>
/// <para>Returns a structured object that includes whether auto-versioning was used.</para>
/// </example>
/// <example>
/// <summary>Step based on the latest published module</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Step-Version -ExpectedVersion '1.0.X' -Module 'MyModule'</code>
/// <para>Resolves the next patch version by looking up the current version of the module.</para>
/// </example>
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
