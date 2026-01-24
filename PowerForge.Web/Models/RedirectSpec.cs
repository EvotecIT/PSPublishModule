namespace PowerForge.Web;

public sealed class RedirectSpec
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public int Status { get; set; } = 301;
    public RedirectMatchType MatchType { get; set; } = RedirectMatchType.Exact;
    public bool PreserveQuery { get; set; } = true;
}
