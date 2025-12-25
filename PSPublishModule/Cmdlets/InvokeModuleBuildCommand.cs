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
    /// <summary>Path to the staged module root folder (contains PSD1/PSM1).</summary>
    [Parameter(Mandatory = true)] public string ProjectRoot { get; set; } = string.Empty;
    /// <summary>Name of the module being built (used for PSD1 naming and exports).</summary>
    [Parameter(Mandatory = true)] public string ModuleName { get; set; } = string.Empty;
    /// <summary>Path to the .NET project that should be published into the module Lib folder.</summary>
    [Parameter(Mandatory = true)] public string CsprojPath { get; set; } = string.Empty;
    /// <summary>Base module version used for build and install.</summary>
    [Parameter(Mandatory = true)] public string ModuleVersion { get; set; } = string.Empty;
    /// <summary>Build configuration used for publishing (e.g., Release).</summary>
    [Parameter] public string Configuration { get; set; } = "Release";
    /// <summary>Target frameworks to publish (e.g., net472, net8.0).</summary>
    [Parameter] public string[] Frameworks { get; set; } = new[] { "net472", "net8.0" };
    /// <summary>Installation strategy controlling versioned install behavior.</summary>
    [Parameter] public InstallationStrategy Strategy { get; set; } = InstallationStrategy.AutoRevision;
    /// <summary>Number of installed versions to keep after install.</summary>
    [Parameter] public int KeepVersions { get; set; } = 3;
    /// <summary>Destination module roots to install to. When empty, defaults are used.</summary>
    [Parameter] public string[]? InstallRoots { get; set; }

    /// <summary>Author value written to the manifest.</summary>
    [Parameter] public string? Author { get; set; }
    /// <summary>CompanyName value written to the manifest.</summary>
    [Parameter] public string? CompanyName { get; set; }
    /// <summary>Description value written to the manifest.</summary>
    [Parameter] public string? Description { get; set; }
    /// <summary>CompatiblePSEditions written to the manifest.</summary>
    [Parameter] public string[]? CompatiblePSEditions { get; set; } = new[] { "Desktop", "Core" };
    /// <summary>Tags written to the manifest PrivateData.PSData.</summary>
    [Parameter] public string[]? Tags { get; set; }
    /// <summary>IconUri written to the manifest PrivateData.PSData.</summary>
    [Parameter] public string? IconUri { get; set; }
    /// <summary>ProjectUri written to the manifest PrivateData.PSData.</summary>
    [Parameter] public string? ProjectUri { get; set; }

    /// <summary>
    /// Executes the build (dotnet publish + manifest + exports) and installs to versioned module roots.
    /// </summary>
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
        WriteObject(new PSObject(new { ResolvedVersion = res.Version, res.InstalledPaths, res.PrunedPaths }));
    }
}

