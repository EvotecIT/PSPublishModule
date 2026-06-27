using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds a module-state plan from module-state objects or artifacts.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the plan-only entry point for ModuleState. It does not install, update,
/// remove, or repair modules. Use the returned plan as support evidence or as input for a
/// later apply workflow.
/// </para>
/// </remarks>
/// <example>
/// <summary>Build a module-state plan from objects</summary>
/// <prefix>PS&gt; </prefix>
/// <code>$inventory = Get-ModuleState; $desired = @{ Modules = @(@{ Name = 'Company.Tools'; Version = '=1.2.0' }) }; $inventory | Get-ModuleStatePlan -DesiredState $desired</code>
/// <para>Uses normal PowerShell objects for inventory and desired state, then returns a typed plan object.</para>
/// </example>
/// <example>
/// <summary>Build a module-state plan from artifacts</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json</code>
/// <para>Reads inventory and desired-state artifacts, then returns the proposed actions and findings.</para>
/// </example>
/// <example>
/// <summary>Include maintenance receipt drift checks</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json</code>
/// <para>Returns findings when a previously maintained module is missing, has drifted version, source, or scope.</para>
/// </example>
/// <example>
/// <summary>Plan conservative receipt repair actions</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -MaintenanceReceiptPath .\module-state.maintenance.json -Repair</code>
/// <para>Returns install or update intents pinned to the receipt-managed version where the current machine drifted.</para>
/// </example>
/// <example>
/// <summary>Plan cleanup of old managed versions</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Cleanup OldVersions</code>
/// <para>Returns cleanup actions for old, unloaded versions of modules that ModuleState already manages.</para>
/// </example>
/// <example>
/// <summary>Add a built-in family coherence policy</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -Family MicrosoftGraph</code>
/// <para>Adds the built-in MicrosoftGraph family policy without creating a separate family cmdlet.</para>
/// </example>
/// <example>
/// <summary>Build a JSON plan artifact</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleStatePlan -InventoryPath .\inventory.json -DesiredStatePath .\powerforge.modules.json -AsJson</code>
/// <para>Returns the plan as JSON for CI logs or support bundles.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ModuleStatePlan")]
[OutputType(typeof(ModuleStatePlanResult))]
[OutputType(typeof(string))]
public sealed class GetModuleStatePlanCommand : PSCmdlet
{
    private const string ParameterSetFiles = "Files";
    private const string ParameterSetObjects = "Objects";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

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
    /// Gets or sets the path where a plan JSON artifact should be written.
    /// </summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Gets or sets whether to render a Spectre.Console summary in addition to returning objects.
    /// </summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>
    /// Gets or sets whether to return the plan as JSON instead of a typed result object.
    /// </summary>
    [Parameter]
    public SwitchParameter AsJson { get; set; }

    /// <summary>
    /// Executes the module-state plan workflow.
    /// </summary>
    protected override void ProcessRecord()
    {
        try
        {
            var maintenanceReceiptPaths = ResolveOptionalFilePaths(MaintenanceReceiptPath, nameof(MaintenanceReceiptPath));
            var result = string.Equals(ParameterSetName, ParameterSetObjects, StringComparison.OrdinalIgnoreCase)
                ? ModuleStatePlanCommandSupport.CreatePlanResult(
                    Inventory!,
                    DesiredState!,
                    maintenanceReceiptPaths,
                    Repair.IsPresent,
                    ParseCleanupMode(Cleanup),
                    Family)
                : ModuleStatePlanCommandSupport.CreatePlanResult(
                    ResolveFilePath(InventoryPath, nameof(InventoryPath)),
                    ResolveFilePath(DesiredStatePath, nameof(DesiredStatePath)),
                    maintenanceReceiptPaths,
                    Repair.IsPresent,
                    ParseCleanupMode(Cleanup),
                    Family);

            if (ShowSummary.IsPresent)
                ModuleStateConsoleRenderer.WritePlan(result);

            if (!string.IsNullOrWhiteSpace(OutputPath))
                WriteJsonArtifact(result, OutputPath!);

            if (AsJson.IsPresent)
            {
                WriteObject(JsonSerializer.Serialize(result, JsonOptions), enumerateCollection: false);
                return;
            }

            WriteObject(result, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "GetModuleStatePlanFailed", ErrorCategory.NotSpecified, null));
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

    private string ResolveOutputPath(string path)
        => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private void WriteJsonArtifact(ModuleStatePlanResult result, string path)
    {
        var resolved = ResolveOutputPath(path);
        var directory = System.IO.Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resolved, JsonSerializer.Serialize(result, JsonOptions));
    }
}
