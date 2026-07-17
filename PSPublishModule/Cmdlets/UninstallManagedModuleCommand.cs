using System;
using System.Collections.Generic;
using System.IO;
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
/// <example>
/// <summary>Uninstall the exact installed module returned by Get-ManagedModule</summary>
/// <code>Get-ManagedModule -Name Company.Tools -Version 1.2.0 | Uninstall-ManagedModule</code>
/// </example>
[Cmdlet(VerbsLifecycle.Uninstall, "ManagedModule", SupportsShouldProcess = true, DefaultParameterSetName = NameParameterSet)]
[OutputType(typeof(ManagedModuleUninstallResult), typeof(ManagedModuleUninstallPlan))]
public sealed class UninstallManagedModuleCommand : PSCmdlet
{
    private const string NameParameterSet = "NameParameterSet";
    private const string InputObjectParameterSet = "InputObjectParameterSet";
    private readonly List<ManagedModuleUninstallPlan> _plans = [];

    /// <summary>Module names or wildcard patterns to uninstall.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = NameParameterSet)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Installed module rows returned by Get-ManagedModule to uninstall.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = InputObjectParameterSet)]
    [ValidateNotNullOrEmpty]
    public ModuleStateInstalledModuleResult[] InputObject { get; set; } = Array.Empty<ModuleStateInstalledModuleResult>();

    /// <summary>Exact version or NuGet-style version range to uninstall. When omitted, the latest matching version is selected.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = NameParameterSet)]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Restrict matching to prerelease module versions.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Install scope used when ModuleRoot is not supplied.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers module roots.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit module root. Use with Scope Custom.</summary>
    [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = NameParameterSet)]
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
        if (ParameterSetName == InputObjectParameterSet)
        {
            foreach (var resource in InputObject)
            {
                if (resource is null ||
                    string.IsNullOrWhiteSpace(resource.Name) ||
                    string.IsNullOrWhiteSpace(resource.Version) ||
                    string.IsNullOrWhiteSpace(resource.InstalledLocation))
                {
                    throw new InvalidOperationException(
                        "Each InputObject entry must include Name, Version, and InstalledLocation from Get-ManagedModule.");
                }

                AddPlan(
                    new[] { resource.Name },
                    resource.Version,
                    resource.InstalledLocation,
                    ManagedModuleVersionComparer.IsPrerelease(resource.Version));
            }

            return;
        }

        AddPlan(Name, Version, ModuleRoot, Prerelease.IsPresent);
    }

    private void AddPlan(string[] names, string? version, string? modulePath, bool prerelease)
    {
        var moduleRoot = ResolveModuleRoot(modulePath, names);
        var service = new ManagedModuleUninstallService();
        var request = new ManagedModuleUninstallRequest
        {
            Name = names,
            Version = version,
            Prerelease = prerelease,
            Scope = string.IsNullOrWhiteSpace(moduleRoot) ? Scope : ManagedModuleInstallScope.Custom,
            ShellEdition = ShellEdition,
            ModuleRoot = moduleRoot,
            SkipDependencyCheck = SkipDependencyCheck.IsPresent,
            AllowLoadedModuleUninstall = AllowLoadedModuleUninstall.IsPresent,
            DeferLoadedModuleCheck = true,
            DeferDependencyCheck = true,
            LoadedModules = ResolveLoadedModules()
        };
        var plan = service.PlanUninstall(request);

        _plans.Add(plan);
    }

    /// <summary>Runs planned uninstall operations after all pipeline input has been collected.</summary>
    protected override void EndProcessing()
    {
        var service = new ManagedModuleUninstallService();
        foreach (var plan in MergePlans(_plans))
        {
            if (Plan.IsPresent)
            {
                WriteObject(plan, enumerateCollection: false);
                continue;
            }

            if (plan.Targets.Count == 0)
            {
                WriteObject(service.Uninstall(plan), enumerateCollection: true);
                continue;
            }

            var targets = plan.Targets
                .Where(target => ShouldProcess(
                    target.ModulePath,
                    $"Uninstall managed module '{target.Name}' version '{target.Version}'"))
                .ToArray();
            if (targets.Length == 0)
                continue;

            var selectedPlan = new ManagedModuleUninstallPlan
            {
                Name = plan.Name,
                Version = plan.Version,
                ModuleRoot = plan.ModuleRoot,
                SkipDependencyCheck = plan.SkipDependencyCheck,
                AllowLoadedModuleUninstall = plan.AllowLoadedModuleUninstall,
                Targets = targets,
                MissingNames = plan.MissingNames
            };
            WriteObject(service.Uninstall(selectedPlan), enumerateCollection: true);
        }
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

    private string? ResolveModuleRoot(string? modulePath, string[] names)
    {
        var resolved = ManagedModuleCommandSupport.ResolveProviderPath(this, modulePath);
        if (string.IsNullOrWhiteSpace(resolved) ||
            names.Length != 1 ||
            string.IsNullOrWhiteSpace(names[0]))
        {
            return resolved;
        }

        return TryResolveModuleRootFromInstalledPath(resolved!, names[0]) ?? resolved;
    }

    private static string? TryResolveModuleRootFromInstalledPath(string path, string moduleName)
    {
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;

        if (!Directory.Exists(path))
            return null;

        var selectedDirectory = new DirectoryInfo(path);
        if (string.Equals(selectedDirectory.Name, moduleName, StringComparison.OrdinalIgnoreCase) &&
            HasInstalledModulePayload(selectedDirectory.FullName, moduleName))
        {
            return selectedDirectory.Parent?.FullName;
        }

        var moduleDirectory = selectedDirectory.Parent;
        if (moduleDirectory is null ||
            !string.Equals(moduleDirectory.Name, moduleName, StringComparison.OrdinalIgnoreCase) ||
            !HasInstalledModulePayload(selectedDirectory.FullName, moduleName))
        {
            return null;
        }

        return moduleDirectory.Parent?.FullName;
    }

    private static bool HasInstalledModuleManifest(string modulePath, string moduleName)
        => File.Exists(Path.Combine(modulePath, moduleName + ".psd1")) ||
           Directory.GetFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly).Length > 0;

    private static bool HasInstalledModulePayload(string modulePath, string moduleName)
        => HasInstalledModuleManifest(modulePath, moduleName) ||
           File.Exists(Path.Combine(modulePath, moduleName + ".psm1")) ||
           File.Exists(Path.Combine(modulePath, moduleName + ".dll"));

    private static StringComparison PathStringComparison
        => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathStringComparer
        => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IReadOnlyList<ManagedModuleUninstallPlan> MergePlans(IReadOnlyList<ManagedModuleUninstallPlan> plans)
    {
        if (plans.Count < 2)
            return plans;

        return plans
            .GroupBy(
                static plan => new PlanMergeKey(plan.ModuleRoot, plan.SkipDependencyCheck, plan.AllowLoadedModuleUninstall),
                PlanMergeKey.Comparer)
            .Select(static group => group.Count() == 1 ? group.Single() : MergePlanGroup(group))
            .ToArray();
    }

    private static ManagedModuleUninstallPlan MergePlanGroup(IEnumerable<ManagedModuleUninstallPlan> plans)
    {
        var group = plans.ToArray();
        var versions = group
            .Select(static plan => plan.Version)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ManagedModuleUninstallPlan
        {
            Name = group.SelectMany(static plan => plan.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Version = versions.Length == 1 ? versions[0] : null,
            ModuleRoot = group[0].ModuleRoot,
            SkipDependencyCheck = group[0].SkipDependencyCheck,
            AllowLoadedModuleUninstall = group[0].AllowLoadedModuleUninstall,
            Targets = group
                .SelectMany(static plan => plan.Targets)
                .GroupBy(static target => target.ModulePath, PathStringComparer)
                .Select(static targetGroup => targetGroup.First())
                .ToArray(),
            MissingNames = group.SelectMany(static plan => plan.MissingNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private readonly struct PlanMergeKey : IEquatable<PlanMergeKey>
    {
        public PlanMergeKey(string moduleRoot, bool skipDependencyCheck, bool allowLoadedModuleUninstall)
        {
            ModuleRoot = NormalizePath(moduleRoot);
            SkipDependencyCheck = skipDependencyCheck;
            AllowLoadedModuleUninstall = allowLoadedModuleUninstall;
        }

        private string ModuleRoot { get; }

        private bool SkipDependencyCheck { get; }

        private bool AllowLoadedModuleUninstall { get; }

        public static IEqualityComparer<PlanMergeKey> Comparer { get; } = new PlanMergeKeyComparer();

        public bool Equals(PlanMergeKey other)
            => string.Equals(ModuleRoot, other.ModuleRoot, PathStringComparison) &&
               SkipDependencyCheck == other.SkipDependencyCheck &&
               AllowLoadedModuleUninstall == other.AllowLoadedModuleUninstall;

        public override bool Equals(object? obj)
            => obj is PlanMergeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = PathStringComparer.GetHashCode(ModuleRoot);
                hash = (hash * 397) ^ SkipDependencyCheck.GetHashCode();
                hash = (hash * 397) ^ AllowLoadedModuleUninstall.GetHashCode();
                return hash;
            }
        }

        private sealed class PlanMergeKeyComparer : IEqualityComparer<PlanMergeKey>
        {
            public bool Equals(PlanMergeKey x, PlanMergeKey y)
                => x.Equals(y);

            public int GetHashCode(PlanMergeKey obj)
                => obj.GetHashCode();
        }
    }
}
