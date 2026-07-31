using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Recognizes visual-story manifests without treating unrelated same-name JSON assets as stories.</summary>
internal static class WebVisualStoryManifestDiscovery
{
    internal static bool IsRecognizable(string manifestPath)
    {
        try
        {
            using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var bytes = new byte[WebVisualStoryStager.MaximumManifestBytes + 1];
            var totalBytes = 0;
            while (totalBytes < bytes.Length)
            {
                var bytesRead = stream.Read(bytes, totalBytes, bytes.Length - totalBytes);
                if (bytesRead == 0)
                    break;
                totalBytes += bytesRead;
            }
            if (totalBytes > WebVisualStoryStager.MaximumManifestBytes || stream.ReadByte() >= 0)
                return false;

            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(bytes, 0, totalBytes),
                new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            return TryGetProperty(document.RootElement, "schemaVersion", out var schemaVersion) &&
                   schemaVersion.ValueKind == JsonValueKind.Number &&
                   schemaVersion.TryGetInt32(out var version) &&
                   version == 1 &&
                   TryGetProperty(document.RootElement, "artifacts", out var artifacts) &&
                   artifacts.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
