using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;

namespace PSPublishModule;

/// <summary>
/// Gets installed PowerShell modules from managed module inventory.
/// </summary>
/// <remarks>
/// <para>
/// This command is the PowerShell-native inventory surface for managed module
/// maintenance. It returns installed module rows by default while reusing the
/// same inventory engine that powers the advanced ModuleState workflow.
/// </para>
/// </remarks>
/// <example>
/// <summary>Inventory installed modules</summary>
/// <code>Get-ManagedModule</code>
/// </example>
/// <example>
/// <summary>Inventory loaded and installed Graph modules</summary>
/// <code>Get-ManagedModule -Name Microsoft.Graph.* -IncludeLoaded -ShowSummary</code>
/// </example>
[Cmdlet(VerbsCommon.Get, "ManagedModule", DefaultParameterSetName = ParameterSetLocal)]
[OutputType(typeof(ModuleStateInstalledModuleResult))]
[OutputType(typeof(ModuleStateInventoryResult))]
[OutputType(typeof(string))]
public sealed class GetManagedModuleCommand : PSCmdlet
{
    private const string ParameterSetLocal = "Local";
    private const string ParameterSetPath = "Path";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Optional module name filters. Wildcards are supported.</summary>
    [Parameter(Position = 0, ParameterSetName = ParameterSetLocal)]
    [Parameter(Position = 0, ParameterSetName = ParameterSetPath)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Path to a module install root or previously written inventory JSON artifact.</summary>
    [Parameter(ParameterSetName = ParameterSetPath, Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Explicit module roots to scan. When omitted, PSModulePath is used.</summary>
    [Parameter(ParameterSetName = ParameterSetLocal)]
    [ValidateNotNullOrEmpty]
    public string[]? ModulePath { get; set; }

    /// <summary>Exact module version or explicit version range to return.</summary>
    [Parameter(ParameterSetName = ParameterSetLocal)]
    [Parameter(ParameterSetName = ParameterSetPath)]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Module installation scope to return.</summary>
    [Parameter(ParameterSetName = ParameterSetLocal)]
    [Parameter(ParameterSetName = ParameterSetPath)]
    [ValidateSet("CurrentUser", "AllUsers")]
    public string? Scope { get; set; }

    /// <summary>Include modules loaded in the current runspace as inventory evidence.</summary>
    [Parameter]
    public SwitchParameter IncludeLoaded { get; set; }

    /// <summary>Return the full inventory object instead of installed module rows.</summary>
    [Parameter]
    public SwitchParameter AsInventory { get; set; }

    /// <summary>Return JSON instead of typed objects.</summary>
    [Parameter]
    public SwitchParameter AsJson { get; set; }

    /// <summary>Write a compact Spectre.Console inventory summary.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Gets managed module inventory.</summary>
    protected override void ProcessRecord()
    {
        try
        {
            var inventory = ResolveInventory();
            if (ShowSummary.IsPresent)
                ModuleStateConsoleRenderer.WriteInventory(inventory);

            if (AsJson.IsPresent)
            {
                var json = AsInventory.IsPresent
                    ? JsonSerializer.Serialize(inventory, JsonOptions)
                    : JsonSerializer.Serialize(inventory.InstalledModules, JsonOptions);
                WriteObject(json, enumerateCollection: false);
                return;
            }

            if (AsInventory.IsPresent)
                WriteObject(inventory, enumerateCollection: false);
            else
                WriteObject(inventory.InstalledModules, enumerateCollection: true);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "GetManagedModuleFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private ModuleStateInventoryResult ResolveInventory()
    {
        var loadedModules = IncludeLoaded.IsPresent
            ? ModuleStateInventoryCommandSupport.GetLoadedModules(this)
            : null;

        if (string.Equals(ParameterSetName, ParameterSetPath, StringComparison.OrdinalIgnoreCase))
        {
            var resolvedPath = ResolveExistingPath(Path!, nameof(Path));
            if (File.Exists(resolvedPath))
                return ModuleStateInventoryCommandSupport.CreateInventoryResultFromFile(resolvedPath, loadedModules, Name, Version, Scope);

            return ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(new[] { resolvedPath }, loadedModules, Name, Version, Scope);
        }

        return ModuleStateInventoryCommandSupport.CreateInventoryResultFromModulePaths(
            ModulePath is { Length: > 0 }
                ? ModulePath
                : ModuleStateInventoryCommandSupport.ResolveEnvironmentModulePaths(),
            loadedModules,
            Name,
            Version,
            Scope);
    }

    private string ResolveExistingPath(string path, string parameterName)
    {
        var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
            throw new FileNotFoundException($"The {parameterName} path was not found.", resolved);

        return resolved;
    }
}
