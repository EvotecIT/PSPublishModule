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
/// destinations are reported and blocked.
/// </para>
/// <para>
/// Live apply performs delivery before exact-path old-version cleanup, revalidates loaded-module and dependency
/// safety, inventories the same roots again, and returns post-apply plan and convergence evidence. Operational
/// failures remain visible in the typed result and are also written as nonterminating errors.
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

    /// <summary>Existing inventory object. When omitted, local module paths are inventoried.</summary>
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

    /// <summary>Explicit user profile home directories whose standard Windows PowerShell and PowerShell 7 module roots are inventoried.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? UserProfilePath { get; set; }

    /// <summary>Discover standard PowerShell module roots below the current local profile container. Use UserProfilePath or ModulePath for redirected and custom layouts.</summary>
    [Parameter]
    public SwitchParameter IncludeAllUserProfiles { get; set; }

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

    /// <summary>Optional old-version cleanup. Live apply removes only exact planned paths after delivery and safety revalidation.</summary>
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

            var inventory = ResolveInventory();
            var selectedModules = SelectBaselineModules(inventory).ToArray();
            var desiredState = CreateDesiredState(selectedModules, requiredResourceTargets, requiredResourceInputSupplied);
            var maintenanceReceiptPaths = ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath));
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
