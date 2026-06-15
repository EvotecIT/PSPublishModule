using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports embedded or installed module dependencies by exact manifest/path.
/// </summary>
/// <remarks>
/// <para>
/// Use <c>-Name</c> to import dependencies directly from a module's bundled Internals\Modules payload.
/// Use <c>-Path</c> after <c>Install-ModuleDependency</c> to import from a private dependency folder without
/// relying on PSModulePath discovery.
/// </para>
/// </remarks>
/// <example>
/// <summary>Import dependencies bundled inside a module</summary>
/// <code>Import-ModuleDependency -Name EntraIDConfig</code>
/// </example>
/// <example>
/// <summary>Import dependencies from an explicit private folder</summary>
/// <code>Import-ModuleDependency -Path C:\PrivateDeps -DependencyName Microsoft.Graph.Authentication</code>
/// </example>
[Cmdlet(VerbsData.Import, "ModuleDependency", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
public sealed class ImportModuleDependencyCommand : PSCmdlet
{
    /// <summary>Module containing embedded dependencies.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ByName", ValueFromPipelineByPropertyName = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Specific module object containing embedded dependencies.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ByModule", ValueFromPipeline = true)]
    public PSObject Module { get; set; } = default!;

    /// <summary>Installed dependency root or module-dependencies.json receipt path.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ByPath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional exact source module version when using -Name or -Module.</summary>
    [Parameter(ParameterSetName = "ByName")]
    [Parameter(ParameterSetName = "ByModule")]
    public Version? RequiredVersion { get; set; }

    /// <summary>Dependency names to import. When omitted, imports all dependencies in the receipt.</summary>
    [Parameter]
    public string[]? DependencyName { get; set; }

    /// <summary>Force re-import of modules already loaded in the session.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Return imported module information from Import-Module -PassThru.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Imports selected dependency modules by exact path.</summary>
    protected override void ProcessRecord()
    {
        var manifestPath = ResolveDependencyManifestPath();
        var manifest = EmbeddedModuleDependencyService.ReadManifest(manifestPath);
        var entries = EmbeddedModuleDependencyService.FilterEntries(manifest, DependencyName);

        foreach (var entry in entries)
        {
            var modulePath = EmbeddedModuleDependencyService.ResolveEntryPath(manifestPath, entry);
            var importPath = EmbeddedModuleDependencyService.ResolveModuleImportPath(modulePath, entry.Name);

            if (!ShouldProcess(entry.Name, $"Import module dependency from '{importPath}'"))
                continue;

            var script = InvokeCommand.NewScriptBlock(
                "param($p, [bool]$force, [bool]$passThru) " +
                "if ($passThru) { Import-Module -LiteralPath $p -Force:$force -PassThru } " +
                "else { Import-Module -LiteralPath $p -Force:$force }");
            var output = script.Invoke(importPath, Force.IsPresent, PassThru.IsPresent);
            if (PassThru)
            {
                foreach (var item in output)
                    WriteObject(item);
            }
        }
    }

    private string ResolveDependencyManifestPath()
    {
        if (string.Equals(ParameterSetName, "ByPath", StringComparison.OrdinalIgnoreCase))
            return EmbeddedModuleDependencyService.ResolveManifestPath(SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path));

        var resolver = new ModuleResolver(this);
        var module = Module is not null
            ? resolver.Resolve(string.Empty, PSObject.AsPSObject(Module), RequiredVersion)
            : resolver.Resolve(Name, null!, RequiredVersion);

        var moduleName = ModuleDependencyCommandHelpers.GetProperty(module, "Name") ??
                         ModuleDependencyCommandHelpers.GetProperty(module, "ModuleName") ??
                         Name;
        var moduleBase = ModuleDependencyCommandHelpers.GetProperty(module, "ModuleBase");
        if (string.IsNullOrWhiteSpace(moduleBase) || !Directory.Exists(moduleBase))
            throw new DirectoryNotFoundException($"Module base path not found for '{moduleName}'.");

        return ModuleDependencyCommandHelpers.ResolveEmbeddedManifestPath(this, moduleBase!);
    }
}
