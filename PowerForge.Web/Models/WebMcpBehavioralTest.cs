namespace PowerForge.Web;

/// <summary>Options for exercising a rendered WebMCP site-search tool in Chromium.</summary>
public sealed class WebMcpBehavioralTestOptions
{
    /// <summary>Absolute URL of the page that registers the tool.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Expected registered tool name.</summary>
    public string ToolName { get; set; } = "search_site";
    /// <summary>Search query passed to the tool.</summary>
    public string Query { get; set; } = string.Empty;
    /// <summary>Optional result limit. Zero uses the tool default.</summary>
    public int Limit { get; set; }
    /// <summary>Browser navigation and tool execution timeout.</summary>
    public int TimeoutMs { get; set; } = 30_000;
    /// <summary>Install the Chromium runtime before executing the check.</summary>
    public bool EnsureBrowserInstalled { get; set; }
    /// <summary>Run Chromium without a visible browser window.</summary>
    public bool Headless { get; set; } = true;
}

/// <summary>Observed WebMCP tool registration, execution, and visible-page synchronization.</summary>
public sealed class WebMcpBehavioralTestResult
{
    /// <summary>Whether every required behavioral contract passed.</summary>
    public bool Success { get; set; }
    /// <summary>Tested page URL.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Expected tool name.</summary>
    public string ToolName { get; set; } = string.Empty;
    /// <summary>Search query executed by the captured tool.</summary>
    public string Query { get; set; } = string.Empty;
    /// <summary>Registered tool names observed on the page.</summary>
    public string[] RegisteredTools { get; set; } = Array.Empty<string>();
    /// <summary>Number of matching records reported by the tool.</summary>
    public int TotalMatches { get; set; }
    /// <summary>Number of records returned by the tool.</summary>
    public int Returned { get; set; }
    /// <summary>Serialized response length in characters.</summary>
    public int OutputCharacters { get; set; }
    /// <summary>Whether the tool reported additional matching records.</summary>
    public bool MoreResultsAvailable { get; set; }
    /// <summary>Whether the output budget truncated the selected results.</summary>
    public bool OutputTruncated { get; set; }
    /// <summary>Query shown in the page's visible search input after execution.</summary>
    public string VisibleQuery { get; set; } = string.Empty;
    /// <summary>Bounded visible result text captured after the input event.</summary>
    public string VisibleResultText { get; set; } = string.Empty;
    /// <summary>Absolute result URLs observed in the visible results region.</summary>
    public string[] VisibleResultUrls { get; set; } = Array.Empty<string>();
    /// <summary>Whether the visible results changed while the tool invocation was applied.</summary>
    public bool VisibleResultChanged { get; set; }
    /// <summary>Bounded JSON returned by the Website Tool.</summary>
    public string OutputJson { get; set; } = string.Empty;
    /// <summary>Contract failures or execution diagnostics.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
}
