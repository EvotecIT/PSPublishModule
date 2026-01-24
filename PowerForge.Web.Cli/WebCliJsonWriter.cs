using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal sealed class WebCliJsonEnvelope
{
    public int SchemaVersion { get; init; }
    public string Command { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? Error { get; init; }

    public string? Config { get; init; }
    public string? ConfigPath { get; init; }

    public JsonElement? Spec { get; init; }
    public JsonElement? Plan { get; init; }
    public JsonElement? Result { get; init; }
}

internal static class WebCliJsonWriter
{
    internal static void Write(WebCliJsonEnvelope envelope)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("schemaVersion", envelope.SchemaVersion);
            writer.WriteString("command", envelope.Command ?? string.Empty);
            writer.WriteBoolean("success", envelope.Success);
            writer.WriteNumber("exitCode", envelope.ExitCode);

            if (!string.IsNullOrWhiteSpace(envelope.Error))
                writer.WriteString("error", envelope.Error);

            if (!string.IsNullOrWhiteSpace(envelope.Config))
                writer.WriteString("config", envelope.Config);

            if (!string.IsNullOrWhiteSpace(envelope.ConfigPath))
                writer.WriteString("configPath", envelope.ConfigPath);

            WriteElement(writer, "spec", envelope.Spec);
            WriteElement(writer, "plan", envelope.Plan);
            WriteElement(writer, "result", envelope.Result);

            writer.WriteEndObject();
        }

        Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static void WriteElement(Utf8JsonWriter writer, string name, JsonElement? element)
    {
        if (element is null) return;
        writer.WritePropertyName(name);
        element.Value.WriteTo(writer);
    }
}
