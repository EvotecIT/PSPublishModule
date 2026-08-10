using System.Text.Json;

namespace PowerForge.Web;

internal static class WebPerformanceJsonBoundary
{
    internal static void ValidateNoDuplicateObjectMembers(JsonElement value, string label)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new ArgumentException($"{label} contains duplicate JSON member '{property.Name}'.", nameof(value));
                    ValidateNoDuplicateObjectMembers(property.Value, label);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    ValidateNoDuplicateObjectMembers(item, label);
                break;
        }
    }
}
