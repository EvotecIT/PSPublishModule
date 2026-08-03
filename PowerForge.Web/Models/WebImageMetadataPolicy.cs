using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Controls how image optimization handles metadata on original image files.</summary>
[JsonConverter(typeof(WebImageMetadataPolicyJsonConverter))]
public enum WebImageMetadataPolicy {
    /// <summary>
    /// Preserve the original file byte for byte. Generated variants are derivatives and do not inherit a valid C2PA signature.
    /// </summary>
    Preserve,

    /// <summary>Remove all metadata understood by the image encoder and always rewrite the original file.</summary>
    StripAll
}

/// <summary>Serializes image metadata policies using publish-spec camelCase values.</summary>
public sealed class WebImageMetadataPolicyJsonConverter : JsonStringEnumConverter<WebImageMetadataPolicy> {
    /// <summary>Initializes a camelCase image metadata policy converter.</summary>
    public WebImageMetadataPolicyJsonConverter()
        : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) {
    }
}