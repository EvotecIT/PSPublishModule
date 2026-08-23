using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds a packaged executable, typed CLR library, or importable binary/hybrid module from PowerShell source.
/// </summary>
/// <example>
/// <summary>Package a script as a single-file executable</summary>
/// <code>Build-PowerShellArtifact -Path .\tool.ps1 -Kind Executable -OutputDirectory .\artifacts</code>
/// </example>
/// <example>
/// <summary>Compile eligible functions and retain unsupported functions as script fallback</summary>
/// <code>Build-PowerShellArtifact -Path .\module.psm1 -Kind BinaryModule -Mode Hybrid -OutputDirectory .\artifacts</code>
/// </example>
[Cmdlet("Build", "PowerShellArtifact", SupportsShouldProcess = true)]
[OutputType(typeof(PowerShellCompilationBuildResult))]
public sealed class BuildPowerShellArtifactCommand : PSCmdlet
{
    /// <summary>PowerShell script or module source path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Artifact shape to produce.</summary>
    [Parameter(Mandatory = true)]
    public PowerShellCompilationArtifactKind Kind { get; set; }

    /// <summary>Destination for durable artifacts and the compilation manifest.</summary>
    [Parameter]
    public string? OutputDirectory { get; set; }

    /// <summary>Artifact file and assembly name. Defaults to the source file name.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Fallback policy. Defaults to Package for EXE, Strict for binary modules, and Hybrid for CLR libraries.</summary>
    [Parameter]
    public PowerShellCompilationMode? Mode { get; set; }

    /// <summary>Generated .NET target framework.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string TargetFramework { get; set; } = "net8.0";

    /// <summary>Optional runtime identifier used when publishing an executable.</summary>
    [Parameter]
    public string? RuntimeIdentifier { get; set; }

    /// <summary>Include the .NET runtime when publishing an executable.</summary>
    [Parameter]
    public SwitchParameter SelfContained { get; set; }

    /// <summary>Publish an executable as one file.</summary>
    [Parameter]
    public bool SingleFile { get; set; } = true;

    /// <summary>Retain the generated project workspace for inspection.</summary>
    [Parameter]
    public SwitchParameter KeepBuildWorkspace { get; set; }

    /// <summary>Maximum restore and compile time in seconds.</summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 300;

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        var sourcePath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var outputPath = string.IsNullOrWhiteSpace(OutputDirectory)
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourcePath) ?? SessionState.Path.CurrentFileSystemLocation.Path, "artifacts")
            : SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputDirectory);
        var artifactName = string.IsNullOrWhiteSpace(Name) ? System.IO.Path.GetFileNameWithoutExtension(sourcePath) : Name!;
        var mode = Mode ?? GetDefaultMode(Kind);
        if (!ShouldProcess(outputPath, $"Build {Kind} artifact '{artifactName}' from '{sourcePath}'"))
            return;

        var spec = new PowerShellCompilationBuildSpec(sourcePath, outputPath, artifactName, Kind, mode)
        {
            TargetFramework = TargetFramework,
            RuntimeIdentifier = RuntimeIdentifier,
            SelfContained = SelfContained.IsPresent,
            SingleFile = SingleFile,
            KeepBuildWorkspace = KeepBuildWorkspace.IsPresent,
            TimeoutSeconds = TimeoutSeconds
        };
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        if (!result.Succeeded)
        {
            var message = result.Error ?? "PowerShell artifact build failed.";
            if (!string.IsNullOrWhiteSpace(result.BuildOutput))
                message += Environment.NewLine + result.BuildOutput;
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException(message),
                "PowerShellArtifactBuildFailed",
                ErrorCategory.InvalidResult,
                spec));
        }
        WriteObject(result);
    }

    private static PowerShellCompilationMode GetDefaultMode(PowerShellCompilationArtifactKind kind)
        => kind switch
        {
            PowerShellCompilationArtifactKind.Executable => PowerShellCompilationMode.Package,
            PowerShellCompilationArtifactKind.BinaryModule => PowerShellCompilationMode.Strict,
            _ => PowerShellCompilationMode.Hybrid
        };
}
