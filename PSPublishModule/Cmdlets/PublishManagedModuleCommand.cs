using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Publishes a PowerShell module package through the managed C# module engine.
/// </summary>
/// <remarks>
/// <para>
/// This initial managed publish surface creates a NuGet package from a module folder and writes it to a local folder
/// feed or output directory. Remote feed push support is intentionally left to the managed HTTP publish phase.
/// </para>
/// </remarks>
/// <example>
/// <summary>Publish a module to a local folder feed</summary>
/// <code>Publish-ManagedModule -Path C:\Source\Company.Tools -Repository C:\Packages</code>
/// </example>
[Cmdlet(VerbsData.Publish, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModulePackResult))]
public sealed class PublishManagedModuleCommand : PSCmdlet
{
    /// <summary>Module folder to package.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ModulePath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Local folder repository that receives the package.</summary>
    [Parameter(Position = 1)]
    [Alias("RepositoryPath")]
    [ValidateNotNullOrEmpty]
    public string? Repository { get; set; }

    /// <summary>Output directory used when Repository is omitted.</summary>
    [Parameter]
    [Alias("DestinationPath", "OutputPath")]
    [ValidateNotNullOrEmpty]
    public string? OutputDirectory { get; set; }

    /// <summary>Optional explicit module manifest path.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ManifestPath { get; set; }

    /// <summary>Optional package id override.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    /// <summary>Optional package version override.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Optional authors override.</summary>
    [Parameter]
    public string? Authors { get; set; }

    /// <summary>Optional description override.</summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>Optional project URL override.</summary>
    [Parameter]
    public string? ProjectUrl { get; set; }

    /// <summary>Optional package tags override.</summary>
    [Parameter]
    public string[]? Tags { get; set; }

    /// <summary>Overwrite an existing package.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Creates and publishes the package to the selected local destination.</summary>
    protected override void ProcessRecord()
    {
        var modulePath = ManagedModuleCommandSupport.ResolveProviderPath(this, Path)!;
        var manifestPath = ManagedModuleCommandSupport.ResolveProviderPath(this, ManifestPath);
        var outputDirectory = ResolveOutputDirectory();

        if (!ShouldProcess(modulePath, $"Publish managed module package to '{outputDirectory}'"))
            return;

        var result = new ManagedModulePackService().Pack(new ManagedModulePackRequest
        {
            ModulePath = modulePath,
            ManifestPath = manifestPath,
            Name = Name,
            Version = Version,
            OutputDirectory = outputDirectory,
            Authors = Authors,
            Description = Description,
            ProjectUrl = ProjectUrl,
            Tags = Tags,
            Force = Force.IsPresent
        });

        WriteObject(result);
    }

    private string ResolveOutputDirectory()
    {
        if (!string.IsNullOrWhiteSpace(Repository))
        {
            var repository = new ManagedModuleRepository("Repository", Repository!);
            if (repository.Kind != ManagedModuleRepositoryKind.LocalFolder)
                throw new NotSupportedException("Managed remote publish is not implemented yet. Use a local folder repository or OutputDirectory.");

            return ManagedModuleCommandSupport.ResolveProviderPath(this, Repository)!;
        }

        if (!string.IsNullOrWhiteSpace(OutputDirectory))
            return ManagedModuleCommandSupport.ResolveProviderPath(this, OutputDirectory)!;

        throw new ArgumentException("Specify Repository as a local folder path or provide OutputDirectory.");
    }
}
