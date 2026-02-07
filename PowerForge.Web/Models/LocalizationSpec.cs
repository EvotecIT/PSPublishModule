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

    /// <summary>Marks language as default.</summary>
    public bool Default { get; set; }

    /// <summary>Marks language as disabled without removing it from config history.</summary>
    public bool Disabled { get; set; }
}
