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

            if (HasStorySchemaDiscriminator(document.RootElement))
            {
                return true;
            }

            return TryGetProperty(document.RootElement, "schemaVersion", out var schemaVersion) &&
                   schemaVersion.ValueKind == JsonValueKind.Number &&
                   schemaVersion.TryGetInt32(out var version) &&
                   version == 1 &&
                   HasNonWhitespaceString(document.RootElement, "id") &&
                   HasNonWhitespaceString(document.RootElement, "title") &&
                   HasNonWhitespaceString(document.RootElement, "alt") &&
                   HasNonWhitespaceString(document.RootElement, "outcome") &&
                   HasCompletedArtifact(document.RootElement);
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

    private static bool HasStorySchemaDiscriminator(JsonElement root)
        => TryGetProperty(root, "$schema", out var schema) &&
           schema.ValueKind == JsonValueKind.String &&
           (schema.GetString() ?? string.Empty).EndsWith(
               "powerforge.web.visualstory.schema.json",
               StringComparison.OrdinalIgnoreCase);

    private static bool HasNonWhitespaceString(JsonElement root, string name)
        => TryGetProperty(root, name, out var value) &&
           value.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(value.GetString());

    private static bool HasCompletedArtifact(JsonElement root)
    {
        if (!TryGetProperty(root, "artifacts", out var artifacts) ||
            artifacts.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (artifact.ValueKind == JsonValueKind.Object &&
                TryGetProperty(artifact, "role", out var role) &&
                role.ValueKind == JsonValueKind.String &&
                string.Equals(role.GetString(), "completed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
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
