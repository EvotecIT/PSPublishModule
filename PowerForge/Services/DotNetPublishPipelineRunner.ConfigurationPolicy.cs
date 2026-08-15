using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string ComputePortableConfigurationPolicySha256(
        string targetName,
        DotNetPublishTargetKind targetKind,
        string? bundleId,
        bool zip,
        DotNetPublishSignOptions sign)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            throw new ArgumentException("Portable configuration policy requires a target name.", nameof(targetName));
        if (sign is null) throw new ArgumentNullException(nameof(sign));

        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Target = targetName.Trim(),
            Kind = targetKind.ToString(),
            BundleId = string.IsNullOrWhiteSpace(bundleId) ? null : bundleId!.Trim(),
            Zip = zip,
            SigningEnabled = sign.Enabled,
            Provider = sign.Provider.ToString(),
            sign.IncludeDlls
        });
        using JsonDocument document = JsonDocument.Parse(serialized);
        using var canonical = new MemoryStream();
        using (var writer = new Utf8JsonWriter(canonical))
        {
            WriteCanonicalJson(writer, document.RootElement);
        }

        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(canonical.ToArray()))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
