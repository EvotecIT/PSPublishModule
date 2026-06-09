using System.Collections.Generic;

namespace PowerForge;

internal sealed class NuGetPackagePublishRequest
{
    public IReadOnlyList<string> Roots { get; set; } = System.Array.Empty<string>();
    public string ApiKey { get; set; } = string.Empty;
    public string Source { get; set; } = "https://api.nuget.org/v3/index.json";
    public bool SkipDuplicate { get; set; }
}
