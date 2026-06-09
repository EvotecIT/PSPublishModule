using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Deserializes typed installer components from JSON using a small <c>Type</c> discriminator.
/// </summary>
public sealed class PowerForgeInstallerComponentJsonConverter : JsonConverter<PowerForgeInstallerComponent>
{
    /// <inheritdoc />
    public override PowerForgeInstallerComponent Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var type = ReadComponentType(document.RootElement);
        var json = document.RootElement.GetRawText();

        return type switch
        {
            "File" => JsonSerializer.Deserialize<PowerForgeInstallerFileComponent>(json, options)!,
            "Folder" => JsonSerializer.Deserialize<PowerForgeInstallerFolderComponent>(json, options)!,
            "RemoveFolder" => JsonSerializer.Deserialize<PowerForgeInstallerRemoveFolderComponent>(json, options)!,
            "Service" => JsonSerializer.Deserialize<PowerForgeInstallerServiceComponent>(json, options)!,
            "RegistryValue" => JsonSerializer.Deserialize<PowerForgeInstallerRegistryValueComponent>(json, options)!,
            "Shortcut" => JsonSerializer.Deserialize<PowerForgeInstallerShortcutComponent>(json, options)!,
            _ => throw new JsonException($"Unsupported installer component Type '{type}'.")
        };
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        PowerForgeInstallerComponent value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var type = value switch
        {
            PowerForgeInstallerFileComponent => "File",
            PowerForgeInstallerFolderComponent => "Folder",
            PowerForgeInstallerRemoveFolderComponent => "RemoveFolder",
            PowerForgeInstallerServiceComponent => "Service",
            PowerForgeInstallerRegistryValueComponent => "RegistryValue",
            PowerForgeInstallerShortcutComponent => "Shortcut",
            _ => throw new JsonException($"Unsupported installer component type '{value.GetType().FullName}'.")
        };

        var json = JsonSerializer.Serialize(value, value.GetType(), options);
        using var document = JsonDocument.Parse(json);

        writer.WriteStartObject();
        writer.WriteString("Type", type);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "Type", StringComparison.OrdinalIgnoreCase))
                continue;

            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static string ReadComponentType(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, "Type", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value!.Trim();
        }

        var available = string.Join(", ", element.EnumerateObject().Select(p => p.Name));
        throw new JsonException(
            string.IsNullOrWhiteSpace(available)
                ? "Installer component requires a Type discriminator."
                : $"Installer component requires a Type discriminator. Available properties: {available}.");
    }
}
