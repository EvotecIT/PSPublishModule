using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports a module runtime by exact paths, with dependencies loaded before the root module.
/// </summary>
/// <remarks>
/// <para>
/// Use <c>-Name</c> to import dependencies directly from a module's bundled Internals\Modules payload and
/// then import that module by exact path. Add <c>-Path</c> after <c>Install-ModuleDependency</c> to import
/// the private runtime copy without relying on PSModulePath discovery.
/// </para>
/// </remarks>
/// <example>
/// <summary>Import dependencies bundled inside a module, then import the module</summary>
/// <code>Import-ModuleDependency -Name EntraIDConfig</code>
/// </example>
/// <example>
/// <summary>Import a private runtime folder created by Install-ModuleDependency</summary>
/// <code>Import-ModuleDependency -Name EntraIDConfig -Path C:\PrivateDeps</code>
/// </example>
[Cmdlet(VerbsData.Import, "ModuleDependency", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
public sealed class ImportModuleDependencyCommand : PSCmdlet
{
    /// <summary>Module containing embedded dependencies.</summary>
    [Parameter(Position = 0, ParameterSetName = "ByName", ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Specific module object containing embedded dependencies.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ByModule", ValueFromPipeline = true)]
    public PSObject Module { get; set; } = default!;

    /// <summary>Installed dependency root or module-dependencies.json receipt path.</summary>
    [Parameter(Position = 1, ParameterSetName = "ByName")]
    [Parameter(Position = 1, ParameterSetName = "ByModule")]
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

    /// <summary>Imports selected dependency modules and then the root module by exact path.</summary>
    protected override void ProcessRecord()
    {
        var context = ResolveImportContext();
        var manifest = EmbeddedModuleDependencyService.ReadManifest(context.ManifestPath);
        var entries = EmbeddedModuleDependencyService.FilterEntries(manifest, DependencyName);

        foreach (var entry in entries)
        {
            var modulePath = EmbeddedModuleDependencyService.ResolveEntryPath(context.ManifestPath, entry);
            var importPath = EmbeddedModuleDependencyService.ResolveModuleImportPath(modulePath, entry.Name);
            ImportModuleByPath(entry.Name, importPath, "Import module dependency");
        }

        if (!context.ImportRootModule)
            return;

        var rootImportPath = context.RootImportPath;
        var rootModuleName = context.RootModuleName;

        if (string.IsNullOrWhiteSpace(rootImportPath))
        {
            if (string.IsNullOrWhiteSpace(rootModuleName) && manifest.RootModule is null)
                return;

            var rootEntry = string.IsNullOrWhiteSpace(rootModuleName)
                ? EmbeddedModuleDependencyService.ResolveRootModuleEntry(manifest)
                : EmbeddedModuleDependencyService.ResolveRootModuleEntry(manifest, rootModuleName);
            ValidateRootModuleVersion(rootEntry);
            var rootPath = EmbeddedModuleDependencyService.ResolveEntryPath(context.ManifestPath, rootEntry);
            rootImportPath = EmbeddedModuleDependencyService.ResolveModuleImportPath(rootPath, rootEntry.Name);
            rootModuleName = rootEntry.Name;
        }

        ImportModuleByPath(rootModuleName, rootImportPath!, "Import root module");
    }

    private ImportContext ResolveImportContext()
    {
        if (string.Equals(ParameterSetName, "ByPath", StringComparison.OrdinalIgnoreCase) ||
            (Module is null && string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Path)))
        {
            return new ImportContext(
                EmbeddedModuleDependencyService.ResolveManifestPath(SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path)),
                importRootModule: true,
                rootModuleName: string.Empty,
                rootImportPath: null);
        }

        if (!string.IsNullOrWhiteSpace(Path))
        {
            var rootModuleName = string.Equals(ParameterSetName, "ByModule", StringComparison.OrdinalIgnoreCase)
                ? ModuleDependencyCommandHelpers.GetProperty(PSObject.AsPSObject(Module), "Name") ??
                  ModuleDependencyCommandHelpers.GetProperty(PSObject.AsPSObject(Module), "ModuleName")
                : Name;

            if (string.IsNullOrWhiteSpace(rootModuleName))
                throw new ArgumentException("Module name could not be determined from -Name or -Module.");

            return new ImportContext(
                EmbeddedModuleDependencyService.ResolveManifestPath(SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path)),
                importRootModule: true,
                rootModuleName: rootModuleName!,
                rootImportPath: null);
        }

        if (string.IsNullOrWhiteSpace(Name) && Module is null)
            throw new ArgumentException("Specify -Name, -Module, or -Path.");

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

        return new ImportContext(
            ModuleDependencyCommandHelpers.ResolveEmbeddedManifestPath(this, moduleBase!),
            importRootModule: true,
            rootModuleName: moduleName,
            rootImportPath: EmbeddedModuleDependencyService.ResolveModuleImportPath(moduleBase!, moduleName));
    }

    private void ValidateRootModuleVersion(EmbeddedModuleDependencyEntry rootEntry)
    {
        if (RequiredVersion is null)
            return;

        if (!string.Equals(rootEntry.Version, RequiredVersion.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new ItemNotFoundException($"Dependency receipt root module '{rootEntry.Name}' is version {rootEntry.Version}, not required version {RequiredVersion}.");
    }

    private void ImportModuleByPath(string moduleName, string importPath, string action)
    {
        if (!ShouldProcess(moduleName, $"{action} from '{importPath}'"))
            return;

        var script = InvokeCommand.NewScriptBlock(
            "param($p, [bool]$force, [bool]$passThru) " +
            "if ($passThru) { Import-Module -Name $p -Force:$force -PassThru -ErrorAction Stop } " +
            "else { Import-Module -Name $p -Force:$force -ErrorAction Stop }");
        var output = script.Invoke(importPath, Force.IsPresent, PassThru.IsPresent);
        if (PassThru)
        {
            foreach (var item in output)
                WriteObject(item);
        }
    }

    private sealed class ImportContext
    {
        public ImportContext(string manifestPath, bool importRootModule, string rootModuleName, string? rootImportPath)
        {
            ManifestPath = manifestPath;
            ImportRootModule = importRootModule;
            RootModuleName = rootModuleName;
            RootImportPath = rootImportPath;
        }

        public string ManifestPath { get; }
        public bool ImportRootModule { get; }
        public string RootModuleName { get; }
        public string? RootImportPath { get; }
    }
}
