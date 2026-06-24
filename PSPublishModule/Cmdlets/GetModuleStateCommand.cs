using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;

namespace PSPublishModule;

/// <summary>
/// Gets module-state inventory from the local machine or an inventory artifact.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the inventory entry point for ModuleState. It scans module roots
/// from <c>-ModulePath</c> or <c>$env:PSModulePath</c>, or reads an existing
/// inventory artifact supplied through <c>-Path</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Inventory modules from PSModulePath</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleState</code>
/// <para>Scans module roots from <c>$env:PSModulePath</c> and returns installed module entries.</para>
/// </example>
/// <example>
/// <summary>Read inventory from an artifact</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleState -Path .\inventory.json</code>
/// <para>Reads a previously captured module-state inventory artifact.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ModuleState", DefaultParameterSetName = ParameterSetLocal)]
[OutputType(typeof(ModuleStateInventoryResult))]
[OutputType(typeof(string))]
public sealed class GetModuleStateCommand : PSCmdlet
{
    private const string ParameterSetLocal = "Local";
    private const string ParameterSetPath = "Path";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Gets or sets the path to an inventory JSON file.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetPath, Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets explicit module roots to scan. When omitted, <c>$env:PSModulePath</c> is used.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetLocal)]
    [ValidateNotNullOrEmpty]
    public string[]? ModulePath { get; set; }

    /// <summary>
    /// Gets or sets whether modules loaded in the current runspace should be marked in the inventory.
    /// </summary>
    [Parameter]
    public SwitchParameter IncludeLoaded { get; set; }

    /// <summary>
    /// Gets or sets the path where an inventory JSON artifact should be written.
    /// </summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Gets or sets whether to render a Spectre.Console summary in addition to returning objects.
    /// </summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>
    /// Gets or sets whether to return the inventory as JSON instead of a typed result object.
    /// </summary>
    [Parameter]
    public SwitchParameter AsJson { get; set; }

    /// <summary>
    /// Executes module-state inventory collection.
    /// </summary>
    protected override void ProcessRecord()
    {
        try
        {
            var loadedModules = IncludeLoaded.IsPresent
                ? ModuleStateInventoryCommandSupport.GetLoadedModules(this)
                : null;
            var result = string.Equals(ParameterSetName, ParameterSetPath, StringComparison.OrdinalIgnoreCase)
                ? ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(ResolveFilePath(Path!, nameof(Path)), loadedModules)
                : ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(
                    ModulePath is { Length: > 0 } ? ModulePath : ModuleStateInventoryCommandSupport.ResolveEnvironmentModulePaths(),
                    loadedModules);

            if (ShowSummary.IsPresent)
                ModuleStateConsoleRenderer.WriteInventory(result);

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
            WriteError(new ErrorRecord(ex, "GetModuleStateFailed", ErrorCategory.NotSpecified, null));
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

    private string ResolveOutputPath(string path)
        => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private void WriteJsonArtifact(ModuleStateInventoryResult result, string path)
    {
        var resolved = ResolveOutputPath(path);
        var directory = System.IO.Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resolved, JsonSerializer.Serialize(result, JsonOptions));
    }
}
