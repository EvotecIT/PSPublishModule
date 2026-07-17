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
/// Repairs and verifies installed PowerShell modules through the managed module-state engine.
/// </summary>
/// <remarks>
/// <para>
/// This command is the managed operator surface for module estate maintenance. It
/// inventories installed modules, plans stale-version, receipt-drift, source,
/// scope, family, and cleanup actions, and can apply the plan through the
/// managed delivery engine.
/// </para>
/// <para>
/// PSResourceGet has no equivalent estate-repair command. Use the lifecycle cmdlets for one requested operation and
/// this command when the desired outcome spans installed-state discovery, drift analysis, repair, and cleanup.
/// </para>
/// <para>
/// Repair keeps module copies in separate physical roots, PowerShell editions, scopes, and local user profiles
/// independent. Missing modules require an explicit ModuleRoot or exactly one eligible scanned root; ambiguous
/// destinations are reported and blocked. A single explicit UserProfilePath supplies the current PowerShell
/// edition's standard CurrentUser root even when that root does not exist yet; it never overrides an AllUsers
/// request. Explicit ModuleRoot and profile destinations are merged into supplied Inventory or InventoryPath
/// artifacts and remain part of convergence scans. Module roots declared by maintenance receipts are merged the
/// same way, including roots that do not exist until repair delivery creates them. A successfully enumerated
/// explicit root replaces stale artifact rows and diagnostics for that root, even when the live root is empty.
/// </para>
/// <para>
/// Live apply performs delivery, inventories the same estate again, replans exact-path old-version cleanup from
/// current state. Cleanup requires that refreshed plan to be error-free, preflights the complete exact-path removal
/// set, validates loaded-module and dependency safety across relevant global/profile roots, and removes selected
/// dependents before their selected dependencies. Current-runspace loaded modules are protected even when
/// IncludeLoaded is not used for inventory output. A declined delivery or cleanup action is reported as skipped and
/// cannot be reported as successful convergence. Repair performs a final live inventory and returns post-apply plan
/// and convergence evidence after execution or a no-action apply. Operational failures remain visible in the typed
/// result and are also written as nonterminating errors.
/// </para>
/// <para>
/// This is local-machine estate management suitable for workstations and servers, including service-account and
/// multi-profile roots. It does not connect to or orchestrate remote computers; invoke it in each target session or
/// through the operator's existing remoting/configuration system.
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
/// <example>
/// <summary>Preview repair across Windows PowerShell and PowerShell 7 roots</summary>
/// <code>Repair-ManagedModule -ModulePath $ps5Root,$ps7Root -Latest -Cleanup OldVersions -Repository PSGallery -Plan -ShowSummary</code>
/// </example>
/// <example>
/// <summary>Preview modules installed for multiple local user profiles</summary>
/// <code>Repair-ManagedModule -UserProfilePath C:\Users\Alice,C:\Users\Service.PowerShell -Name Company.* -Latest -Repository CompanyModules -Plan -ShowSummary</code>
/// </example>
/// <example>
/// <summary>Apply safe old-version cleanup and verify convergence</summary>
/// <code>Repair-ManagedModule -IncludeAllUserProfiles -Latest -Cleanup OldVersions -Repository CompanyModules -Confirm:$false -ShowSummary</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Repair, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ModuleStateWorkflowResult))]
public sealed partial class RepairManagedModuleCommand : AsyncPSCmdlet
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

    /// <summary>Existing inventory object. Explicit ModuleRoot and UserProfilePath destinations are merged into it and included in post-apply convergence scans.</summary>
    [Parameter(ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModuleStateInventoryResult? Inventory { get; set; }

    /// <summary>Path to a previously written inventory JSON artifact.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? InventoryPath { get; set; }

    /// <summary>Explicit required module roots to inventory. Missing or inaccessible roots block apply. When omitted, optional PSModulePath entries are used.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? ModulePath { get; set; }

    /// <summary>Explicit user profile home directories whose platform-standard module roots are inventoried. Existing roots are required and block repair when inaccessible; a missing current-edition root remains a creatable destination. One explicit profile provides that CurrentUser destination for missing modules, but never overrides AllUsers scope.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? UserProfilePath { get; set; }

    /// <summary>Discover existing standard PowerShell module roots below the local profile container. Unix root sessions scan /home and retain /root; inaccessible optional profiles or roots are reported as warnings. Use UserProfilePath or ModulePath for redirected and custom layouts.</summary>
    [Parameter]
    public SwitchParameter IncludeAllUserProfiles { get; set; }

    /// <summary>Include modules loaded in the current runspace as inventory and plan evidence. Cleanup protects current-runspace modules regardless of this reporting option.</summary>
    [Parameter]
    public SwitchParameter IncludeLoaded { get; set; }

    /// <summary>Optional module-state maintenance receipt artifacts used for drift checks. Receipt-declared module roots are inventoried and retained for post-apply convergence.</summary>
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

    /// <summary>Optional old-version cleanup. Live apply replans after delivery, requires an error-free refreshed estate, batch-preflights exact paths, and removes selected dependents before dependencies.</summary>
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

    /// <summary>Explicit physical module root used to narrow inventory selection and managed delivery. It also resolves missing-module destination ambiguity.</summary>
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

    /// <summary>Return the full repair and cleanup plan without applying any actions.</summary>
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
            var maintenanceReceiptPaths = ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath));

            var inventory = ResolveInventory(maintenanceReceiptPaths);
            var selectedModules = SelectBaselineModules(inventory).ToArray();
            var desiredState = CreateDesiredState(selectedModules, requiredResourceTargets, requiredResourceInputSupplied);
            var plan = ModuleStatePlanCommandSupport.CreatePlanResult(
                inventory,
                desiredState,
                maintenanceReceiptPaths,
                repair: true,
                ParseCleanupMode(Cleanup),
                Family);
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
                requiredResourceInputSupplied,
                desiredState,
                maintenanceReceiptPaths).ConfigureAwait(false);
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
            foreach (var failedExecution in applyResult.ExecutionResults.Where(static execution => !execution.Succeeded))
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(failedExecution.ErrorMessage ?? $"Module repair operation '{failedExecution.Operation}' failed."),
                    "RepairManagedModuleExecutionFailed",
                    ErrorCategory.InvalidOperation,
                    failedExecution.TargetPath));
            }
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "RepairManagedModuleFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }
}
