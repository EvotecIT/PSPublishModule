namespace PowerForge.Web;

/// <summary>Configuration for Prism syntax highlighting injection.</summary>
public sealed class PrismSpec
{
    /// <summary>Mode: auto (default), always, or off.</summary>
    public string Mode { get; set; } = "auto";
    /// <summary>Source: local, cdn, or hybrid (prefer local).</summary>
    public string Source { get; set; } = "cdn";
    /// <summary>Light theme name or CSS path override.</summary>
    public string? ThemeLight { get; set; }
    /// <summary>Dark theme name or CSS path override.</summary>
    public string? ThemeDark { get; set; }
    /// <summary>CDN base URL (used when Source=cdn).</summary>
    public string? CdnBase { get; set; }
    /// <summary>Default language for code blocks missing an explicit language (e.g., "csharp").</summary>
    public string? DefaultLanguage { get; set; }
    /// <summary>Local asset paths (used when Source=local).</summary>
    public PrismLocalSpec? Local { get; set; }
}

/// <summary>Local Prism asset paths.</summary>
public sealed class PrismLocalSpec
{
    /// <summary>Light theme CSS path.</summary>
    public string? ThemeLight { get; set; }
    /// <summary>Dark theme CSS path.</summary>
    public string? ThemeDark { get; set; }
    /// <summary>Prism core script path.</summary>
    public string? Core { get; set; }
    /// <summary>Prism autoloader script path.</summary>
    public string? Autoloader { get; set; }
    /// <summary>Language components base path.</summary>
    public string? LanguagesPath { get; set; }
}
