// ReSharper disable All
#nullable disable
using System;
using System.Management.Automation;

namespace PowerGuardian;

/// <summary>
/// <para type="synopsis">Copies a module's bundled documentation (Internals, README/CHANGELOG/LICENSE) to a chosen location.</para>
/// <para type="description">Resolves the module and copies its Internals folder and selected root files into a destination folder arranged by <see cref="DocumentationLayout"/>. Repeat runs can merge, overwrite, skip or stop based on <see cref="OnExistsOption"/>. When successful, returns the destination path.</para>
/// <example>
///   <code>Install-ModuleDocumentation -Name EFAdminManager -Path C:\Docs -Layout ModuleAndVersion</code>
/// </example>
/// <example>
///   <code>Get-Module -ListAvailable EFAdminManager | Install-ModuleDocumentation -Path C:\Docs -OnExists Merge -Open</code>
/// </example>
/// </summary>
[Cmdlet(VerbsLifecycle.Install, "ModuleDocumentation", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
[Alias("Install-Documentation")]
[OutputType(typeof(string))]
public sealed class InstallModuleDocumentationCommand : PSCmdlet
{
    /// <summary>Module name to install documentation for.</summary>
    [Parameter(ParameterSetName = "ByName", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    public string Name { get; set; }

    /// <summary>Module object to install documentation for. Alternative to <c>-Name</c>.</summary>
    [Parameter(ParameterSetName = "ByModule", ValueFromPipeline = true)]
    [Alias("InputObject", "ModuleInfo")]
    public PSModuleInfo Module { get; set; }

    /// <summary>Exact version to select when multiple module versions are installed.</summary>
    public Version RequiredVersion { get; set; }

    /// <summary>Destination folder where documentation will be written.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Output folder structure strategy. Default is ModuleAndVersion.</summary>
    public DocumentationLayout Layout { get; set; } = DocumentationLayout.ModuleAndVersion;

    /// <summary>Behavior when the destination folder already exists. Default is Merge.</summary>
    public OnExistsOption OnExists { get; set; } = OnExistsOption.Merge;

    /// <summary>Legacy toggle equivalent to selecting ModuleAndVersion when set; Direct when not set.</summary>
    [Parameter] public SwitchParameter CreateVersionSubfolder { get; set; }
    /// <summary>Overwrite files during merge or overwrite operations.</summary>
    [Parameter] public SwitchParameter Force { get; set; }
    /// <summary>Plan only; output the resolved destination without copying files.</summary>
    [Parameter] public SwitchParameter ListOnly { get; set; }
    /// <summary>Open the resulting folder or README after installation.</summary>
    [Parameter] public SwitchParameter Open { get; set; }
    /// <summary>Suppress IntroText display during installation.</summary>
    [Parameter] public SwitchParameter NoIntro { get; set; }

    protected override void ProcessRecord()
    {
        var resolver = new ModuleResolver(this);
        var installer = new DocumentationInstaller(this);

        // Resolve module (by Module param or by Name)
        PSObject modulePso;
        if (Module != null)
        {
            modulePso = PSObject.AsPSObject(Module);
        }
        else
        {
            modulePso = resolver.Resolve(Name, null, RequiredVersion);
        }

        var modName = (modulePso.Properties["Name"]?.Value ?? modulePso.Properties["ModuleName"]?.Value)?.ToString();
        var modVersion = modulePso.Properties["Version"]?.Value?.ToString();
        var modBase = modulePso.Properties["ModuleBase"]?.Value?.ToString();
        if (string.IsNullOrEmpty(modName) || string.IsNullOrEmpty(modVersion) || string.IsNullOrEmpty(modBase))
            throw new InvalidOperationException("Unable to resolve module name/version/base.");

        // Legacy toggle mapping if user only passed CreateVersionSubfolder
        if (MyInvocation.BoundParameters.ContainsKey(nameof(CreateVersionSubfolder)) && !MyInvocation.BoundParameters.ContainsKey(nameof(Layout)))
            Layout = CreateVersionSubfolder ? DocumentationLayout.ModuleAndVersion : DocumentationLayout.Direct;

        var dest = installer.PlanDestination(modName, modVersion, Path, Layout);

        if (ListOnly)
        {
            WriteVerbose($"Would copy Internals to '{dest}' using Layout={Layout}, OnExists={OnExists}.");
            WriteObject(dest);
            return;
        }

        if (ShouldProcess(modName, $"Install docs to '{dest}'"))
        {
            var result = installer.Install(modBase, modName, modVersion, dest, OnExists, Force, Open, NoIntro);
            WriteObject(result);
        }
    }
}
