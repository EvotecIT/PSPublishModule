namespace PowerForge.Web;

/// <summary>Resolved runtime model for localization/language selectors.</summary>
public sealed class LocalizationRuntime
{
    /// <summary>True when localization is enabled and at least one language is configured.</summary>
    public bool Enabled { get; set; }

    /// <summary>Current language entry.</summary>
    public LocalizationLanguageRuntime Current { get; set; } = new();

    /// <summary>All language entries for current page switcher rendering.</summary>
    public LocalizationLanguageRuntime[] Languages { get; set; } = Array.Empty<LocalizationLanguageRuntime>();
}

/// <summary>Resolved language entry for runtime rendering.</summary>
public sealed class LocalizationLanguageRuntime
{
    /// <summary>Language code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Resolved route prefix for this language.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>True when this language is the default language.</summary>
    public bool IsDefault { get; set; }

    /// <summary>True when this language is current for rendered page.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Resolved URL for this page in target language.</summary>
    public string Url { get; set; } = string.Empty;
}
