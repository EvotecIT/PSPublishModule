using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
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
/// <summary>Apply receipt-drift repair through a managed repository profile</summary>
/// <code>Repair-ManagedModule -MaintenanceReceiptPath .\module-maintenance.json -ProfileName CompanyModules -AcceptLicense</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Repair, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ModuleStateWorkflowResult))]
public sealed class RepairManagedModuleCommand : PSCmdlet
{
    /// <summary>Optional module names to repair. When omitted, all installed modules in scope are considered.</summary>
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

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
    protected override void ProcessRecord()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(Repository))
                throw new InvalidOperationException("Specify either ProfileName or Repository, not both.");
            if (!string.IsNullOrWhiteSpace(InventoryPath) && Inventory is not null)
                throw new InvalidOperationException("Specify either Inventory or InventoryPath, not both.");
            ValidateVersionPolicy();

            var inventory = ResolveInventory();
            var plan = ModuleStatePlanCommandSupport.CreatePlanResult(
                inventory,
                CreateDesiredState(inventory),
                ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath)),
                repair: true,
                ParseCleanupMode(Cleanup),
                Family);
            ApplyLatestUpdateIntent(plan);
            EnrichManagedLicenseMetadata(plan);

            var test = ModuleStateTestResult.FromPlan(plan);
            var applyResult = PrepareApply(plan, inventory);
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

    private object CreateDesiredState(ModuleStateInventoryResult inventory)
    {
        var selectedModules = SelectBaselineModules(inventory).ToArray();
        var desiredRepository = ResolveRepositoryName();
        var modules = new ArrayList();

        foreach (var selected in selectedModules)
        {
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = selected.Name,
                ["VersionPolicy"] = ResolveVersionPolicy(selected)
            };
            if (!string.IsNullOrWhiteSpace(desiredRepository))
                module["Repository"] = desiredRepository!;
            if (!string.IsNullOrWhiteSpace(Scope))
                module["Scope"] = Scope!;
            else if (!string.IsNullOrWhiteSpace(selected.Scope))
                module["Scope"] = selected.Scope!;

            modules.Add(module);
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Modules"] = modules
        };
    }

    private ModuleStateInstalledModuleResult[] SelectBaselineModules(ModuleStateInventoryResult inventory)
    {
        var modules = inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>();
        var filters = Name
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => new WildcardPattern(name.Trim(), WildcardOptions.IgnoreCase))
            .ToArray();

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

    private ModuleStateApplyResult PrepareApply(ModuleStatePlanResult plan, ModuleStateInventoryResult inventory)
    {
        var deliveryOptions = new ModuleStateDeliveryOptions(
            ProfileName,
            Repository,
            installPrerequisites: false,
            prerelease: Prerelease.IsPresent,
            force: Force.IsPresent,
            acceptLicense: AcceptLicense.IsPresent,
            allowErrorFindings: AllowConflict.IsPresent,
            allowClobber: AllowClobber.IsPresent,
            moduleRoot: ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot),
            transport: Transport);
        var service = new ModuleStateApplyService();
        var corePlan = ModuleStatePlanResultMapper.ToCorePlan(plan);
        var result = service.Prepare(corePlan, deliveryOptions);
        var executionResults = Array.Empty<ModuleStateDeliveryExecutionResult>();

        if (!Plan.IsPresent && result.Receipt.CanApply && ShouldProcess("managed module estate", "Repair managed modules"))
            executionResults = ExecuteDelivery(result, inventory);

        return ModuleStateApplyResultMapper.ToCmdletResult(
            result,
            receiptPath: null,
            maintenanceReceiptOutputPath: null,
            executionRequested: executionResults.Length > 0,
            executionResults,
            postApplyInventory: null);
    }

    private ModuleStateDeliveryExecutionResult[] ExecuteDelivery(PowerForge.ModuleStateApplyResult result, ModuleStateInventoryResult inventory)
    {
        if (Transport == ModuleStateDeliveryTransport.ManagedModule)
        {
            return new ModuleStateManagedDeliveryService(this).Execute(
                result,
                CreateManagedDeliveryOptions(inventory));
        }

        return new ModuleStatePrivateDeliveryService(this).Execute(
            result,
            new ModuleStatePrivateDeliveryOptions
            {
                ProfileName = ProfileName,
                Repository = Repository,
                InstallPrerequisites = false,
                Prerelease = Prerelease.IsPresent,
                Force = Force.IsPresent,
                DeliveryTransport = Transport,
                CredentialUserName = CredentialUserName,
                CredentialSecret = CredentialSecret,
                CredentialSecretFilePath = CredentialSecretFilePath,
                PromptForCredential = false
            });
    }

    private void EnrichManagedLicenseMetadata(ModuleStatePlanResult plan)
    {
        if (Transport != ModuleStateDeliveryTransport.ManagedModule)
            return;

        try
        {
            new ModuleStateManagedPlanLicenseEnricher(this).Enrich(plan, CreateManagedDeliveryOptions());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or UriFormatException)
        {
            WriteVerbose("Managed module license preflight skipped: " + ex.Message);
        }
    }

    private ModuleStateManagedDeliveryOptions CreateManagedDeliveryOptions(ModuleStateInventoryResult? inventory = null)
        => new()
        {
            ProfileName = ProfileName,
            Repository = Repository,
            Prerelease = Prerelease.IsPresent,
            Force = Force.IsPresent,
            AllowClobber = AllowClobber.IsPresent,
            AcceptLicense = AcceptLicense.IsPresent,
            ModuleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot),
            Credential = ManagedModuleCommandSupport.ResolveCredential(
                this,
                Credential,
                CredentialUserName,
                CredentialSecret,
                CredentialSecretFilePath),
            LoadedModules = ResolveLoadedModules(inventory)
        };

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

            action.Kind = ModuleStatePlanActionKind.Update.ToString();
            action.Reason = "Latest requested; update delivery will keep the module unchanged when the repository has no newer version.";
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
            return Repository;
        if (!string.IsNullOrWhiteSpace(ProfileName))
            return new ModuleRepositoryProfileStore().GetProfile(ProfileName!)?.RepositoryName;
        return null;
    }

    private static ModuleStateCleanupMode ParseCleanupMode(string? cleanup)
        => string.Equals(cleanup, "OldVersions", StringComparison.OrdinalIgnoreCase)
            ? ModuleStateCleanupMode.OldVersions
            : ModuleStateCleanupMode.None;
}
