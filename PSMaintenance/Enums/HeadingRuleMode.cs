namespace PSMaintenance;

/// <summary>
/// Controls decorative rules drawn above headings when rendering markdown.
/// </summary>
public enum HeadingRuleMode
{
    /// <summary>Do not draw rules for any headings.</summary>
    None,
    /// <summary>Draw rules for H1 headings only.</summary>
    H1,
    /// <summary>Draw rules for H1 and H2 headings.</summary>
    H1AndH2
}
