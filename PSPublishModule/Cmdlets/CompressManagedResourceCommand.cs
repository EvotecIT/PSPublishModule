using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Compresses a managed PowerShell resource folder into a NuGet package.
/// </summary>
/// <remarks>
/// <para>
/// This PSResourceGet-shaped surface currently packages PowerShell module folders through the managed C# pack service.
/// Script resources remain intentionally unsupported until the managed script metadata lane exists.
/// </para>
/// </remarks>
/// <example>
/// <summary>Compress a module folder to a package directory</summary>
/// <code>Compress-ManagedResource -Path C:\Source\Company.Tools -DestinationPath C:\Packages -PassThru</code>
/// </example>
[Cmdlet(VerbsData.Compress, "ManagedResource", SupportsShouldProcess = true)]
[OutputType(typeof(FileInfo))]
public sealed class CompressManagedResourceCommand : PSCmdlet
{
    /// <summary>Path to the module resource folder to compress.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Directory where the compressed .nupkg file is written.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [Alias("OutputDirectory", "OutputPath")]
    [ValidateNotNullOrEmpty]
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Return the created package as a FileInfo object.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Skip managed module manifest metadata validation before package creation.</summary>
    [Parameter]
    public SwitchParameter SkipModuleManifestValidate { get; set; }

    /// <summary>Compresses the resource when the target is approved by ShouldProcess.</summary>
    protected override void ProcessRecord()
    {
        var resourcePath = ManagedModuleCommandSupport.ResolveProviderPath(this, Path)!;
        var destinationPath = ManagedModuleCommandSupport.ResolveProviderPath(this, DestinationPath)!;
        ValidateSupportedResourcePath(resourcePath);

        if (!ShouldProcess(resourcePath, $"Compress managed resource to '{destinationPath}'"))
            return;

        var result = new ManagedModulePackService().Pack(new ManagedModulePackRequest
        {
            ModulePath = resourcePath,
            OutputDirectory = destinationPath,
            SkipModuleManifestValidate = SkipModuleManifestValidate.IsPresent
        });

        if (PassThru.IsPresent)
            WriteObject(new FileInfo(result.PackagePath));
    }

    private static void ValidateSupportedResourcePath(string resourcePath)
    {
        if (File.Exists(resourcePath))
        {
            if (System.IO.Path.GetExtension(resourcePath).Equals(".ps1", System.StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Script resource compression is not supported yet. Use Microsoft.PowerShell.PSResourceGet Compress-PSResource for scripts until the managed script resource lane is implemented.");

            throw new InvalidOperationException($"Managed resource path must be a module folder: {resourcePath}");
        }

        if (!Directory.Exists(resourcePath))
            throw new DirectoryNotFoundException($"Managed resource folder was not found: {resourcePath}");

        var moduleManifests = Directory.EnumerateFiles(resourcePath, "*.psd1", SearchOption.TopDirectoryOnly).Any();
        if (!moduleManifests &&
            Directory.EnumerateFiles(resourcePath, "*.ps1", SearchOption.TopDirectoryOnly).Any())
        {
            throw new NotSupportedException("Script resource compression is not supported yet. Use Microsoft.PowerShell.PSResourceGet Compress-PSResource for scripts until the managed script resource lane is implemented.");
        }
    }
}
