using System.Text.Json;

namespace PowerForge;

public sealed partial class AgentPluginPackageService
{
    private static void ValidateMcp(JsonElement configuration, string root)
    {
        RequireFields(configuration, new[] { "$schema", "mcpServers" });
        if (RequiredString(configuration, "$schema") != McpSchema) throw new InvalidDataException("MCP configuration must target Agent Plugins 1.0.0.");
        if (!configuration.TryGetProperty("mcpServers", out var servers)) throw new InvalidDataException("Missing mcpServers.");
        RequireObject(servers, "mcpServers");
        foreach (var server in servers.EnumerateObject())
        {
            var value = server.Value;
            RequireObject(value, server.Name);
            var type = RequiredString(value, "type");
            if (type == "stdio")
            {
                RequireFields(value, new[] { "type", "command", "args", "env", "cwd" });
                var command = RequiredString(value, "command");
                if (command.StartsWith("./", StringComparison.Ordinal))
                {
                    var executable = Path.GetFullPath(Path.Combine(root, command.Substring(2)));
                    if (!IsWithin(executable, root) || !File.Exists(executable)) throw new InvalidDataException("Missing or escaping plugin executable: " + command);
                    RejectLinkedAncestors(executable);
                }
                else if (command.Any(char.IsWhiteSpace) || command.IndexOfAny(new[] { '/', '\\', ':', '$', '"', '\'' }) >= 0)
                    throw new InvalidDataException("MCP command must be a bare executable token or ./plugin-relative path.");
                OptionalStringArray(value, "args");
                ValidateStringMap(value, "env", reserved: true);
                if (value.TryGetProperty("cwd", out _))
                    throw new InvalidDataException("Explicit cwd is not supported by the Claude compatibility profile. Use a server that does not depend on its working directory.");
                if (value.TryGetProperty("args", out var args))
                    foreach (var arg in args.EnumerateArray()) RejectPluginData(arg.GetString()!);
                if (value.TryGetProperty("env", out var env))
                    foreach (var entry in env.EnumerateObject()) RejectPluginData(entry.Value.GetString()!);
            }
            else if (type == "streamable-http" || type == "sse")
            {
                RequireFields(value, new[] { "type", "url", "headers" });
                var url = RequiredString(value, "url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.Scheme == "http") || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidDataException("MCP URL must be an absolute HTTP(S) endpoint without embedded credentials.");
                ValidateStringMap(value, "headers", reserved: false);
            }
            else throw new InvalidDataException("Unsupported MCP transport: " + type);
        }
    }

    private static void RejectPluginData(string value)
    {
        if (value.Contains("${PLUGIN_DATA}")) throw new InvalidDataException("PLUGIN_DATA is not supported by the Claude compatibility profile.");
    }

    private static void ValidateStringMap(JsonElement element, string field, bool reserved)
    {
        if (!element.TryGetProperty(field, out var map)) return;
        RequireObject(map, field);
        foreach (var property in map.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || (reserved && (property.Name == "PLUGIN_ROOT" || property.Name == "PLUGIN_DATA")))
                throw new InvalidDataException("Invalid MCP " + field + " entry: " + property.Name);
        }
    }

    private static byte[] GenerateLegacyMcp(JsonElement mcp, string rootVariable) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteStartObject("mcpServers");
        foreach (var server in mcp.GetProperty("mcpServers").EnumerateObject())
        {
            writer.WriteStartObject(server.Name);
            var type = RequiredString(server.Value, "type");
            foreach (var property in server.Value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name == "type") writer.WriteStringValue(type == "streamable-http" ? "http" : type);
                else if (property.Name == "command" && property.Value.GetString()!.StartsWith("./", StringComparison.Ordinal))
                    writer.WriteStringValue(rootVariable + "/" + property.Value.GetString()!.Substring(2));
                else if (property.Name == "args")
                {
                    writer.WriteStartArray();
                    foreach (var arg in property.Value.EnumerateArray()) writer.WriteStringValue(LegacyRoot(arg.GetString()!, rootVariable));
                    writer.WriteEndArray();
                }
                else if (property.Name == "env")
                {
                    writer.WriteStartObject();
                    foreach (var env in property.Value.EnumerateObject()) writer.WriteString(env.Name, LegacyRoot(env.Value.GetString()!, rootVariable));
                    writer.WriteEndObject();
                }
                else property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static string LegacyRoot(string value, string rootVariable) => value.Replace("${PLUGIN_ROOT}", rootVariable);
}
