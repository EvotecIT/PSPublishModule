using System.Collections.Generic;

namespace PowerGuardian;

public sealed class DeliveryOptions
{
    public string InternalsPath { get; set; } = "Internals";
    public bool IncludeRootReadme { get; set; } = true;
    public bool IncludeRootChangelog { get; set; } = true;
    public bool IncludeRootLicense { get; set; } = true;
    public string ReadmeDestination { get; set; } = "Internals";   // Internals|Root|Both|None
    public string ChangelogDestination { get; set; } = "Internals"; // Internals|Root|Both|None
    public string LicenseDestination { get; set; } = "Internals";  // Internals|Root|Both|None
    public IReadOnlyList<(string Title, string Url)> ImportantLinks { get; set; } = new List<(string,string)>();
    public IReadOnlyList<string> IntroText { get; set; } = new List<string>();
    public IReadOnlyList<string> UpgradeText { get; set; } = new List<string>();
    public string? IntroFile { get; set; }
    public string? UpgradeFile { get; set; }
}

