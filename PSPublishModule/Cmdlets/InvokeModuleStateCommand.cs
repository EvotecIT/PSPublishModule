using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Runs the one-stop module-state management workflow.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet inventories modules, creates a plan, evaluates compliance, prepares
/// private-module delivery, and optionally executes the install/update workflow.
/// It is the operator-friendly entry point; the lower-level ModuleState cmdlets
/// remain available when inventory, plan, test, and apply need to be inspected
/// independently.
/// </para>
/// </remarks>
/// <example>
/// <summary>Preview management for one module</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleState -ModuleName Company.Tools -RequiredVersion 1.2.0 -Repository CompanyModules -Scope CurrentUser -ShowSummary</code>
/// <para>Inventories the current machine, plans the exact module version, and returns a workflow result without mutating the machine.</para>
/// </example>
/// <example>
/// <summary>Apply a desired-state object</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleState -DesiredState $desired -Repository CompanyModules -Repair -Execute -ShowSummary</code>
/// <para>Runs inventory, repair planning, and private-module delivery in one command.</para>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "ModuleState", SupportsShouldProcess = true, DefaultParameterSetName = ParameterSetModules)]
[OutputType(typeof(ModuleStateWorkflowResult))]
[OutputType(typeof(string))]
public sealed class InvokeModuleStateCommand : PSCmdlet
{
    private const string ParameterSetModules = "Modules";
    private const string ParameterSetDesiredState = "DesiredState";
    private const string ParameterSetInstalled = "Installed";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Gets or sets module names for the convenience desired-state shape.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModules, Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string[] ModuleName { get; set; } = [];

    /// <summary>
    /// Gets or sets whether all currently installed modules should be maintained.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetInstalled, Mandatory = true)]
    public SwitchParameter Installed { get; set; }

    /// <summary>
    /// Gets or sets whether module names should be checked for the latest repository version.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModules)]
    [Parameter(ParameterSetName = ParameterSetInstalled)]
    public SwitchParameter Latest { get; set; }

    /// <summary>
    /// Gets or sets an optional exact required version used with <c>-ModuleName</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModules)]
    public string? RequiredVersion { get; set; }

    /// <summary>
    /// Gets or sets an optional minimum version used with <c>-ModuleName</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModules)]
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Gets or sets an optional version policy used with <c>-ModuleName</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModules)]
    public string? VersionPolicy { get; set; }

    /// <summary>
    /// Gets or sets a desired module state as a hashtable, PSCustomObject, or array of module objects.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetDesiredState, Mandatory = true, Position = 0)]
    [ValidateNotNull]
    public object? DesiredState { get; set; }

    /// <summary>
    /// Gets or sets an existing inventory object. When omitted, local module paths are inventoried.
    /// </summary>
    [Parameter(ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModuleStateInventoryResult? Inventory { get; set; }

    /// <summary>
    /// Gets or sets the path to an inventory artifact. When omitted, local module paths are inventoried.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? InventoryPath { get; set; }

    /// <summary>
    /// Gets or sets explicit module roots to inventory. When omitted, <c>$env:PSModulePath</c> is used.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? ModulePath { get; set; }

    /// <summary>
    /// Gets or sets whether modules loaded in the current runspace should be marked in the inventory.
    /// </summary>
    [Parameter]
    public SwitchParameter IncludeLoaded { get; set; }

    /// <summary>
    /// Gets or sets optional module-state maintenance receipt artifacts used for drift checks.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? MaintenanceReceiptPath { get; set; }

    /// <summary>
    /// Gets or sets whether receipt drift should produce conservative repair actions.
    /// </summary>
    [Parameter]
    public SwitchParameter Repair { get; set; }

    /// <summary>
    /// Gets or sets optional cleanup planning for managed modules.
    /// </summary>
    [Parameter]
    [ValidateSet("None", "OldVersions")]
    public string Cleanup { get; set; } = "None";

    /// <summary>
    /// Gets or sets built-in module family policies to include in the plan.
    /// </summary>
    [Parameter]
    [ValidateSet("MicrosoftGraph", "Graph", "Az", "ExchangeOnline", "Teams")]
    public string[]? Family { get; set; }

    /// <summary>
    /// Gets or sets the target installation scope for the convenience desired-state shape.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModules)]
    [Parameter(ParameterSetName = ParameterSetInstalled)]
    [ValidateSet("CurrentUser", "AllUsers")]
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the private module repository profile used by delivery.
    /// </summary>
    [Parameter]
    public string? ProfileName { get; set; }

    /// <summary>
    /// Gets or sets the registered private module repository used by delivery.
    /// </summary>
    [Parameter]
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the path where the captured inventory artifact should be written.
    /// </summary>
    [Parameter]
    public string? InventoryOutputPath { get; set; }

    /// <summary>
    /// Gets or sets the path where the generated plan artifact should be written.
    /// </summary>
    [Parameter]
    public string? PlanOutputPath { get; set; }

    /// <summary>
    /// Gets or sets the path where a module-state receipt should be written.
    /// </summary>
    [Parameter]
    public string? ReceiptPath { get; set; }

    /// <summary>
    /// Gets or sets the path where a drift-checkable module-state maintenance receipt should be written.
    /// </summary>
    [Parameter]
    public string? MaintenanceReceiptOutputPath { get; set; }

    /// <summary>
    /// Gets or sets whether prepared private-module commands include InstallPrerequisites.
    /// </summary>
    [Parameter]
    public SwitchParameter InstallPrerequisites { get; set; }

    /// <summary>
    /// Gets or sets whether prepared private-module commands include Prerelease.
    /// </summary>
    [Parameter]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>
    /// Gets or sets whether prepared install commands include Force.
    /// </summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// Gets or sets whether the prepared private-module workflow should be executed.
    /// </summary>
    [Parameter]
    public SwitchParameter Execute { get; set; }

    /// <summary>
    /// Gets or sets module roots to inventory after execution.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? PostApplyModulePath { get; set; }

    /// <summary>
    /// Gets or sets an optional repository credential username for repository delivery.
    /// </summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>
    /// Gets or sets an optional repository credential secret for repository delivery.
    /// </summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>
    /// Gets or sets an optional path to a file containing the repository credential secret.
    /// </summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>
    /// Gets or sets whether to prompt for repository credentials.
    /// </summary>
    [Parameter]
    [Alias("Interactive")]
    public SwitchParameter PromptForCredential { get; set; }

    /// <summary>
    /// Gets or sets whether apply preparation should continue when the plan contains error findings.
    /// </summary>
    [Parameter]
    public SwitchParameter AllowConflict { get; set; }

    /// <summary>
    /// Gets or sets whether to render Spectre.Console summaries in addition to returning objects.
    /// </summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>
    /// Gets or sets whether to return the workflow result as JSON instead of a typed object.
    /// </summary>
    [Parameter]
    public SwitchParameter AsJson { get; set; }

    /// <summary>
    /// Runs the module-state management workflow.
    /// </summary>
    protected override void ProcessRecord()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(Repository))
                throw new InvalidOperationException("Specify either ProfileName or Repository, not both.");
            if (!string.IsNullOrWhiteSpace(InventoryPath) && Inventory is not null)
                throw new InvalidOperationException("Specify either Inventory or InventoryPath, not both.");

            var inventory = ResolveInventory();
            var maintenanceReceiptPaths = ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath));
            var plan = ModuleStatePlanCommandSupport.CreatePlanResult(
                inventory,
                ResolveDesiredState(inventory),
                maintenanceReceiptPaths,
                Repair.IsPresent,
                ParseCleanupMode(Cleanup),
                Family);
            ApplyLatestUpdateIntent(plan);
            var test = ModuleStateTestResult.FromPlan(plan);
            var applyResult = PrepareApply(plan);

            var workflow = new ModuleStateWorkflowResult
            {
                Inventory = inventory,
                Plan = plan,
                Test = test,
                Apply = applyResult
            };

            if (!string.IsNullOrWhiteSpace(InventoryOutputPath))
                WriteJsonArtifact(inventory, InventoryOutputPath!);
            if (!string.IsNullOrWhiteSpace(PlanOutputPath))
                WriteJsonArtifact(plan, PlanOutputPath!);

            if (ShowSummary.IsPresent)
            {
                ModuleStateConsoleRenderer.WriteInventory(inventory);
                ModuleStateConsoleRenderer.WritePlan(plan);
                ModuleStateConsoleRenderer.WriteTest(test, includePlan: false);
                ModuleStateConsoleRenderer.WriteApply(applyResult);
            }

            if (AsJson.IsPresent)
            {
                WriteObject(JsonSerializer.Serialize(workflow, JsonOptions), enumerateCollection: false);
                return;
            }

            WriteObject(workflow, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeModuleStateFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private ModuleStateInventoryResult ResolveInventory()
    {
        var loadedModules = IncludeLoaded.IsPresent
            ? ModuleStateInventoryCommandSupport.GetLoadedModules(this)
            : null;

        if (Inventory is not null)
            return Inventory;
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

    private object ResolveDesiredState(ModuleStateInventoryResult inventory)
    {
        if (string.Equals(ParameterSetName, ParameterSetDesiredState, StringComparison.OrdinalIgnoreCase))
            return DesiredState!;

        if (string.Equals(ParameterSetName, ParameterSetInstalled, StringComparison.OrdinalIgnoreCase))
            return CreateDesiredStateForInstalledModules(inventory);

        if (Latest.IsPresent &&
            (!string.IsNullOrWhiteSpace(RequiredVersion) ||
             !string.IsNullOrWhiteSpace(MinimumVersion) ||
             !string.IsNullOrWhiteSpace(VersionPolicy)))
        {
            throw new InvalidOperationException("Latest cannot be combined with RequiredVersion, MinimumVersion, or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(RequiredVersion) &&
            (!string.IsNullOrWhiteSpace(MinimumVersion) || !string.IsNullOrWhiteSpace(VersionPolicy)))
        {
            throw new InvalidOperationException("RequiredVersion cannot be combined with MinimumVersion or VersionPolicy.");
        }

        if (!string.IsNullOrWhiteSpace(MinimumVersion) && !string.IsNullOrWhiteSpace(VersionPolicy))
            throw new InvalidOperationException("MinimumVersion cannot be combined with VersionPolicy.");

        var explicitPolicy = ResolveExplicitVersionPolicy();
        var modules = new ArrayList();
        foreach (var name in ModuleName)
        {
            var selected = SelectInventoryModule(inventory, name, Scope);
            var policy = Latest.IsPresent ? "*" : explicitPolicy;
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = name,
                ["VersionPolicy"] = policy
            };
            if (!string.IsNullOrWhiteSpace(Repository))
                module["Repository"] = Repository!;
            if (!string.IsNullOrWhiteSpace(Scope))
                module["Scope"] = Scope!;

            modules.Add(module);
        }

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Modules"] = modules
        };
    }

    private object CreateDesiredStateForInstalledModules(ModuleStateInventoryResult inventory)
    {
        var modules = new ArrayList();
        foreach (var selected in (inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
            .GroupBy(static module => module.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => SelectInventoryModule(group, Scope))
            .Where(static module => module is not null)
            .Cast<ModuleStateInstalledModuleResult>()
            .OrderBy(static module => module.Name, StringComparer.OrdinalIgnoreCase))
        {
            var module = new Hashtable(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = selected.Name,
                ["VersionPolicy"] = "*"
            };
            if (!string.IsNullOrWhiteSpace(Repository))
                module["Repository"] = Repository!;
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

    private string ResolveExplicitVersionPolicy()
        => !string.IsNullOrWhiteSpace(RequiredVersion)
            ? "=" + RequiredVersion!.Trim()
            : !string.IsNullOrWhiteSpace(MinimumVersion)
                ? ">=" + MinimumVersion!.Trim()
                : string.IsNullOrWhiteSpace(VersionPolicy)
                    ? "*"
                    : VersionPolicy!.Trim();

    private static ModuleStateInstalledModuleResult? SelectInventoryModule(
        ModuleStateInventoryResult inventory,
        string moduleName,
        string? scope)
        => SelectInventoryModule(
            (inventory.InstalledModules ?? Array.Empty<ModuleStateInstalledModuleResult>())
                .Where(module => string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase)),
            scope);

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

    private ModuleStateApplyResult PrepareApply(ModuleStatePlanResult plan)
    {
        var deliveryOptions = new ModuleStateDeliveryOptions(
            ProfileName,
            Repository,
            InstallPrerequisites.IsPresent,
            Prerelease.IsPresent,
            Force.IsPresent,
            AllowConflict.IsPresent);
        var service = new ModuleStateApplyService();
        var corePlan = ModuleStatePlanResultMapper.ToCorePlan(plan);
        var result = service.Prepare(corePlan, deliveryOptions);
        var receiptPath = ResolveOptionalOutputPath(ReceiptPath);
        var maintenanceReceiptOutputPath = ResolveOptionalOutputPath(MaintenanceReceiptOutputPath);
        ModuleStateDeliveryExecutionResult[] executionResults = [];
        ModuleStateInventoryResult? postApplyInventory = null;

        if (Execute.IsPresent)
        {
            if (!result.Receipt.CanApply)
                throw new InvalidOperationException(result.Receipt.BlockedReason ?? "ModuleState plan cannot be applied.");

            executionResults = new ModuleStatePrivateDeliveryService(this).Execute(
                result,
                new ModuleStatePrivateDeliveryOptions
                {
                    ProfileName = ProfileName,
                    Repository = Repository,
                    InstallPrerequisites = InstallPrerequisites.IsPresent,
                    Prerelease = Prerelease.IsPresent,
                    Force = Force.IsPresent,
                    CredentialUserName = CredentialUserName,
                    CredentialSecret = CredentialSecret,
                    CredentialSecretFilePath = CredentialSecretFilePath,
                    PromptForCredential = PromptForCredential.IsPresent
                });
        }

        if (PostApplyModulePath is { Length: > 0 })
            postApplyInventory = ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(PostApplyModulePath);

        var cmdletResult = ModuleStateApplyResultMapper.ToCmdletResult(
            result,
            receiptPath,
            maintenanceReceiptOutputPath,
            Execute.IsPresent,
            executionResults,
            postApplyInventory);

        if (receiptPath is not null &&
            ShouldProcess(receiptPath, "Write ModuleState receipt"))
        {
            WriteJsonArtifact(cmdletResult, receiptPath);
        }

        if (maintenanceReceiptOutputPath is not null &&
            ShouldProcess(maintenanceReceiptOutputPath, "Write ModuleState maintenance receipt"))
        {
            var observedModules = ModuleStateMaintenanceEvidenceMapper.ToObservedModules(
                executionResults,
                postApplyInventory,
                ResolveMaintenanceReceiptSourceRepository());
            service.WriteMaintenanceReceipt(
                result,
                maintenanceReceiptOutputPath,
                source: "ModuleState",
                sourceRepository: ResolveMaintenanceReceiptSourceRepository(),
                observedModules: observedModules);
        }

        return cmdletResult;
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

    private string? ResolveOptionalOutputPath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private static ModuleStateCleanupMode ParseCleanupMode(string? cleanup)
        => string.Equals(cleanup, "OldVersions", StringComparison.OrdinalIgnoreCase)
            ? ModuleStateCleanupMode.OldVersions
            : ModuleStateCleanupMode.None;

    private string? ResolveMaintenanceReceiptSourceRepository()
    {
        if (!string.IsNullOrWhiteSpace(Repository))
            return Repository;
        if (string.IsNullOrWhiteSpace(ProfileName))
            return null;

        var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(ProfileName!);
        return string.IsNullOrWhiteSpace(profile.RepositoryName)
            ? ProfileName
            : profile.RepositoryName;
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

    private void WriteJsonArtifact<T>(T result, string path)
    {
        var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        var directory = System.IO.Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resolved, JsonSerializer.Serialize(result, JsonOptions));
    }
}
