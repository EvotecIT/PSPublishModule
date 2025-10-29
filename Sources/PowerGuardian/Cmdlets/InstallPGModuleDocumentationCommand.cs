// ReSharper disable All
#nullable disable
using System;
using System.Management.Automation;

namespace PowerGuardian;

[Cmdlet(VerbsLifecycle.Install, "ModuleDocumentation", DefaultParameterSetName = "ByName", SupportsShouldProcess = true)]
[Alias("Install-Documentation")]
public sealed class InstallModuleDocumentationCommand : PSCmdlet
{
    [Parameter(ParameterSetName = "ByName", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    public string Name { get; set; }

    [Parameter(ParameterSetName = "ByModule", ValueFromPipeline = true)]
    [Alias("InputObject", "ModuleInfo")]
    public PSModuleInfo Module { get; set; }

    public Version RequiredVersion { get; set; }

    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    public DocumentationLayout Layout { get; set; } = DocumentationLayout.ModuleAndVersion;

    public OnExistsOption OnExists { get; set; } = OnExistsOption.Merge;

    [Parameter] public SwitchParameter CreateVersionSubfolder { get; set; }
    [Parameter] public SwitchParameter Force { get; set; }
    [Parameter] public SwitchParameter ListOnly { get; set; }
    [Parameter] public SwitchParameter Open { get; set; }
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
