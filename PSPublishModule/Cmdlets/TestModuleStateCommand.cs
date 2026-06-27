using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tests module state against a desired state or an existing plan.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet evaluates the same plan produced by <c>Get-ModuleStatePlan</c> and returns
/// whether the current inventory is compliant. It does not mutate the machine.
/// </para>
/// </remarks>
/// <example>
/// <summary>Test module state from objects</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleState | Test-ModuleState -DesiredState @{ Modules = @('Company.Tools') }</code>
/// <para>Tests the current inventory using normal PowerShell objects.</para>
/// </example>
/// <example>
/// <summary>Test an existing plan object</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json | Test-ModuleState</code>
/// <para>Tests compliance from a plan already produced by <c>Get-ModuleStatePlan</c>.</para>
/// </example>
/// <example>
/// <summary>Test module state from JSON files</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json</code>
/// <para>Returns <c>$true</c> when no changes or error findings are required.</para>
/// </example>
/// <example>
/// <summary>Fail when a maintenance receipt has drifted</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -FailOnConflict</code>
/// <para>Throws when receipt-backed module state no longer matches the current inventory.</para>
/// </example>
/// <example>
/// <summary>Test a built-in module family policy</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Family MicrosoftGraph -PassThru</code>
/// <para>Includes built-in family conflict findings in the compliance result.</para>
/// </example>
/// <example>
/// <summary>Test cleanup compliance</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Cleanup OldVersions -PassThru</code>
/// <para>Returns non-compliant when old managed versions would require cleanup actions.</para>
/// </example>
/// <example>
/// <summary>Return the detailed test result</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Test-ModuleState -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -PassThru</code>
/// <para>Returns compliance, required-action counts, error counts, and the underlying plan.</para>
/// </example>
[Cmdlet(VerbsDiagnostic.Test, "ModuleState")]
[OutputType(typeof(bool))]
[OutputType(typeof(ModuleStateTestResult))]
public sealed class TestModuleStateCommand : PSCmdlet
{
    private const string ParameterSetFiles = "Files";
    private const string ParameterSetObjects = "Objects";
    private const string ParameterSetPlan = "Plan";

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
    /// Gets or sets an existing module-state plan object to test.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetPlan, Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModuleStatePlanResult? Plan { get; set; }

    /// <summary>
    /// Gets or sets optional module-state maintenance receipt artifacts used for drift checks.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    [ValidateNotNullOrEmpty]
    public string[]? MaintenanceReceiptPath { get; set; }

    /// <summary>
    /// Gets or sets built-in module family policies to include in validation.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    [ValidateSet("MicrosoftGraph", "Graph", "Az", "ExchangeOnline", "Teams")]
    public string[]? Family { get; set; }

    /// <summary>
    /// Gets or sets optional cleanup planning for validation.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetFiles)]
    [Parameter(ParameterSetName = ParameterSetObjects)]
    [ValidateSet("None", "OldVersions")]
    public string Cleanup { get; set; } = "None";

    /// <summary>
    /// Gets or sets whether to return the detailed test result instead of a Boolean.
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Gets or sets whether to render a Spectre.Console summary in addition to returning objects.
    /// </summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>
    /// Gets or sets whether non-compliant module state should fail with a terminating error.
    /// </summary>
    [Parameter]
    public SwitchParameter FailOnConflict { get; set; }

    /// <summary>
    /// Executes module-state validation.
    /// </summary>
    protected override void ProcessRecord()
    {
        try
        {
            var maintenanceReceiptPaths = ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath));
            var plan = ResolvePlan(maintenanceReceiptPaths);
            var result = ModuleStateTestResult.FromPlan(plan);

            if (ShowSummary.IsPresent)
                ModuleStateConsoleRenderer.WriteTest(result);

            if (FailOnConflict.IsPresent && !result.IsCompliant)
            {
                throw new InvalidOperationException(
                    $"Module state is not compliant. Required actions: {result.RequiredActionCount}; error findings: {result.ErrorCount}.");
            }

            WriteObject(PassThru.IsPresent ? result : result.IsCompliant, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "TestModuleStateFailed", ErrorCategory.NotSpecified, null));
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

    private static ModuleStateCleanupMode ParseCleanupMode(string? cleanup)
        => string.Equals(cleanup, "OldVersions", StringComparison.OrdinalIgnoreCase)
            ? ModuleStateCleanupMode.OldVersions
            : ModuleStateCleanupMode.None;

    private ModuleStatePlanResult ResolvePlan(string[] maintenanceReceiptPaths)
    {
        if (string.Equals(ParameterSetName, ParameterSetPlan, StringComparison.OrdinalIgnoreCase))
            return Plan!;

        if (string.Equals(ParameterSetName, ParameterSetObjects, StringComparison.OrdinalIgnoreCase))
        {
            return ModuleStatePlanCommandSupport.CreatePlanResult(
                Inventory!,
                DesiredState!,
                maintenanceReceiptPaths,
                cleanupMode: ParseCleanupMode(Cleanup),
                families: Family);
        }

        return ModuleStatePlanCommandSupport.CreatePlanResult(
            ResolveFilePath(InventoryPath, nameof(InventoryPath)),
            ResolveFilePath(DesiredStatePath, nameof(DesiredStatePath)),
            maintenanceReceiptPaths,
            cleanupMode: ParseCleanupMode(Cleanup),
            families: Family);
    }
}
