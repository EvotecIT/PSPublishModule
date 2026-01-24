namespace PowerForge.Web;

public sealed class A11ySpec
{
    public string? SkipLinkLabel { get; set; }
    public string? ExternalLinkLabel { get; set; }
    public string? NavToggleLabel { get; set; }
    public string? SearchLabel { get; set; }
}

public sealed class LinkRulesSpec
{
    public string? ExternalRel { get; set; }
    public string? ExternalTarget { get; set; }
    public bool AddExternalIcon { get; set; }
}

public sealed class AnalyticsSpec
{
    public bool Enabled { get; set; }
    public AnalyticsProvider Provider { get; set; } = AnalyticsProvider.None;
    public string? Endpoint { get; set; }
    public bool RespectDnt { get; set; } = true;
    public double SampleRate { get; set; } = 1.0;
}
