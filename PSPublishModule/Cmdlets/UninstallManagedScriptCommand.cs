using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Uninstalls script resources from a managed script root.
/// </summary>
/// <example>
/// <summary>Uninstall a script from the current user's script path.</summary>
/// <code>Uninstall-ManagedScript -Name Invoke-CompanyTask</code>
/// </example>
[Cmdlet(VerbsLifecycle.Uninstall, "ManagedScript", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedScriptUninstallResult), typeof(ManagedScriptUninstallPlan))]
public sealed class UninstallManagedScriptCommand : PSCmdlet
{
    /// <summary>Script resource names to uninstall.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ScriptName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Exact installed script version to uninstall. When omitted, any installed version is removed.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true)]
    [Alias("RequiredVersion", "RequestedVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Install scope used when ScriptRoot is not supplied.</summary>
    [Parameter]
    public ManagedScriptInstallScope Scope { get; set; } = ManagedScriptInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers script roots.</summary>
    [Parameter]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit script root. When supplied, Scope is treated as Custom.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true)]
    [Alias("Path", "DestinationPath", "ScriptPath")]
    [ValidateNotNullOrEmpty]
    public string? ScriptRoot { get; set; }

    /// <summary>Remove a script even when PSScriptInfo metadata cannot be read.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Return an inspectable uninstall plan without removing files.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Uninstalls requested scripts.</summary>
    protected override void ProcessRecord()
    {
        var scriptRoot = ResolveScriptRoot(this, ScriptRoot);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ManagedScriptResourceService(logger);

        foreach (var scriptName in Name)
        {
            var request = new ManagedScriptUninstallRequest
            {
                Name = scriptName,
                Scope = string.IsNullOrWhiteSpace(scriptRoot) ? Scope : ManagedScriptInstallScope.Custom,
                ShellEdition = ShellEdition,
                ScriptRoot = scriptRoot,
                Version = Version,
                Force = Force.IsPresent
            };

            if (Plan.IsPresent)
            {
                WriteObject(service.PlanUninstall(request));
                continue;
            }

            if (!ShouldProcess(scriptName, "Uninstall managed script"))
                continue;

            WriteObject(service.Uninstall(request));
        }
    }

    private static string? ResolveScriptRoot(PSCmdlet cmdlet, string? scriptRootOrPath)
    {
        var resolved = ManagedModuleCommandSupport.ResolveProviderPath(cmdlet, scriptRootOrPath);
        if (string.IsNullOrWhiteSpace(resolved))
            return resolved;

        if (File.Exists(resolved) ||
            string.Equals(Path.GetExtension(resolved), ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(resolved);
            return string.IsNullOrWhiteSpace(directory)
                ? resolved
                : directory;
        }

        return resolved;
    }
}
