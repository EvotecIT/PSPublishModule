using System;
using System.Collections;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates PSResourceGet-compatible PSScriptInfo metadata in an existing script file.
/// </summary>
[Cmdlet(VerbsData.Update, "ManagedScriptFileInfo", SupportsShouldProcess = true)]
public sealed class UpdateManagedScriptFileInfoCommand : PSCmdlet
{
    /// <summary>Path to the script file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Script version.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Script author.</summary>
    [Parameter]
    public string? Author { get; set; }

    /// <summary>Script description stored in comment-based help.</summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>Script metadata GUID.</summary>
    [Parameter]
    public Guid Guid { get; set; }

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

    /// <summary>Remove an existing Authenticode signature block from the script body.</summary>
    [Parameter]
    public SwitchParameter RemoveSignature { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        try
        {
            var resolvedPath = ManagedScriptFileInfoCommandSupport.ResolvePath(this, Path);
            if (!ShouldProcess(resolvedPath, "Update managed script file info"))
                return;

            var guid = MyInvocation.BoundParameters.ContainsKey(nameof(Guid)) ? Guid : Guid.Empty;
            var updates = ManagedScriptFileInfoCommandSupport.CreateInfo(
                resolvedPath,
                Version,
                Author,
                Description,
                guid,
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
                PrivateData);

            _ = new ManagedScriptFileInfoService().Update(resolvedPath, updates, RemoveSignature.IsPresent);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "UpdateManagedScriptFileInfoFailed", ErrorCategory.NotSpecified, Path));
            throw;
        }
    }
}
