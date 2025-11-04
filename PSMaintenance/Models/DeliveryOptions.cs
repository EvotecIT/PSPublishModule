using System.Collections.Generic;

namespace PSMaintenance;

/// <summary>
/// Delivery metadata embedded into module manifests by PSPublishModule and consumed by PSMaintenance.
/// </summary>
public sealed class DeliveryOptions
{
    /// <summary>Relative path to the Internals folder inside a module. Default is <c>Internals</c>.</summary>
    public string InternalsPath { get; set; } = "Internals";
    /// <summary>Include README.* from module root when installing documentation.</summary>
    public bool IncludeRootReadme { get; set; } = true;
    /// <summary>Include CHANGELOG.* from module root when installing documentation.</summary>
    public bool IncludeRootChangelog { get; set; } = true;
    /// <summary>Include LICENSE.* from module root when installing documentation.</summary>
    public bool IncludeRootLicense { get; set; } = true;
    /// <summary>Where to bundle README when building: Internals, Root, Both or None.</summary>
    public string ReadmeDestination { get; set; } = "Internals";   // Internals|Root|Both|None
    /// <summary>Where to bundle CHANGELOG when building: Internals, Root, Both or None.</summary>
    public string ChangelogDestination { get; set; } = "Internals"; // Internals|Root|Both|None
    /// <summary>Where to bundle LICENSE when building: Internals, Root, Both or None.</summary>
    public string LicenseDestination { get; set; } = "Internals";  // Internals|Root|Both|None
    /// <summary>List of important links to display (Title/Url pairs).</summary>
    public IReadOnlyList<(string Title, string Url)> ImportantLinks { get; set; } = new List<(string,string)>();
    /// <summary>Introductory text lines presented by Show-ModuleDocumentation -Intro.</summary>
    public IReadOnlyList<string> IntroText { get; set; } = new List<string>();
    /// <summary>Upgrade guidance text lines presented by Show-ModuleDocumentation -Upgrade.</summary>
    public IReadOnlyList<string> UpgradeText { get; set; } = new List<string>();
    /// <summary>Optional path to a markdown file providing the introduction content.</summary>
    public string? IntroFile { get; set; }
    /// <summary>Optional path to a markdown file providing upgrade guidance.</summary>
    public string? UpgradeFile { get; set; }
}
