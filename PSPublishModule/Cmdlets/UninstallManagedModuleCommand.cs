using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Uninstalls installed PowerShell module versions through the managed module engine.
/// </summary>
/// <remarks>
/// <para>
/// This command removes modules from the selected managed module root without invoking PowerShellGet or
/// PSResourceGet. It follows PSResourceGet-shaped uninstall selection semantics while adding managed
/// dependency and loaded-module safety checks.
/// </para>
/// </remarks>
/// <example>
/// <summary>Uninstall the latest stable installed version of a module</summary>
/// <code>Uninstall-ManagedModule -Name Company.Tools</code>
/// </example>
/// <example>
/// <summary>Preview every installed module version that matches a version range</summary>
/// <code>Uninstall-ManagedModule -Name Company.Tools -Version '[1.0.0,2.0.0)' -Plan</code>
/// </example>
[Cmdlet(VerbsLifecycle.Uninstall, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModuleUninstallResult), typeof(ManagedModuleUninstallPlan))]
public sealed class UninstallManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names or wildcard patterns to uninstall.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Exact version or NuGet-style version range to uninstall. When omitted, the latest matching version is selected.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Restrict matching to prerelease module versions.</summary>
    [Parameter]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Install scope used when ModuleRoot is not supplied.</summary>
    [Parameter]
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers module roots.</summary>
    [Parameter]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit module root. Use with Scope Custom.</summary>
    [Parameter]
    [Alias("Path")]
    [ValidateNotNullOrEmpty]
    public string? ModuleRoot { get; set; }

    /// <summary>Skip checking whether removed modules are still required by other installed modules.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Loaded module evidence used to block risky in-session uninstalls.</summary>
    [Parameter]
    public ManagedModuleLoadedModule[] LoadedModule { get; set; } = Array.Empty<ManagedModuleLoadedModule>();

    /// <summary>Allow removal of module versions that appear loaded in the current PowerShell session.</summary>
    [Parameter]
    public SwitchParameter AllowLoadedModuleUninstall { get; set; }

    /// <summary>Return an inspectable uninstall plan without removing files.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Uninstalls requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);
        var service = new ManagedModuleUninstallService();
        var request = new ManagedModuleUninstallRequest
        {
            Name = Name,
            Version = Version,
            Prerelease = Prerelease.IsPresent,
            Scope = string.IsNullOrWhiteSpace(moduleRoot) ? Scope : ManagedModuleInstallScope.Custom,
            ShellEdition = ShellEdition,
            ModuleRoot = moduleRoot,
            SkipDependencyCheck = SkipDependencyCheck.IsPresent,
            AllowLoadedModuleUninstall = AllowLoadedModuleUninstall.IsPresent,
            LoadedModules = ResolveLoadedModules()
        };
        var plan = service.PlanUninstall(request);

        if (Plan.IsPresent)
        {
            WriteObject(plan, enumerateCollection: false);
            return;
        }

        if (plan.Targets.Count == 0)
        {
            WriteObject(service.Uninstall(plan), enumerateCollection: true);
            return;
        }

        var targets = plan.Targets
            .Where(target => ShouldProcess(
                target.ModulePath,
                $"Uninstall managed module '{target.Name}' version '{target.Version}'"))
            .ToArray();
        if (targets.Length == 0)
            return;

        var selectedPlan = new ManagedModuleUninstallPlan
        {
            Name = plan.Name,
            Version = plan.Version,
            ModuleRoot = plan.ModuleRoot,
            SkipDependencyCheck = plan.SkipDependencyCheck,
            Targets = targets,
            MissingNames = plan.MissingNames
        };
        WriteObject(service.Uninstall(selectedPlan), enumerateCollection: true);
    }

    private ManagedModuleLoadedModule[] ResolveLoadedModules()
    {
        var loaded = LoadedModule ?? Array.Empty<ManagedModuleLoadedModule>();
        var sessionLoaded = ModuleStateInventoryCommandSupport.GetLoadedModules(this)
            .Select(static module => new ManagedModuleLoadedModule
            {
                Name = module.Name ?? string.Empty,
                Version = module.Version,
                Path = module.Path
            });
        return loaded
            .Concat(sessionLoaded)
            .Where(static module => !string.IsNullOrWhiteSpace(module.Name))
            .ToArray();
    }
}
