using System;
using System.Collections;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a PowerShell script file with PSResourceGet-compatible PSScriptInfo metadata.
/// </summary>
[Cmdlet(VerbsCommon.New, "ManagedScriptFileInfo", SupportsShouldProcess = true)]
public sealed class NewManagedScriptFileInfoCommand : PSCmdlet
{
    /// <summary>Path to the script file to create.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Script version. Defaults to 1.0.0.0.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Script author.</summary>
    [Parameter]
    public string? Author { get; set; }

    /// <summary>Script description stored in comment-based help.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Description { get; set; } = string.Empty;

    /// <summary>Script metadata GUID. A new GUID is generated when omitted.</summary>
    [Parameter]
    public Guid Guid { get; set; } = Guid.NewGuid();

    /// <summary>Company name.</summary>
    [Parameter]
    public string? CompanyName { get; set; }

    /// <summary>Copyright text.</summary>
    [Parameter]
    public string? Copyright { get; set; }

    /// <summary>Required modules as PSResourceGet-style hashtables.</summary>
    [Parameter]
    public Hashtable[]? RequiredModules { get; set; }

    /// <summary>External module dependencies.</summary>
    [Parameter]
    public string[]? ExternalModuleDependencies { get; set; }

    /// <summary>Required script dependencies.</summary>
    [Parameter]
    public string[]? RequiredScripts { get; set; }

    /// <summary>External script dependencies.</summary>
    [Parameter]
    public string[]? ExternalScriptDependencies { get; set; }

    /// <summary>Search tags.</summary>
    [Parameter]
    public string[]? Tags { get; set; }

    /// <summary>Project URI.</summary>
    [Parameter]
    public string? ProjectUri { get; set; }

    /// <summary>License URI.</summary>
    [Parameter]
    public string? LicenseUri { get; set; }

    /// <summary>Icon URI.</summary>
    [Parameter]
    public string? IconUri { get; set; }

    /// <summary>Release notes text.</summary>
    [Parameter]
    public string? ReleaseNotes { get; set; }

    /// <summary>Private data text.</summary>
    [Parameter]
    public string? PrivateData { get; set; }

    /// <summary>Overwrite an existing script file.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        try
        {
            var resolvedPath = ManagedScriptFileInfoCommandSupport.ResolvePath(this, Path);
            if (!ShouldProcess(resolvedPath, "Create managed script file info"))
                return;

            var service = new ManagedScriptFileInfoService();
            var info = ManagedScriptFileInfoCommandSupport.CreateInfo(
                resolvedPath,
                Version,
                Author,
                Description,
                Guid,
                CompanyName,
                Copyright,
                RequiredModules,
                ExternalModuleDependencies,
                RequiredScripts,
                ExternalScriptDependencies,
                Tags,
                ProjectUri,
                LicenseUri,
                IconUri,
                ReleaseNotes,
                PrivateData,
                defaultAuthorWhenOmitted: true);

            _ = service.Create(info, Force.IsPresent);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "NewManagedScriptFileInfoFailed", ErrorCategory.NotSpecified, Path));
            throw;
        }
    }
}
