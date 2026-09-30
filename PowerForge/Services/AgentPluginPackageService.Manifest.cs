using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AgentPluginPackageService
{
    private const string ManifestSchema = "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json";
    private const string McpSchema = "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json";
    private static readonly string[] CommonFields = { "name", "version", "description", "author", "homepage", "repository", "license", "keywords" };

    private static JsonDocument ParseJson(string path)
    {
        var document = JsonDocument.Parse(File.ReadAllText(path));
        try { RejectDuplicateKeys(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicateKeys(value);
    }

    private static void ValidateManifest(JsonElement manifest)
    {
        RequireFields(manifest, CommonFields.Concat(new[] { "$schema", "extensions" }).ToArray());
        if (RequiredString(manifest, "$schema") != ManifestSchema) throw new InvalidDataException("Only Agent Plugins 1.0.0 is supported.");
        var name = RequiredString(manifest, "name");
        if (name.Length > 64 || !NamePattern.IsMatch(name)) throw new InvalidDataException("Invalid portable plugin name.");
        var version = RequiredString(manifest, "version");
        var withoutMetadata = version.Split('+')[0];
        var prereleaseIndex = withoutMetadata.IndexOf('-');
        if (!VersionPattern.IsMatch(version) || (prereleaseIndex >= 0 && withoutMetadata.Substring(prereleaseIndex + 1).Split('.').Any(p => p.All(char.IsDigit) && p.Length > 1 && p[0] == '0')))
            throw new InvalidDataException("Packing requires a three-part semantic version.");
        foreach (var field in new[] { "description", "homepage", "repository", "license" }) OptionalString(manifest, field);
        foreach (var field in new[] { "homepage", "repository" }) OptionalWebUrl(manifest, field);
        OptionalStringArray(manifest, "keywords");
        if (manifest.TryGetProperty("author", out var author))
        {
            RequireFields(author, new[] { "name", "email", "url" });
            RequiredString(author, "name");
            foreach (var field in new[] { "name", "email", "url" }) OptionalString(author, field);
            OptionalWebUrl(author, "url");
        }
        if (manifest.TryGetProperty("extensions", out var extensions))
        {
            RequireObject(extensions, "extensions");
            foreach (var extension in extensions.EnumerateObject()) RequireObject(extension.Value, extension.Name);
            if (extensions.TryGetProperty("com.openai", out var openai))
            {
                // This profile uses portable fixed component locations. Conflicting legacy overrides
                // would make installation depend on which manifest a client selects.
                foreach (var field in CommonFields.Concat(new[] { "skills", "mcpServers", "$schema", "extensions" }))
                    if (openai.TryGetProperty(field, out _)) throw new InvalidDataException("Portable profile cannot override " + field + " in com.openai.");
            }
        }
    }

    private static void GenerateCompatibility(JsonElement manifest, JsonElement? mcp, Dictionary<string, byte[]> output)
    {
        output[".claude-plugin/plugin.json"] = WriteJson(writer =>
        {
            writer.WriteStartObject();
            WriteCommonFields(writer, manifest);
            writer.WriteEndObject();
        });
        output[".codex-plugin/plugin.json"] = WriteJson(writer =>
        {
            writer.WriteStartObject();
            WriteCommonFields(writer, manifest);
            if (manifest.TryGetProperty("extensions", out var extensions) && extensions.TryGetProperty("com.openai", out var openai))
                foreach (var property in openai.EnumerateObject()) property.WriteTo(writer);
            writer.WriteString("skills", "./skills/");
            if (mcp.HasValue) writer.WriteString("mcpServers", "./.codex-plugin/mcp.json");
            writer.WriteEndObject();
        });
        if (mcp.HasValue)
        {
            output[".mcp.json"] = GenerateLegacyMcp(mcp.Value, "${CLAUDE_PLUGIN_ROOT}");
            output[".codex-plugin/mcp.json"] = GenerateLegacyMcp(mcp.Value, "${PLUGIN_ROOT}");
        }
    }

    private static void WriteCommonFields(Utf8JsonWriter writer, JsonElement manifest)
    {
        foreach (var field in CommonFields)
            if (manifest.TryGetProperty(field, out var value)) { writer.WritePropertyName(field); value.WriteTo(writer); }
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) write(writer);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string RequiredString(JsonElement element, string field)
    {
        if (!element.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Missing or invalid string: " + field);
        return value.GetString()!;
    }

    private static void OptionalString(JsonElement element, string field)
    {
        if (element.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Expected string: " + field);
    }

    private static void OptionalWebUrl(JsonElement element, string field)
    {
        if (element.TryGetProperty(field, out var value) &&
            (!Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.Scheme == "http") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidDataException("Compatibility metadata requires an absolute HTTP(S) URL: " + field);
    }

    private static void OptionalStringArray(JsonElement element, string field)
    {
        if (element.TryGetProperty(field, out var value) && (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String)))
            throw new InvalidDataException("Expected string array: " + field);
    }

    private static void RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected object: " + label);
    }

    private static void RequireFields(JsonElement element, string[] allowed)
    {
        RequireObject(element, "configuration");
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidDataException("Unsupported JSON property: " + property.Name);
    }
}
