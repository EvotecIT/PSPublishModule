namespace PowerForge.Web;

public sealed class SocialSpec
{
    public bool Enabled { get; set; } = true;
    public string? SiteName { get; set; }
    public string? Image { get; set; }
    public string? TwitterCard { get; set; } = "summary";
}
