using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates Debian desktop-package metadata for the DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create Debian desktop-package metadata</summary>
/// <code>
/// New-ConfigurationDotNetDebian -PackageName 'sample-studio' -Version '1.0.0' -Maintainer 'Example &lt;support@example.com&gt;' -Description 'Sample document studio.' -Executable 'Sample.Studio' -CommandName 'sample-studio' -InstallDirectoryName 'sample-studio' -DesktopName 'Sample Studio' -DesktopCategories 'Office;Utility;' -IconPath 'Assets/Sample.Studio.png'
/// </code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetDebian")]
[OutputType(typeof(DotNetPublishDebianOptions))]
public sealed class NewConfigurationDotNetDebianCommand : PSCmdlet
{
    /// <summary>Lower-case Debian package name.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Debian package version.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Version { get; set; } = string.Empty;

    /// <summary>Package maintainer identity.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Maintainer { get; set; } = string.Empty;

    /// <summary>Short package description.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Description { get; set; } = string.Empty;

    /// <summary>Executable path relative to the published payload root.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Executable { get; set; } = string.Empty;

    /// <summary>Command installed under /usr/bin.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Directory name installed under /opt.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string InstallDirectoryName { get; set; } = string.Empty;

    /// <summary>Optional Debian dependency expression.</summary>
    [Parameter]
    public string? Depends { get; set; }

    /// <summary>Debian section. Defaults to utils.</summary>
    [Parameter]
    public string Section { get; set; } = "utils";

    /// <summary>Debian priority. Defaults to optional.</summary>
    [Parameter]
    public string Priority { get; set; } = "optional";

    /// <summary>Optional architecture override; otherwise inferred from the runtime identifier.</summary>
    [Parameter]
    public string? Architecture { get; set; }

    /// <summary>Optional desktop application name. Setting it enables a desktop entry.</summary>
    [Parameter]
    public string? DesktopName { get; set; }

    /// <summary>Optional desktop application comment.</summary>
    [Parameter]
    public string? DesktopComment { get; set; }

    /// <summary>Optional freedesktop category list.</summary>
    [Parameter]
    public string? DesktopCategories { get; set; }

    /// <summary>Optional MIME type list.</summary>
    [Parameter]
    public string? MimeTypes { get; set; }

    /// <summary>Optional startup WM class.</summary>
    [Parameter]
    public string? StartupWmClass { get; set; }

    /// <summary>Optional PNG icon path resolved from the project root.</summary>
    [Parameter]
    public string? IconPath { get; set; }

    /// <summary>Icon size used in the hicolor theme path. Defaults to 256.</summary>
    [Parameter]
    [ValidateRange(1, 4096)]
    public int IconSize { get; set; } = 256;

    /// <summary>Emits a <see cref="DotNetPublishDebianOptions"/> object.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishDebianOptions
        {
            PackageName = PackageName.Trim(),
            Version = Version.Trim(),
            Maintainer = Maintainer.Trim(),
            Description = Description.Trim(),
            Executable = Executable.Trim(),
            CommandName = CommandName.Trim(),
            InstallDirectoryName = InstallDirectoryName.Trim(),
            Depends = NormalizeNullable(Depends),
            Section = Section.Trim(),
            Priority = Priority.Trim(),
            Architecture = NormalizeNullable(Architecture),
            DesktopName = NormalizeNullable(DesktopName),
            DesktopComment = NormalizeNullable(DesktopComment),
            DesktopCategories = NormalizeNullable(DesktopCategories),
            MimeTypes = NormalizeNullable(MimeTypes),
            StartupWmClass = NormalizeNullable(StartupWmClass),
            IconPath = NormalizeNullable(IconPath),
            IconSize = IconSize
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
