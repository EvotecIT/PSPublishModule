namespace PowerForge.Web;

/// <summary>Defines a redirect or route override.</summary>
public sealed class RedirectSpec
{
    /// <summary>Source path or pattern.</summary>
    public string From { get; set; } = string.Empty;
    /// <summary>Destination path or URL.</summary>
    public string To { get; set; } = string.Empty;
    /// <summary>HTTP status code to emit.</summary>
    public int Status { get; set; } = 301;
    /// <summary>Match strategy for the <see cref="From"/> value.</summary>
    public RedirectMatchType MatchType { get; set; } = RedirectMatchType.Exact;
    /// <summary>When true, preserve the original query string.</summary>
    public bool PreserveQuery { get; set; } = true;
}
