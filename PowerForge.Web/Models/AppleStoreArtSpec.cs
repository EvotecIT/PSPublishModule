namespace PowerForge.Web;

/// <summary>Offline composition of real captures into captioned store frames.</summary>
public sealed class AppleStoreArtSpec
{
    /// <summary>Optional JSON editor schema URI; ignored by composition.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("$schema")]
    public string? Schema { get; set; }
    /// <summary>Spec schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Shipped brand theme and optional CSS token overrides.</summary>
    public AppleStoreArtTheme Theme { get; set; } = new();
    /// <summary>Listing locales with required store-captions.&lt;locale&gt;.json files.</summary>
    public string[] Locales { get; set; } = ["en-US"];
    /// <summary>Directory containing caption files, relative to this spec.</summary>
    public string CaptionsPath { get; set; } = ".";
    /// <summary>Root containing all raw captures. Lineage paths are relative to this root.</summary>
    public string CaptureRoot { get; set; } = ".";
    /// <summary>Named scenario captures; locale-specific keys use scenario@locale.</summary>
    public Dictionary<string, AppleStoreArtCapture> Captures { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Output device keys; supported keys are listed by <see cref="AppleStoreArtComposer.Displays"/>.</summary>
    public string[] Devices { get; set; } = ["iphone-69", "iphone-63"];
    /// <summary>Frames in listing order.</summary>
    public AppleStoreArtFrame[] Frames { get; set; } = [];
}

/// <summary>Theme tokens layered over a shipped template, without executable CSS.</summary>
public sealed class AppleStoreArtTheme
{
    /// <summary>casaray, tactra, or officeimo.</summary>
    public string Name { get; set; } = "casaray";
    /// <summary>CSS custom property values, keyed by their --token names.</summary>
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>A real capture and its optional scenario identity.</summary>
public sealed class AppleStoreArtCapture
{
    /// <summary>PNG path relative to CaptureRoot; {locale} is expanded.</summary>
    public string File { get; set; } = "";
    /// <summary>Deterministic application scenario.</summary>
    public string? Scenario { get; set; }
    /// <summary>Capture locale, when known. Must match the listing locale.</summary>
    public string? Locale { get; set; }
}

/// <summary>One designed frame; dimensions and device geometry scale together.</summary>
public sealed class AppleStoreArtFrame
{
    /// <summary>Stable, unique file-safe frame id.</summary>
    public string Id { get; set; } = "";
    /// <summary>hero, focus, pair, surfaces, statement, ipad, mac, or watch.</summary>
    public string Layout { get; set; } = "hero";
    /// <summary>Optional subset of output device keys for this frame.</summary>
    public string[] Devices { get; set; } = [];
    /// <summary>PNG path or scenario id in Captures.</summary>
    public string Capture { get; set; } = "";
    /// <summary>Second real capture for pair layout (Watch).</summary>
    public string? Watch { get; set; }
    /// <summary>Additional real captures for surfaces layout.</summary>
    public string[] Surfaces { get; set; } = [];
    /// <summary>Caption key in the locale file.</summary>
    public string? HeadlineKey { get; set; }
    /// <summary>Optional eyebrow caption key.</summary>
    public string? EyebrowKey { get; set; }
    /// <summary>Optional subline caption key.</summary>
    public string? SublineKey { get; set; }
    /// <summary>light or dark variant, or empty for the theme default.</summary>
    public string Mood { get; set; } = "";
    /// <summary>Device width in prototype canvas pixels (1320 phone; 2064 tablet; 2880 Mac or 1440 compact Mac).</summary>
    public double? DeviceWidth { get; set; }
    /// <summary>Optional vertical device translation as a fraction of canvas width; zero keeps the whole device raised.</summary>
    public double? Lift { get; set; }
    /// <summary>Watch width on the 1320 canvas.</summary>
    public double WatchWidth { get; set; } = 560;
    /// <summary>Draw a synthetic island only when the capture does not already include one.</summary>
    public bool Island { get; set; } = true;
    /// <summary>Magnified region of the unchanged primary capture.</summary>
    public AppleStoreArtLens? Lens { get; set; }
}

/// <summary>Source rectangle and positioning of a real capture crop.</summary>
public sealed class AppleStoreArtLens
{
    /// <summary>x, y, width, height in raw capture pixels.</summary>
    public double[] Rect { get; set; } = [];
    /// <summary>Magnification relative to the device screen.</summary>
    public double Scale { get; set; } = 1.6;
    /// <summary>Optional x/y offset on the 1320 canvas.</summary>
    public double[] At { get; set; } = [0, 0];
}

/// <summary>Exact output geometry, independent of App Store Connect display-type availability.</summary>
public sealed record AppleStoreArtDisplay(int Width, int Height);
