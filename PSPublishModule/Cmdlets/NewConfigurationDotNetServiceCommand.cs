using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates service packaging options for DotNet publish targets.
/// </summary>
/// <example>
/// <summary>Create service package options</summary>
/// <code>
/// $lifecycle = New-ConfigurationDotNetServiceLifecycle -Enabled
/// New-ConfigurationDotNetService -ServiceName 'My.Service' -GenerateInstallScript -GenerateUninstallScript -Lifecycle $lifecycle
/// </code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetService")]
[OutputType(typeof(DotNetPublishServicePackageOptions))]
public sealed class NewConfigurationDotNetServiceCommand : PSCmdlet
{
    /// <summary>
    /// Service name.
    /// </summary>
    [Parameter]
    public string? ServiceName { get; set; }

    /// <summary>
    /// Display name.
    /// </summary>
    [Parameter]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description text.
    /// </summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>
    /// Executable path relative to output.
    /// </summary>
    [Parameter]
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Optional service arguments.
    /// </summary>
    [Parameter]
    public string? Arguments { get; set; }

    /// <summary>
    /// Generates Install-Service.ps1.
    /// </summary>
    [Parameter]
    public bool GenerateInstallScript { get; set; } = true;

    /// <summary>
    /// Generates Uninstall-Service.ps1.
    /// </summary>
    [Parameter]
    public bool GenerateUninstallScript { get; set; } = true;

    /// <summary>
    /// Generates Run-Once.ps1.
    /// </summary>
    [Parameter]
    public bool GenerateRunOnceScript { get; set; }

    /// <summary>
    /// Optional lifecycle settings.
    /// </summary>
    [Parameter]
    public DotNetPublishServiceLifecycleOptions? Lifecycle { get; set; }

    /// <summary>
    /// Optional recovery settings.
    /// </summary>
    [Parameter]
    public DotNetPublishServiceRecoveryOptions? Recovery { get; set; }

    /// <summary>
    /// Optional config bootstrap rules.
    /// </summary>
    [Parameter]
    public DotNetPublishConfigBootstrapRule[]? ConfigBootstrap { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishServicePackageOptions"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishServicePackageOptions
        {
            ServiceName = NormalizeNullable(ServiceName),
            DisplayName = NormalizeNullable(DisplayName),
            Description = NormalizeNullable(Description),
            ExecutablePath = NormalizeNullable(ExecutablePath),
            Arguments = NormalizeNullable(Arguments),
            GenerateInstallScript = GenerateInstallScript,
            GenerateUninstallScript = GenerateUninstallScript,
            GenerateRunOnceScript = GenerateRunOnceScript,
            Lifecycle = Lifecycle,
            Recovery = Recovery,
            ConfigBootstrap = (ConfigBootstrap ?? System.Array.Empty<DotNetPublishConfigBootstrapRule>())
                .Where(r => r is not null)
                .ToArray()
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

