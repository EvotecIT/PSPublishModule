using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Repairs installed PowerShell modules through the managed module-state engine.
/// </summary>
/// <remarks>
/// <para>
/// This command is the managed operator surface for module estate maintenance. It
/// inventories installed modules, plans stale-version, receipt-drift, source,
/// scope, family, and cleanup actions, and can apply the plan through the
/// managed delivery engine.
/// </para>
/// </remarks>
/// <example>
/// <summary>Preview latest-version repair for installed modules</summary>
/// <code>Repair-ManagedModule -Latest -Repository PSGallery -Plan -ShowSummary</code>
/// </example>
/// <example>
/// <summary>Preview version-coherence repair for Graph modules</summary>
/// <code>Repair-ManagedModule -Family Graph -Repository PSGallery -Plan -ShowSummary</code>
/// </example>
/// <example>
/// <summary>Preview baseline enforcement from a list of module names</summary>
/// <code>Repair-ManagedModule -Name Company.Tools,Company.Web -InstallMissing -Latest -Repository PSGallery -Plan -ShowSummary</code>
/// </example>
/// <example>
/// <summary>Preview baseline enforcement from a PSResourceGet-style resource file</summary>
/// <code>Repair-ManagedModule -RequiredResourceFile .\required-resources.psd1 -Latest -Repository PSGallery -Plan -ShowSummary</code>
/// </example>
/// <example>
/// <summary>Apply receipt-drift repair through a managed repository profile</summary>
/// <code>Repair-ManagedModule -MaintenanceReceiptPath .\module-maintenance.json -ProfileName CompanyModules -AcceptLicense</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Repair, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ModuleStateWorkflowResult))]
public sealed class RepairManagedModuleCommand : AsyncPSCmdlet
{
    /// <summary>Optional module names to repair. When omitted, all installed modules in scope are considered.</summary>
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Plan installs for literal names that are not present in the selected inventory.</summary>
    [Parameter]
    public SwitchParameter InstallMissing { get; set; }

    /// <summary>PSResourceGet-style required resource map used as desired module state.</summary>
    [Parameter]
    [ValidateNotNull]
    public object? RequiredResource { get; set; }

    /// <summary>Path to a PowerShell data file containing a PSResourceGet-style required resource map.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? RequiredResourceFile { get; set; }

    /// <summary>Existing inventory object. When omitted, local module paths are inventoried.</summary>
    [Parameter(ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModuleStateInventoryResult? Inventory { get; set; }

    /// <summary>Path to a previously written inventory JSON artifact.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? InventoryPath { get; set; }

    /// <summary>Explicit module roots to inventory. When omitted, PSModulePath is used.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? ModulePath { get; set; }

    /// <summary>Include modules loaded in the current runspace as inventory evidence.</summary>
    [Parameter]
    public SwitchParameter IncludeLoaded { get; set; }

    /// <summary>Optional module-state maintenance receipt artifacts used for drift checks.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? MaintenanceReceiptPath { get; set; }

    /// <summary>Plan latest-version repair/update delivery for selected installed modules.</summary>
    [Parameter]
    public SwitchParameter Latest { get; set; }

    /// <summary>Exact required version used when repairing named modules.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum version used when repairing named modules.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>NuGet-style version range policy used when repairing named modules.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? VersionPolicy { get; set; }

    /// <summary>Optional cleanup planning for managed modules.</summary>
    [Parameter]
    [ValidateSet("None", "OldVersions")]
    public string Cleanup { get; set; } = "None";

    /// <summary>Built-in module family policies to include in repair planning.</summary>
    [Parameter]
    [ValidateSet("MicrosoftGraph", "Graph", "Az", "ExchangeOnline", "Teams")]
    public string[]? Family { get; set; }

    /// <summary>Target installation scope used when selecting installed baseline modules.</summary>
    [Parameter]
    [ValidateSet("CurrentUser", "AllUsers")]
    public string? Scope { get; set; }

    /// <summary>Saved repository profile used by managed delivery.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

    /// <summary>Repository source or registered repository name used by managed delivery.</summary>
    [Parameter]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string? Repository { get; set; }

    /// <summary>Delivery transport used for install/update repair actions.</summary>
    [Parameter]
    public ModuleStateDeliveryTransport Transport { get; set; } = ModuleStateDeliveryTransport.ManagedModule;

    /// <summary>Custom module root for managed delivery.</summary>
    [Parameter]
    [Alias("Path")]
    [ValidateNotNullOrEmpty]
    public string? ModuleRoot { get; set; }

    /// <summary>Include prerelease versions during managed delivery.</summary>
    [Parameter]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Force reinstall when repair selects the same installed version.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Allow managed delivery to overwrite exported command conflicts.</summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>Accept package licenses when packages declare license acceptance is required.</summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

    /// <summary>Skip installing dependencies declared by repaired packages.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Allow apply preparation to continue when the plan contains error findings.</summary>
    [Parameter]
    public SwitchParameter AllowConflict { get; set; }

    /// <summary>Return the repair plan without applying install/update actions.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Write a compact Spectre.Console summary.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Optional repository credential.</summary>
    [Parameter]
    public PSCredential? Credential { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Repairs managed modules.</summary>
    protected override async Task ProcessRecordAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(Repository))
                throw new InvalidOperationException("Specify either ProfileName or Repository, not both.");
            if (!string.IsNullOrWhiteSpace(InventoryPath) && Inventory is not null)
                throw new InvalidOperationException("Specify either Inventory or InventoryPath, not both.");
            if (RequiredResource is not null && !string.IsNullOrWhiteSpace(RequiredResourceFile))
                throw new InvalidOperationException("Specify either RequiredResource or RequiredResourceFile, not both.");
            if (InstallMissing.IsPresent && Name.Length == 0 && RequiredResource is null && string.IsNullOrWhiteSpace(RequiredResourceFile))
                throw new InvalidOperationException("InstallMissing requires Name, RequiredResource, or RequiredResourceFile.");
            ValidateVersionPolicy();
            var deliveryModuleRoot = ResolveManagedDeliveryModuleRoot();
            var credentialSecretFilePath = ResolveCredentialSecretFilePath();
            var requiredResourceInputSupplied = RequiredResource is not null || !string.IsNullOrWhiteSpace(RequiredResourceFile);
            var requiredResourceTargets = ResolveRequiredResourceTargets().ToArray();

            var inventory = ResolveInventory();
            var selectedModules = SelectBaselineModules(inventory).ToArray();
            var plan = ModuleStatePlanCommandSupport.CreatePlanResult(
                inventory,
                CreateDesiredState(selectedModules, requiredResourceTargets, requiredResourceInputSupplied),
                ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath)),
                repair: true,
                ParseCleanupMode(Cleanup),
                Family);
            ApplySelectedInventoryTargets(plan, selectedModules);
            ApplyLatestUpdateIntent(plan);
            ApplyForceRepairIntent(plan, inheritGlobalForce: !requiredResourceInputSupplied);
            var managedDeliveryOptions = CreateManagedDeliveryOptions(
                inventory,
                deliveryModuleRoot,
                inheritResourceDefaults: !requiredResourceInputSupplied);
            var repositoriesResolved = PreResolveManagedDeliveryRepositories(
                plan,
                managedDeliveryOptions,
                required: !Plan.IsPresent);
            if (repositoriesResolved && ShouldResolveManagedCredential(plan))
                managedDeliveryOptions.Credential = ResolveRepositoryCredential(credentialSecretFilePath);
            if (repositoriesResolved)
                await EnrichManagedLicenseMetadataAsync(plan, managedDeliveryOptions).ConfigureAwait(false);

            var test = ModuleStateTestResult.FromPlan(plan);
            var applyResult = await PrepareApplyAsync(
                plan,
                inventory,
                deliveryModuleRoot,
                credentialSecretFilePath,
                managedDeliveryOptions,
                requiredResourceInputSupplied).ConfigureAwait(false);
            var workflow = new ModuleStateWorkflowResult
            {
                Inventory = inventory,
                Plan = plan,
                Test = test,
                Apply = applyResult
            };

            if (ShowSummary.IsPresent)
            {
                ModuleStateConsoleRenderer.WriteInventory(inventory);
                ModuleStateConsoleRenderer.WritePlan(plan);
                ModuleStateConsoleRenderer.WriteTest(test, includePlan: false);
                ModuleStateConsoleRenderer.WriteApply(applyResult);
            }

            WriteObject(workflow, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "RepairManagedModuleFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private ModuleStateInventoryResult ResolveInventory()
    {
        var loadedModules = IncludeLoaded.IsPresent
            ? ModuleStateInventoryCommandSupport.GetLoadedModules(this)
            : null;

        if (Inventory is not null)
            return ModuleStateInventoryCommandSupport.IncludeLoadedModules(Inventory, loadedModules);
        if (!string.IsNullOrWhiteSpace(InventoryPath))
            return ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(
                ResolveFilePath(InventoryPath!, nameof(InventoryPath)),
                loadedModules);

        return ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(
            ModulePath is { Length: > 0 }
                ? ModulePath
                : ModuleStateInventoryCommandSupport.ResolveEnvironmentModulePaths(),
            loadedModules);
    }

    private object CreateDesiredState(
        ModuleStateInstalledModuleResult[] selectedModules,
        ManagedModuleRequiredResourceTarget[] requiredResourceTargets,
        bool requiredResourceInputSupplied)
    {
        var desiredRepository = ResolveRepositoryName();
        var desiredRepositorySource = ResolveRepositorySource();
        var modules = new ArrayList();

        if (requiredResourceInputSupplied)
        {
            foreach (var target in FilterRequiredResourceTargets(requiredResourceTargets))
            {
                var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = target.Name,
                    ["VersionPolicy"] = ResolveVersionPolicy(target)
                };
                var targetRepository = string.IsNullOrWhiteSpace(target.Repository)
                    ? desiredRepository
                    : ResolveRepositoryName(target.Repository);
                var targetRepositorySource = string.IsNullOrWhiteSpace(target.Repository)
                    ? desiredRepositorySource
                    : ResolveRepositorySource(target.Repository);
                if (!string.IsNullOrWhiteSpace(targetRepository))
                    module["Repository"] = targetRepository!;
                if (!string.IsNullOrWhiteSpace(targetRepositorySource))
                    module["RepositorySource"] = targetRepositorySource!;
                if (target.ScopeSpecified || !string.IsNullOrWhiteSpace(Scope))
                    module["Scope"] = target.Scope.ToString();
                if (target.IncludePrerelease)
                    module["Prerelease"] = true;
                if (target.Reinstall)
                    module["Reinstall"] = true;
                if (target.AllowClobber)
                    module["AllowClobber"] = true;
                if (target.AcceptLicense)
                    module["AcceptLicense"] = true;
                if (target.SkipDependencyCheck)
                    module["SkipDependencyCheck"] = true;

                modules.Add(module);
            }

            return new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Modules"] = modules
            };
        }

        foreach (var selected in selectedModules)
        {
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = selected.Name,
                ["VersionPolicy"] = ResolveVersionPolicy(selected)
            };
            if (!string.IsNullOrWhiteSpace(desiredRepository))
                module["Repository"] = desiredRepository!;
            if (!string.IsNullOrWhiteSpace(desiredRepositorySource))
                module["RepositorySource"] = desiredRepositorySource!;
            if (!string.IsNullOrWhiteSpace(Scope))
                module["Scope"] = Scope!;
            else if (!string.IsNullOrWhiteSpace(selected.Scope))
                module["Scope"] = selected.Scope!;

            modules.Add(module);
        }

        foreach (var missingName in ResolveMissingRequestedNames(selectedModules))
        {
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = missingName,
                ["VersionPolicy"] = ResolveMissingVersionPolicy()
            };
            if (!string.IsNullOrWhiteSpace(desiredRepository))
                module["Repository"] = desiredRepository!;
            if (!string.IsNullOrWhiteSpace(desiredRepositorySource))
                module["RepositorySource"] = desiredRepositorySource!;
            if (!string.IsNullOrWhiteSpace(Scope))
                module["Scope"] = Scope!;
            if (Prerelease.IsPresent)
                module["Prerelease"] = true;
            if (Force.IsPresent)
                module["Reinstall"] = true;
            if (AllowClobber.IsPresent)
                module["AllowClobber"] = true;
            if (AcceptLicense.IsPresent)
                module["AcceptLicense"] = true;
            if (SkipDependencyCheck.IsPresent)
                module["SkipDependencyCheck"] = true;

            modules.Add(module);
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Modules"] = modules
        };
    }

    private IEnumerable<ManagedModuleRequiredResourceTarget> ResolveRequiredResourceTargets()
    {
        var resource = RequiredResource;
        if (resource is null && !string.IsNullOrWhiteSpace(RequiredResourceFile))
            resource = ManagedModuleRequiredResourceSupport.ImportRequiredResourceFile(this, RequiredResourceFile);
        if (resource is null)
            return Array.Empty<ManagedModuleRequiredResourceTarget>();

        var defaults = new ManagedModuleRequiredResourceDefaults(
            Prerelease.IsPresent,
            ParseInstallScope(Scope),
            Force.IsPresent,
            AllowClobber.IsPresent,
            AcceptLicense.IsPresent,
            SkipDependencyCheck.IsPresent);
        return ManagedModuleRequiredResourceSupport.Parse(resource, defaults);
    }

    private string[] ResolveMissingRequestedNames(IReadOnlyList<ModuleStateInstalledModuleResult> selectedModules)
    {
        if (!InstallMissing.IsPresent || Name.Length == 0)
            return Array.Empty<string>();

        var installed = selectedModules
            .Select(static module => module.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Where(static name => !ManagedModuleCommandSupport.HasWildcard(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !installed.Contains(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ApplySelectedInventoryTargets(
        ModuleStatePlanResult plan,
        IReadOnlyList<ModuleStateInstalledModuleResult> selectedModules)
    {
        if (!string.IsNullOrWhiteSpace(ModuleRoot) || ModulePath is { Length: 1 } || plan.Actions is null)
            return;

        var selectedByActionKey = selectedModules
            .GroupBy(static module => CreateSelectedActionKey(module.Name, module.Scope), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var selectedByName = selectedModules
            .GroupBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var action in plan.Actions)
        {
            if (!string.IsNullOrWhiteSpace(action.TargetPath))
                continue;

            var actionKey = CreateSelectedActionKey(action.ModuleName, action.TargetScope);
            if (!selectedByActionKey.TryGetValue(actionKey, out var selected))
            {
                if (!string.IsNullOrWhiteSpace(action.TargetScope) ||
                    !selectedByName.TryGetValue(action.ModuleName, out selected))
                    continue;
            }

            var moduleRoot = ResolveSelectedModuleRoot(selected);
            if (!string.IsNullOrWhiteSpace(moduleRoot))
                action.TargetPath = moduleRoot;
        }
    }

    private ManagedModuleRequiredResourceTarget[] FilterRequiredResourceTargets(
        ManagedModuleRequiredResourceTarget[] requiredResourceTargets)
    {
        var filters = CreateNameFilters();
        if (filters.Length == 0)
            return requiredResourceTargets;

        return requiredResourceTargets
            .Where(target => filters.Any(filter => filter.IsMatch(target.Name)))
            .ToArray();
    }

    private WildcardPattern[] CreateNameFilters()
        => Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => new WildcardPattern(name.Trim(), WildcardOptions.IgnoreCase))
            .ToArray();

    private static string CreateSelectedActionKey(string moduleName, string? scope)
        => string.Join("|", moduleName, scope ?? string.Empty);

    private static string? ResolveSelectedModuleRoot(ModuleStateInstalledModuleResult selected)
    {
        if (string.IsNullOrWhiteSpace(selected.Path))
            return null;

        var selectedDirectory = new DirectoryInfo(selected.Path!);
        if (string.Equals(selectedDirectory.Name, selected.Name, StringComparison.OrdinalIgnoreCase))
            return selectedDirectory.Parent?.FullName;

        var moduleDirectory = selectedDirectory.Parent;
        if (moduleDirectory is null)
            return null;

        return string.Equals(moduleDirectory.Name, selected.Name, StringComparison.OrdinalIgnoreCase)
            ? moduleDirectory.Parent?.FullName
            : null;
    }

    private ModuleStateInstalledModuleResult[] SelectBaselineModules(ModuleStateInventoryResult inventory)
    {
        var modules = inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>();
        var filters = CreateNameFilters();

        if (filters.Length > 0)
            modules = modules.Where(module => filters.Any(filter => filter.IsMatch(module.Name))).ToArray();
        if (!string.IsNullOrWhiteSpace(Scope))
            modules = modules.Where(module => string.Equals(module.Scope, Scope, StringComparison.OrdinalIgnoreCase)).ToArray();

        return modules
            .GroupBy(module => string.Join("|", module.Name, string.IsNullOrWhiteSpace(Scope) ? module.Scope ?? string.Empty : Scope), StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectInventoryModule(group, Scope))
            .Where(static module => module is not null)
            .Cast<ModuleStateInstalledModuleResult>()
            .OrderBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ModuleStateInstalledModuleResult? SelectInventoryModule(
        IEnumerable<ModuleStateInstalledModuleResult> modules,
        string? scope)
    {
        var candidates = modules
            .Where(module => string.IsNullOrWhiteSpace(scope) ||
                             string.Equals(module.Scope, scope, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return candidates
            .Where(static module => module.IsEffectiveImportCandidate)
            .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
            .FirstOrDefault()
            ?? candidates
                .OrderByDescending(static module => ModuleStateVersion.TryParse(module.Version, out var version) ? version : default)
                .FirstOrDefault();
    }

    private string ResolveVersionPolicy(ModuleStateInstalledModuleResult selected)
    {
        if (Latest.IsPresent)
            return "*";
        if (!string.IsNullOrWhiteSpace(Version))
            return "=" + Version!.Trim();
        if (!string.IsNullOrWhiteSpace(MinimumVersion))
            return ">=" + MinimumVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(VersionPolicy))
            return VersionPolicy!.Trim();

        return string.IsNullOrWhiteSpace(selected.Version)
            ? "*"
            : "=" + selected.Version.Trim();
    }

    private string ResolveMissingVersionPolicy()
    {
        if (Latest.IsPresent)
            return "*";
        if (!string.IsNullOrWhiteSpace(Version))
            return "=" + Version!.Trim();
        if (!string.IsNullOrWhiteSpace(MinimumVersion))
            return ">=" + MinimumVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(VersionPolicy))
            return VersionPolicy!.Trim();

        return "*";
    }

    private string ResolveVersionPolicy(ManagedModuleRequiredResourceTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.Version))
            return "=" + target.Version!.Trim();
        if (!string.IsNullOrWhiteSpace(target.VersionPolicy))
            return target.VersionPolicy!.Trim();
        if (!string.IsNullOrWhiteSpace(Version))
            return "=" + Version!.Trim();
        if (!string.IsNullOrWhiteSpace(MinimumVersion))
            return ">=" + MinimumVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(VersionPolicy))
            return VersionPolicy!.Trim();

        return "*";
    }

    private async Task<ModuleStateApplyResult> PrepareApplyAsync(
        ModuleStatePlanResult plan,
        ModuleStateInventoryResult inventory,
        string? deliveryModuleRoot,
        string? credentialSecretFilePath,
        ModuleStateManagedDeliveryOptions managedDeliveryOptions,
        bool requiredResourceInputSupplied)
    {
        var deliveryOptions = new ModuleStateDeliveryOptions(
            ProfileName,
            Repository,
            installPrerequisites: false,
            prerelease: !requiredResourceInputSupplied && Prerelease.IsPresent,
            force: !requiredResourceInputSupplied && Force.IsPresent,
            acceptLicense: !requiredResourceInputSupplied && AcceptLicense.IsPresent,
            allowErrorFindings: AllowConflict.IsPresent,
            allowClobber: !requiredResourceInputSupplied && AllowClobber.IsPresent,
            skipDependencyCheck: !requiredResourceInputSupplied && SkipDependencyCheck.IsPresent,
            moduleRoot: deliveryModuleRoot,
            transport: Transport,
            profileRepository: ResolveProfileRepositoryName());
        var service = new ModuleStateApplyService();
        var corePlan = ModuleStatePlanResultMapper.ToCorePlan(plan);
        var result = service.Prepare(corePlan, deliveryOptions);
        var executionResults = Array.Empty<ModuleStateDeliveryExecutionResult>();

        if (!Plan.IsPresent && result.Receipt.CanApply && ShouldProcess("managed module estate", "Repair managed modules"))
            executionResults = await ExecuteDeliveryAsync(
                result,
                inventory,
                deliveryModuleRoot,
                credentialSecretFilePath,
                managedDeliveryOptions,
                requiredResourceInputSupplied).ConfigureAwait(false);

        return ModuleStateApplyResultMapper.ToCmdletResult(
            result,
            receiptPath: null,
            maintenanceReceiptOutputPath: null,
            executionRequested: executionResults.Length > 0,
            executionResults,
            postApplyInventory: null);
    }

    private async Task<ModuleStateDeliveryExecutionResult[]> ExecuteDeliveryAsync(
        PowerForge.ModuleStateApplyResult result,
        ModuleStateInventoryResult inventory,
        string? deliveryModuleRoot,
        string? credentialSecretFilePath,
        ModuleStateManagedDeliveryOptions managedDeliveryOptions,
        bool requiredResourceInputSupplied)
    {
        if (Transport == ModuleStateDeliveryTransport.ManagedModule)
        {
            return await new ModuleStateManagedDeliveryService(this).ExecuteAsync(
                result,
                managedDeliveryOptions,
                CancelToken).ConfigureAwait(false);
        }

        return new ModuleStatePrivateDeliveryService(this).Execute(
            result,
            new ModuleStatePrivateDeliveryOptions
            {
                ProfileName = ProfileName,
                Repository = Repository,
                InstallPrerequisites = false,
                Prerelease = !requiredResourceInputSupplied && Prerelease.IsPresent,
                Force = !requiredResourceInputSupplied && Force.IsPresent,
                DeliveryTransport = Transport,
                CredentialUserName = Credential?.UserName ?? CredentialUserName,
                CredentialSecret = Credential?.GetNetworkCredential().Password ?? CredentialSecret,
                CredentialSecretFilePath = credentialSecretFilePath,
                PromptForCredential = false,
                ManagedModuleRoot = deliveryModuleRoot,
                ManagedAllowClobber = !requiredResourceInputSupplied && AllowClobber.IsPresent,
                ManagedAcceptLicense = !requiredResourceInputSupplied && AcceptLicense.IsPresent,
                ManagedSkipDependencyCheck = !requiredResourceInputSupplied && SkipDependencyCheck.IsPresent,
                LoadedModules = ResolveLoadedModules(inventory)
            });
    }

    private async Task EnrichManagedLicenseMetadataAsync(
        ModuleStatePlanResult plan,
        ModuleStateManagedDeliveryOptions managedDeliveryOptions)
    {
        if (Transport != ModuleStateDeliveryTransport.ManagedModule)
            return;

        try
        {
            await new ModuleStateManagedPlanLicenseEnricher(this)
                .EnrichAsync(plan, managedDeliveryOptions, CancelToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or UriFormatException)
        {
            WriteVerbose("Managed module license preflight skipped: " + ex.Message);
        }
    }

    private bool PreResolveManagedDeliveryRepositories(
        ModuleStatePlanResult plan,
        ModuleStateManagedDeliveryOptions options,
        bool required)
    {
        if (Transport != ModuleStateDeliveryTransport.ManagedModule)
            return true;

        var actions = (plan.Actions ?? Array.Empty<ModuleStatePlanActionResult>())
            .Where(static action => action.Kind is "Install" or "Update" or "Save")
            .ToArray();
        if (actions.Length == 0)
            return true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var action in actions)
            {
                var actionRepository = ResolveActionDeliveryRepository(action);
                var key = ModuleStateManagedRepositoryResolver.CreateRepositoryKey(actionRepository);
                if (!seen.Add(key))
                    continue;
                if (key.Length == 0 &&
                    string.IsNullOrWhiteSpace(options.Repository) &&
                    string.IsNullOrWhiteSpace(options.ProfileName))
                {
                    continue;
                }

                options.ResolvedRepositories[key] = ModuleStateManagedRepositoryResolver.ResolveRepositoryForAction(
                    this,
                    actionRepository,
                    options,
                    "Managed module delivery requires Repository, ProfileName, or action target repository.");
            }

            return true;
        }
        catch (Exception ex) when (!required &&
                                   (ex is InvalidOperationException or ArgumentException or NotSupportedException or UriFormatException))
        {
            WriteVerbose("Managed module license preflight skipped: " + ex.Message);
            return false;
        }
    }

    private ModuleStateManagedDeliveryOptions CreateManagedDeliveryOptions(
        ModuleStateInventoryResult? inventory = null,
        string? moduleRoot = null,
        RepositoryCredential? credential = null,
        bool inheritResourceDefaults = true)
        => new()
        {
            ProfileName = ProfileName,
            Repository = Repository,
            Prerelease = inheritResourceDefaults && Prerelease.IsPresent,
            Force = inheritResourceDefaults && Force.IsPresent,
            AllowClobber = inheritResourceDefaults && AllowClobber.IsPresent,
            AcceptLicense = inheritResourceDefaults && AcceptLicense.IsPresent,
            SkipDependencyCheck = inheritResourceDefaults && SkipDependencyCheck.IsPresent,
            ModuleRoot = moduleRoot,
            Credential = credential,
            LoadedModules = ResolveLoadedModules(inventory)
        };

    private RepositoryCredential? ResolveRepositoryCredential(string? credentialSecretFilePath)
        => ManagedModuleCommandSupport.ResolveCredential(
            this,
            Credential,
            CredentialUserName,
            CredentialSecret,
            credentialSecretFilePath);

    private bool ShouldResolveManagedCredential(ModuleStatePlanResult plan)
        => Transport == ModuleStateDeliveryTransport.ManagedModule &&
           HasDeliveryActions(plan);

    private static bool HasDeliveryActions(ModuleStatePlanResult plan)
        => (plan.Actions ?? Array.Empty<ModuleStatePlanActionResult>())
            .Any(static action => action.Kind is "Install" or "Update" or "Save");

    private string? ResolveCredentialSecretFilePath()
        => ManagedModuleCommandSupport.ResolveProviderPath(this, CredentialSecretFilePath);

    private string? ResolveManagedDeliveryModuleRoot()
    {
        if (!string.IsNullOrWhiteSpace(ModuleRoot))
            return ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);

        return ModulePath is { Length: 1 }
            ? ManagedModuleCommandSupport.ResolveProviderPath(this, ModulePath[0])
            : null;
    }

    private static ManagedModuleLoadedModule[] ResolveLoadedModules(ModuleStateInventoryResult? inventory)
        => (inventory?.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
            .Where(static module => module.IsLoaded)
            .Select(static module => new ManagedModuleLoadedModule
            {
                Name = module.Name,
                Version = module.Version,
                ModuleBase = module.Path
            })
            .ToArray();

    private void ValidateVersionPolicy()
    {
        if (Latest.IsPresent &&
            (!string.IsNullOrWhiteSpace(Version) ||
             !string.IsNullOrWhiteSpace(MinimumVersion) ||
             !string.IsNullOrWhiteSpace(VersionPolicy)))
        {
            throw new InvalidOperationException("Latest cannot be combined with Version, MinimumVersion, or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(Version) &&
            (!string.IsNullOrWhiteSpace(MinimumVersion) || !string.IsNullOrWhiteSpace(VersionPolicy)))
        {
            throw new InvalidOperationException("Version cannot be combined with MinimumVersion or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(MinimumVersion) && !string.IsNullOrWhiteSpace(VersionPolicy))
            throw new InvalidOperationException("MinimumVersion cannot be combined with VersionPolicy.");
    }

    private void ApplyLatestUpdateIntent(ModuleStatePlanResult plan)
    {
        if (!Latest.IsPresent || plan.Actions is null)
            return;

        foreach (var action in plan.Actions)
        {
            if (!string.Equals(action.Kind, ModuleStatePlanActionKind.NoAction.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsExactVersionPolicy(action.VersionPolicy))
                continue;

            action.Kind = ModuleStatePlanActionKind.Update.ToString();
            action.Reason = "Latest requested; update delivery will keep the module unchanged when the repository has no newer version.";
        }
    }

    private static bool IsExactVersionPolicy(string? versionPolicy)
        => !string.IsNullOrWhiteSpace(versionPolicy) &&
           versionPolicy!.Trim().StartsWith("=", StringComparison.Ordinal);

    private static string? ResolveActionDeliveryRepository(ModuleStatePlanActionResult action)
        => string.IsNullOrWhiteSpace(action.TargetRepositorySource)
            ? action.TargetRepository
            : action.TargetRepositorySource;

    private void ApplyForceRepairIntent(ModuleStatePlanResult plan, bool inheritGlobalForce = true)
    {
        if (!inheritGlobalForce || !Force.IsPresent || Latest.IsPresent || plan.Actions is null)
            return;
        if (plan.Actions.Any(static action => string.Equals(action.Kind, ModuleStatePlanActionKind.Remove.ToString(), StringComparison.OrdinalIgnoreCase)))
            return;

        foreach (var action in plan.Actions)
        {
            if (!string.Equals(action.Kind, ModuleStatePlanActionKind.NoAction.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            action.Kind = ModuleStatePlanActionKind.Update.ToString();
            action.IsRepair = true;
            action.Force = true;
            action.Reason = "Force requested; repair delivery will reinstall the selected module version.";
        }
    }

    private string ResolveFilePath(string path, string parameterName)
    {
        var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"The {parameterName} file was not found.", resolved);

        return resolved;
    }

    private string[] ResolveOptionalFilePaths(string[]? paths, string parameterName)
        => (paths ?? Array.Empty<string>())
            .Select(path => ResolveFilePath(path, parameterName))
            .ToArray();

    private string? ResolveRepositoryName()
    {
        if (!string.IsNullOrWhiteSpace(Repository))
            return ResolveRepositoryName(Repository);
        if (!string.IsNullOrWhiteSpace(ProfileName))
            return ModuleRepositoryProfileCommandSupport.TryResolve(ProfileName)?.RepositoryName;
        return null;
    }

    private string? ResolveRepositorySource()
        => ResolveRepositorySource(Repository);

    private string? ResolveRepositorySource(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        var trimmed = repository!.Trim();
        if (ModuleStateManagedRepositoryResolver.IsRepositorySource(trimmed))
            return trimmed;

        var providerPath = ManagedModuleCommandSupport.ResolveProviderPath(this, trimmed);
        return !string.IsNullOrWhiteSpace(providerPath) && Directory.Exists(providerPath)
            ? providerPath
            : null;
    }

    private string? ResolveRepositoryName(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        var source = ResolveRepositorySource(repository);
        return ModuleStateManagedRepositoryResolver.ResolveRepositoryIdentity(this, source ?? repository!);
    }

    private string? ResolveProfileRepositoryName()
        => string.IsNullOrWhiteSpace(ProfileName)
            ? null
            : ModuleRepositoryProfileCommandSupport.TryResolve(ProfileName)?.RepositoryName;

    private static ModuleStateCleanupMode ParseCleanupMode(string? cleanup)
        => string.Equals(cleanup, "OldVersions", StringComparison.OrdinalIgnoreCase)
            ? ModuleStateCleanupMode.OldVersions
            : ModuleStateCleanupMode.None;

    private static ManagedModuleInstallScope ParseInstallScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return ManagedModuleInstallScope.CurrentUser;
        if (Enum.TryParse<ManagedModuleInstallScope>(scope, ignoreCase: true, out var parsed))
            return parsed;

        throw new InvalidOperationException($"Unsupported scope '{scope}'.");
    }
}
