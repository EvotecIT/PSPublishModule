using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Installs a module and its embedded dependencies to an explicit private runtime folder.
/// </summary>
/// <remarks>
/// <para>
/// The command reads the dependency manifest produced by <c>New-ConfigurationModule -Type EmbeddedModule</c>
/// and copies the root module plus each dependency payload to the requested path. Modules are not installed
/// into PSModulePath unless the chosen path is already part of PSModulePath.
/// </para>
/// </remarks>
/// <example>
/// <summary>Install a module and all embedded dependencies to a private runtime folder</summary>
/// <code>Install-ModuleDependency -Name EntraIDConfig -Path C:\PrivateDeps</code>
/// </example>
/// <example>
/// <summary>Install only selected dependencies</summary>
/// <code>Install-ModuleDependency -Name EntraIDConfig -DependencyName Microsoft.Graph.Authentication -Path C:\PrivateDeps -Force</code>
/// </example>
[Cmdlet(VerbsLifecycle.Install, "ModuleDependency", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
[OutputType(typeof(EmbeddedModuleDependencyInstallResult))]
public sealed class InstallModuleDependencyCommand : PSCmdlet
{
    /// <summary>Module containing embedded dependencies.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ByName", ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Specific module object containing embedded dependencies.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ByModule", ValueFromPipeline = true)]
    public PSObject Module { get; set; } = default!;

    /// <summary>Optional exact source module version.</summary>
    [Parameter]
    public Version? RequiredVersion { get; set; }

    /// <summary>Dependency names to install. When omitted, installs all embedded dependencies.</summary>
    [Parameter]
    public string[]? DependencyName { get; set; }

    /// <summary>Destination folder. The root module and dependencies are copied under Name\Version folders.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Conflict handling when a dependency version folder already exists.</summary>
    [Parameter]
    public OnExistsOption OnExists { get; set; } = OnExistsOption.Merge;

    /// <summary>Overwrite existing dependency folders when merge or overwrite behavior requires it.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Preview planned dependency copies without writing files.</summary>
    [Parameter]
    public SwitchParameter ListOnly { get; set; }

    /// <summary>Copies the module runtime payload and writes install results.</summary>
    protected override void ProcessRecord()
    {
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

        var manifestPath = ModuleDependencyCommandHelpers.ResolveEmbeddedManifestPath(this, moduleBase!);
        var destination = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var service = new EmbeddedModuleDependencyService(new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose")));

        var moduleVersion = ModuleDependencyCommandHelpers.GetProperty(module, "Version");

        if (!ListOnly && !ShouldProcess(moduleName, $"Install module runtime to '{destination}'"))
            return;

        var results = service.Install(
            dependencyManifestPath: manifestPath,
            destinationRoot: destination,
            rootModuleName: moduleName,
            rootModuleVersion: moduleVersion,
            rootModuleBasePath: moduleBase,
            dependencyNames: DependencyName,
            onExists: OnExists,
            force: Force.IsPresent,
            listOnly: ListOnly.IsPresent);

        foreach (var result in results)
            WriteObject(result);
    }

}
