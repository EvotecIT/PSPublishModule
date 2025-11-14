using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds a PowerShell module (libraries + manifest + install) using C# only.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "ModuleBuild", SupportsShouldProcess = false)]
public sealed class InvokeModuleBuildCommand : PSCmdlet
{
    [Parameter(Mandatory = true)] public string ProjectRoot { get; set; } = string.Empty; // Module folder
    [Parameter(Mandatory = true)] public string ModuleName { get; set; } = string.Empty;
    [Parameter(Mandatory = true)] public string CsprojPath { get; set; } = string.Empty;
    [Parameter(Mandatory = true)] public string ModuleVersion { get; set; } = string.Empty;
    [Parameter] public string Configuration { get; set; } = "Release";
    [Parameter] public string[] Frameworks { get; set; } = new[] { "net472", "net8.0" };
    [Parameter] public InstallationStrategy Strategy { get; set; } = InstallationStrategy.AutoRevision;
    [Parameter] public int KeepVersions { get; set; } = 3;
    [Parameter] public string[]? InstallRoots { get; set; }

    [Parameter] public string? Author { get; set; }
    [Parameter] public string? CompanyName { get; set; }
    [Parameter] public string? Description { get; set; }
    [Parameter] public string[]? CompatiblePSEditions { get; set; } = new[] { "Desktop", "Core" };
    [Parameter] public string[]? Tags { get; set; }
    [Parameter] public string? IconUri { get; set; }
    [Parameter] public string? ProjectUri { get; set; }

    protected override void ProcessRecord()
    {
        var logger = new ConsoleLogger { IsVerbose = MyInvocation.BoundParameters.ContainsKey("Verbose") };
        var builder = new ModuleBuilder(logger);
        var opts = new ModuleBuilder.Options
        {
            ProjectRoot = System.IO.Path.GetFullPath(ProjectRoot.Trim().Trim('"')),
            ModuleName = ModuleName.Trim(),
            CsprojPath = System.IO.Path.GetFullPath(CsprojPath.Trim().Trim('"')),
            Configuration = Configuration,
            Frameworks = Frameworks,
            ModuleVersion = ModuleVersion,
            Strategy = Strategy,
            KeepVersions = KeepVersions,
            InstallRoots = (InstallRoots ?? Array.Empty<string>()).Select(p => System.IO.Path.GetFullPath(p)).ToArray(),
            Author = Author,
            CompanyName = CompanyName,
            Description = Description,
            CompatiblePSEditions = CompatiblePSEditions ?? new[] { "Desktop", "Core" },
            Tags = Tags ?? Array.Empty<string>(),
            IconUri = IconUri,
            ProjectUri = ProjectUri
        };
        var res = builder.Build(opts);
        WriteObject(new PSObject(new { res.ResolvedVersion, res.InstalledPaths, res.PrunedPaths }));
    }
}

