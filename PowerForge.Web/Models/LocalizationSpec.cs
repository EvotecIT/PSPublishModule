namespace PowerForge.Web;

/// <summary>Localization and multi-language routing configuration.</summary>
public sealed class LocalizationSpec
{
    /// <summary>When true, multi-language routing/runtime is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Default language code (for example "en").</summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>When true, default language URLs also receive a language prefix.</summary>
    public bool PrefixDefaultLanguage { get; set; }

    /// <summary>When true, engine can infer language from first folder segment.</summary>
    public bool DetectFromPath { get; set; } = true;

    /// <summary>
    /// When true, language switcher routes fall back to the default-language page
    /// when the requested translation does not exist.
    /// </summary>
    public bool FallbackToDefaultLanguage { get; set; }

    /// <summary>
    /// When true and <see cref="FallbackToDefaultLanguage"/> is enabled, the engine materializes
    /// fallback pages under each configured language route (for example <c>/pl/projects/...</c>)
    /// using default-language content until native translations are added.
    /// </summary>
    public bool MaterializeFallbackPages { get; set; }

    /// <summary>Configured languages.</summary>
    public LanguageSpec[] Languages { get; set; } = Array.Empty<LanguageSpec>();
}

/// <summary>Single language definition used by localization runtime.</summary>
public sealed class LanguageSpec
{
    /// <summary>Language code (for example "en", "pl").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display label for language switchers.</summary>
    public string? Label { get; set; }

    /// <summary>Optional URL prefix override (defaults to code).</summary>
    public string? Prefix { get; set; }

    /// <summary>Optional absolute base URL override for this language (for example https://evotec.pl).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// When true, public URLs for this language are emitted at the site root of its BaseUrl
    /// instead of under the configured language prefix (for example https://evotec.pl/docs/ instead of https://evotec.pl/pl/docs/).
    /// </summary>
    public bool RenderAtRoot { get; set; }

    /// <summary>Marks language as default.</summary>
    public bool Default { get; set; }

    /// <summary>Marks language as disabled without removing it from config history.</summary>
    public bool Disabled { get; set; }
}
