using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Prepares private-module delivery commands and an optional receipt from a module-state plan.
/// </summary>
/// <remarks>
/// <para>
/// By default, this cmdlet prepares command intents and receipts only. When <c>-Execute</c> is supplied, it
/// runs grouped install and update actions through the same private-module workflow used by
/// <c>Install-PrivateModule</c> and <c>Update-PrivateModule</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Prepare private-module delivery from objects</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleState | Get-ModuleStatePlan -DesiredState @{ Modules = @('Company.Tools') } | Invoke-ModuleStatePlan -Repository Company</code>
/// <para>Uses typed PowerShell objects through the full inventory, plan, and apply-preparation flow.</para>
/// </example>
/// <example>
/// <summary>Prepare private-module delivery commands</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -ProfileName Company</code>
/// <para>Returns the private-module commands needed to reconcile the plan.</para>
/// </example>
/// <example>
/// <summary>Prepare private-module delivery from an approved plan artifact</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -PlanPath .\module-state.plan.json -Repository Company</code>
/// <para>Reads a plan previously written by <c>Get-ModuleStatePlan -AsJson</c> and prepares delivery commands from that approved artifact.</para>
/// </example>
/// <example>
/// <summary>Preview execution through the private-module workflow</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Repository Company -Execute -WhatIf</code>
/// <para>Shows the grouped private-module workflow operations that would reconcile the plan.</para>
/// </example>
/// <example>
/// <summary>Block apply when a maintenance receipt has drifted</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -ProfileName Company</code>
/// <para>Includes receipt drift findings in the plan before private-module delivery is prepared.</para>
/// </example>
/// <example>
/// <summary>Prepare repair actions from maintenance receipts</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -Repair -Repository Company</code>
/// <para>Prepares private-module delivery command intents for receipt-managed modules that drifted.</para>
/// </example>
/// <example>
/// <summary>Plan cleanup of old managed versions without executing removals</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Cleanup OldVersions -ProfileName Company</code>
/// <para>Includes cleanup actions in the returned plan, but does not convert them to private-module delivery commands.</para>
/// </example>
/// <example>
/// <summary>Add a built-in family coherence policy</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Family MicrosoftGraph -Repository Company</code>
/// <para>Includes built-in family findings before preparing private-module delivery commands.</para>
/// </example>
/// <example>
/// <summary>Write a drift-checkable maintenance receipt</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Repository Company -MaintenanceReceiptOutputPath .\module-state.maintenance.json</code>
/// <para>Writes a maintenance receipt for modules whose maintained version is known from exact policy or satisfied inventory.</para>
/// </example>
/// <example>
/// <summary>Write a module-state receipt</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -ProfileName Company -ReceiptPath .\module-state.receipt.json</code>
/// <para>Writes a JSON receipt describing the prepared delivery commands and any requested execution evidence.</para>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "ModuleStatePlan", SupportsShouldProcess = true, DefaultParameterSetName = ParameterSetFiles)]
[OutputType(typeof(ModuleStateApplyResult))]
[OutputType(typeof(string))]
public sealed class InvokeModuleStatePlanCommand : PSCmdlet
{
    private const string ParameterSetFiles = "Files";
    private const string ParameterSetObjects = "Objects";
    private const string ParameterSetPlanPath = "PlanPath";
    private const string ParameterSetPlanObject = "PlanObject";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Gets or sets the path to a module-state plan artifact.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetPlanPath, Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? PlanPath { get; set; }

    /// <summary>
    /// Gets or sets an existing module-state plan object.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetPlanObject, Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModuleStatePlanResult? Plan { get; set; }

    /// <summary>
    /// Gets or sets the path to the module-state inventory artifact.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles, Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string InventoryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the module-state desired-state artifact.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles, Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string DesiredStatePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the in-memory module-state inventory object.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetObjects, Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModuleStateInventoryResult? Inventory { get; set; }

    /// <summary>
    /// Gets or sets the desired module state as a hashtable, PSCustomObject, or array of module objects.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetObjects, Mandatory = true, Position = 1)]
    [ValidateNotNull]
    public object? DesiredState { get; set; }

    /// <summary>
    /// Gets or sets optional module-state maintenance receipt artifacts used for drift checks.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    [ValidateNotNullOrEmpty]
    public string[]? MaintenanceReceiptPath { get; set; }

    /// <summary>
    /// Gets or sets whether receipt drift should produce conservative repair actions.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    public SwitchParameter Repair { get; set; }

    /// <summary>
    /// Gets or sets optional cleanup planning for managed modules.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    [ValidateSet("None", "OldVersions")]
    public string Cleanup { get; set; } = "None";

    /// <summary>
    /// Gets or sets built-in module family policies to include in the plan.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    [ValidateSet("MicrosoftGraph", "Graph", "Az", "ExchangeOnline", "Teams")]
    public string[]? Family { get; set; }

    /// <summary>
    /// Gets or sets the private module repository profile used by prepared delivery commands.
    /// </summary>
    [Parameter]
    public string? ProfileName { get; set; }

    /// <summary>
    /// Gets or sets the registered private module repository used by prepared delivery commands.
    /// </summary>
    [Parameter]
    public string? Repository { get; set; }

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
    /// Gets or sets the delivery transport used for prepared and executed install/update actions.
    /// </summary>
    [Parameter]
    public ModuleStateDeliveryTransport Transport { get; set; } = ModuleStateDeliveryTransport.PrivateModule;

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
    /// Gets or sets whether managed module delivery may overwrite exported command conflicts.
    /// </summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>
    /// Gets or sets whether managed module delivery accepts package licenses.
    /// </summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

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
    /// Gets or sets whether to render a Spectre.Console summary in addition to returning objects.
    /// </summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>
    /// Gets or sets whether to return the result as JSON instead of a typed object.
    /// </summary>
    [Parameter]
    public SwitchParameter AsJson { get; set; }

    /// <summary>
    /// Executes module-state apply preparation and optional private-module delivery.
    /// </summary>
    protected override void ProcessRecord()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(Repository))
                throw new InvalidOperationException("Specify either ProfileName or Repository, not both.");

            var deliveryOptions = new ModuleStateDeliveryOptions(
                ProfileName,
                Repository,
                InstallPrerequisites.IsPresent,
                Prerelease.IsPresent,
                Force.IsPresent,
                AllowConflict.IsPresent,
                Transport);
            var plan = ResolvePlan();
            var service = new ModuleStateApplyService();
            var result = service.Prepare(plan, deliveryOptions);
            var receiptPath = ResolveOptionalReceiptPath(ReceiptPath);
            var maintenanceReceiptOutputPath = ResolveOptionalReceiptPath(MaintenanceReceiptOutputPath);
            ModuleStateDeliveryExecutionResult[] executionResults = [];
            ModuleStateInventoryResult? postApplyInventory = null;

            if (Execute.IsPresent)
            {
                if (!result.Receipt.CanApply)
                    throw new InvalidOperationException(result.Receipt.BlockedReason ?? "ModuleState plan cannot be applied.");

                executionResults = ExecuteDelivery(result);
            }

            if (PostApplyModulePath is { Length: > 0 })
            {
                postApplyInventory = ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(PostApplyModulePath);
            }

            var cmdletResult = ModuleStateApplyResultMapper.ToCmdletResult(
                result,
                receiptPath,
                maintenanceReceiptOutputPath,
                Execute.IsPresent,
                executionResults,
                postApplyInventory);

            if (ShowSummary.IsPresent)
                ModuleStateConsoleRenderer.WriteApply(cmdletResult);

            if (receiptPath is not null &&
                ShouldProcess(receiptPath, "Write ModuleState receipt"))
            {
                WriteReceipt(cmdletResult, receiptPath);
            }
            if (maintenanceReceiptOutputPath is not null &&
                ShouldProcess(maintenanceReceiptOutputPath, "Write ModuleState maintenance receipt"))
            {
                if (HasFailedExecutionResult(executionResults))
                    throw new InvalidOperationException("ModuleState maintenance receipt cannot be written because one or more private-module delivery operations failed.");
                if (HasSkippedExecutionResult(executionResults))
                    throw new InvalidOperationException("ModuleState maintenance receipt cannot be written because one or more private-module delivery operations were skipped.");

                var observedModules = ModuleStateMaintenanceEvidenceMapper.ToObservedModules(
                    executionResults,
                    postApplyInventory,
                    ResolveMaintenanceReceiptSourceRepository());
                service.WriteMaintenanceReceipt(
                    result,
                    maintenanceReceiptOutputPath,
                    source: "ModuleStatePlan",
                    sourceRepository: ResolveMaintenanceReceiptSourceRepository(),
                    observedModules: observedModules);
            }
            if (AsJson.IsPresent)
            {
                WriteObject(JsonSerializer.Serialize(cmdletResult, JsonOptions), enumerateCollection: false);
                return;
            }

            WriteObject(cmdletResult, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeModuleStatePlanFailed", ErrorCategory.NotSpecified, null));
            throw;
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

    private string? ResolveOptionalReceiptPath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private static ModuleStateCleanupMode ParseCleanupMode(string? cleanup)
        => string.Equals(cleanup, "OldVersions", StringComparison.OrdinalIgnoreCase)
            ? ModuleStateCleanupMode.OldVersions
            : ModuleStateCleanupMode.None;

    private PowerForge.ModuleStatePlan ResolvePlan()
    {
        if (string.Equals(ParameterSetName, ParameterSetPlanPath, StringComparison.OrdinalIgnoreCase))
            return LoadPlanArtifact(ResolveFilePath(PlanPath!, nameof(PlanPath)));

        if (string.Equals(ParameterSetName, ParameterSetPlanObject, StringComparison.OrdinalIgnoreCase))
            return ModuleStatePlanResultMapper.ToCorePlan(Plan!);

        if (string.Equals(ParameterSetName, ParameterSetObjects, StringComparison.OrdinalIgnoreCase))
        {
            return ModuleStatePlanCommandSupport.CreatePlan(
                Inventory!,
                DesiredState!,
                ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath)),
                Repair.IsPresent,
                ParseCleanupMode(Cleanup),
                Family);
        }

        return ModuleStatePlanCommandSupport.CreatePlan(
            ResolveFilePath(InventoryPath, nameof(InventoryPath)),
            ResolveFilePath(DesiredStatePath, nameof(DesiredStatePath)),
            ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath)),
            Repair.IsPresent,
            ParseCleanupMode(Cleanup),
            Family);
    }

    private static PowerForge.ModuleStatePlan LoadPlanArtifact(string path)
    {
        var result = JsonSerializer.Deserialize<ModuleStatePlanResult>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("ModuleState plan artifact is empty.");

        return ModuleStatePlanResultMapper.ToCorePlan(result);
    }

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

    private ModuleStateDeliveryExecutionResult[] ExecuteDelivery(PowerForge.ModuleStateApplyResult result)
    {
        if (Transport == ModuleStateDeliveryTransport.ManagedModule)
        {
            return new ModuleStateManagedDeliveryService(this).Execute(
                result,
                new ModuleStateManagedDeliveryOptions
                {
                    ProfileName = ProfileName,
                    Repository = Repository,
                    Prerelease = Prerelease.IsPresent,
                    Force = Force.IsPresent,
                    AllowClobber = AllowClobber.IsPresent,
                    AcceptLicense = AcceptLicense.IsPresent,
                    Credential = ManagedModuleCommandSupport.ResolveCredential(
                        this,
                        null,
                        CredentialUserName,
                        CredentialSecret,
                        CredentialSecretFilePath)
                });
        }

        return new ModuleStatePrivateDeliveryService(this).Execute(
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

    private static void WriteReceipt(ModuleStateApplyResult result, string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    private static bool HasFailedExecutionResult(ModuleStateDeliveryExecutionResult[] executionResults)
        => executionResults.Any(static result =>
            (result.DependencyResults ?? Array.Empty<ModuleStateDependencyResult>())
            .Any(static dependency => string.Equals(dependency.Status, "Failed", StringComparison.OrdinalIgnoreCase)));

    private static bool HasSkippedExecutionResult(ModuleStateDeliveryExecutionResult[] executionResults)
        => executionResults.Any(static result => !result.OperationPerformed);
}
