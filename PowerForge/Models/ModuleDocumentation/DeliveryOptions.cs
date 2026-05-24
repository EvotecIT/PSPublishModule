using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Delivery metadata embedded into module manifests and consumed at runtime.
/// Keep this focused on where to find local content inside the installed module.
/// </summary>
public sealed class DeliveryOptions
{
    /// <summary>Relative path to the Internals folder inside a module. Default is <c>Internals</c>.</summary>
    public string InternalsPath { get; set; } = "Internals";
    /// <summary>Relative path to scripts inside the module (used by Install-ModuleScript). Default is <c>Internals\\Scripts</c>.</summary>
    public string ScriptsPath { get; set; } = System.IO.Path.Combine("Internals","Scripts");
    /// <summary>Additional documentation folders under the module root (e.g., <c>Docs</c>, <c>Internals\\Docs</c>).</summary>
    public IReadOnlyList<string> DocsPaths { get; set; } = new List<string>();
    /// <summary>List of important links to display (Title/Url pairs).</summary>
    public IReadOnlyList<(string Title, string Url)> ImportantLinks { get; set; } = new List<(string,string)>();
    /// <summary>Introductory text lines shown in the Intro section.</summary>
    public IReadOnlyList<string> IntroText { get; set; } = new List<string>();
    /// <summary>Upgrade guidance text lines shown in the Upgrade section.</summary>
    public IReadOnlyList<string> UpgradeText { get; set; } = new List<string>();
    /// <summary>Optional path to a markdown file providing the introduction content.</summary>
    public string? IntroFile { get; set; }
    /// <summary>Optional path to a markdown file providing upgrade guidance.</summary>
    public string? UpgradeFile { get; set; }
}
