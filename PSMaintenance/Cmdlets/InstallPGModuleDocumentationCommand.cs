// ReSharper disable All
using System;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// <para type="synopsis">Copies a module's bundled documentation (Internals, README/CHANGELOG/LICENSE) to a chosen location.</para>
/// <para type="description">Resolves the module and copies its Internals folder and selected root files into a destination folder arranged by <see cref="DocumentationLayout"/>. Repeat runs can merge, overwrite, skip or stop based on <see cref="OnExistsOption"/>. When successful, returns the destination path.</para>
/// </summary>
/// <example>
///   <code>Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout ModuleAndVersion</code>
/// </example>
/// <example>
///   <code>Get-Module -ListAvailable EFAdminManager | Install-ModuleDocumentation -Path C:\\Docs -OnExists Merge -Open</code>
/// </example>
/// <example>
///   <code>Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout Module</code>
/// </example>
/// <example>
///   <code>Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -Layout Direct</code>
/// </example>
/// <example>
///   <code>Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -OnExists Overwrite</code>
/// </example>
/// <example>
///   <code>Install-ModuleDocumentation -Name EFAdminManager -Path C:\\Docs -OnExists Merge -Force</code>
/// </example>
/// <example>
///   <code>New-ConfigurationInformation -IncludeAll 'Internals\\' ; New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -DocumentationOrder '01-Intro.md','02-HowTo.md' -IncludeRootReadme -IncludeRootChangelog</code>
/// </example>
[Cmdlet(VerbsLifecycle.Install, "ModuleDocumentation", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
[Alias("Install-Documentation")]
[OutputType(typeof(string))]
public sealed partial class InstallModuleDocumentationCommand : PSCmdlet
{
    // Parameters are in partial class (InstallPGModuleDocumentationCommand.Parameters.cs)

    /// <summary>
    /// Executes the copy operation according to parameters and writes the destination path.
    /// </summary>
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

        var dest = installer.PlanDestination(modName!, modVersion!, Path, Layout);

        if (ListOnly)
        {
            WriteVerbose($"Would copy Internals to '{dest}' using Layout={Layout}, OnExists={OnExists}.");
            WriteObject(dest);
            return;
        }

        if (ShouldProcess(modName!, $"Install docs to '{dest}'"))
        {
            var result = installer.Install(modBase!, modName!, modVersion!, dest, OnExists, Force, Open, NoIntro);
            WriteObject(result);
        }
    }
}
