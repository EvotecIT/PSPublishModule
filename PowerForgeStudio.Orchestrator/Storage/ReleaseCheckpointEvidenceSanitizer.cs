using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Storage;

/// <summary>Removes URL credentials from durable queue checkpoints, including nested source checkpoints.</summary>
internal static class ReleaseCheckpointEvidenceSanitizer
{
    internal static string? Sanitize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            var node = JsonNode.Parse(json);
            var changed = false;
            var safe = SanitizeNode(node, ref changed);
            return changed ? safe?.ToJsonString() ?? "null" : json;
        }
        catch (JsonException)
        {
            // Invalid checkpoints cannot be replayed, but must not retain a readable URL secret.
            return StudioOutputSanitizer.SanitizeAddressText(json);
        }
    }

    private static JsonNode? SanitizeNode(JsonNode? node, ref bool changed)
    {
        if (node is JsonObject objectNode)
        {
            if (objectNode.TryGetPropertyValue("Destination", out var destinationNode) &&
                destinationNode is JsonValue destinationValue &&
                destinationValue.TryGetValue<string>(out var destination) &&
                StudioOutputSanitizer.DestinationCredentialsOmitted(destination))
            {
                objectNode["DestinationCredentialsOmitted"] = true;
                changed = true;
            }

            foreach (var key in objectNode.Select(static entry => entry.Key).ToArray())
            {
                if (key.Equals("Destination", StringComparison.OrdinalIgnoreCase) &&
                    objectNode[key] is JsonValue addressValue &&
                    addressValue.TryGetValue<string>(out var address))
                {
                    var safeAddress = StudioOutputSanitizer.SanitizeDestination(address);
                    if (safeAddress != address)
                    {
                        objectNode[key] = safeAddress;
                        changed = true;
                    }
                }
                else
                {
                    var original = objectNode[key];
                    var safe = SanitizeNode(original, ref changed);
                    if (!ReferenceEquals(original, safe)) objectNode[key] = safe;
                }
            }
            return objectNode;
        }

        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var original = array[index];
                var safe = SanitizeNode(original, ref changed);
                if (!ReferenceEquals(original, safe)) array[index] = safe;
            }
            return array;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text) && text is not null)
        {
            string safeText;
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
                safeText = Sanitize(text) ?? text;
            else
                safeText = StudioOutputSanitizer.SanitizeAddressText(text);
            if (safeText == text) return node;
            changed = true;
            return JsonValue.Create(safeText);
        }
        return node;
    }
}
