using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Polymorphic JSON converter for <see cref="IConfigurationSegment"/> values within <see cref="ModulePipelineSpec"/>.
/// The JSON format uses a <c>Type</c> discriminator property matching the legacy PowerShell DSL segment names.
/// </summary>
public sealed class ConfigurationSegmentJsonConverter : JsonConverter<IConfigurationSegment>
{
    /// <inheritdoc />
    public override IConfigurationSegment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Configuration segment must be a JSON object.");

        if (!TryGetProperty(doc.RootElement, "Type", out var typeProp))
            throw new JsonException("Configuration segment is missing required 'Type' discriminator.");

        var type = typeProp.GetString();
        if (string.IsNullOrWhiteSpace(type))
            throw new JsonException("Configuration segment 'Type' discriminator is empty.");

        var discriminator = type!;
        var concreteType = ResolveConcreteType(discriminator);
        var typeInfo = options.GetTypeInfo(concreteType);
        var segment = (IConfigurationSegment?)JsonSerializer.Deserialize(doc.RootElement.GetRawText(), typeInfo);
        if (segment is null)
            throw new JsonException($"Failed to deserialize configuration segment of type '{discriminator}'.");

        ApplyDiscriminatorFixups(discriminator, segment);
        return segment;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IConfigurationSegment value, JsonSerializerOptions options)
    {
        var typeInfo = options.GetTypeInfo(value.GetType());
        JsonSerializer.Serialize(writer, (object)value, typeInfo);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Type ResolveConcreteType(string discriminator)
    {
        if (string.IsNullOrWhiteSpace(discriminator))
            return typeof(object);

        // Fixed segment types
        if (discriminator.Equals("Manifest", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationManifestSegment);
        if (discriminator.Equals("Build", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationBuildSegment);
        if (discriminator.Equals("BuildLibraries", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationBuildLibrariesSegment);
        if (discriminator.Equals("Information", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationInformationSegment);
        if (discriminator.Equals("Options", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationOptionsSegment);
        if (discriminator.Equals("Formatting", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationFormattingSegment);
        if (discriminator.Equals("Documentation", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationDocumentationSegment);
        if (discriminator.Equals("BuildDocumentation", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationBuildDocumentationSegment);
        if (discriminator.Equals("ImportModules", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationImportModulesSegment);
        if (discriminator.Equals("ModuleSkip", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationModuleSkipSegment);
        if (discriminator.Equals("Command", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationCommandSegment);
        if (discriminator.Equals("PlaceHolder", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationPlaceHolderSegment);
        if (discriminator.Equals("PlaceHolderOption", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationPlaceHolderOptionSegment);
        if (discriminator.Equals("Compatibility", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationCompatibilitySegment);
        if (discriminator.Equals("FileConsistency", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationFileConsistencySegment);
        if (discriminator.Equals("TestsAfterMerge", StringComparison.OrdinalIgnoreCase)) return typeof(ConfigurationTestSegment);

        // Dynamic discriminator types (maps to a single concrete segment type)
        if (discriminator.Equals("GalleryNuget", StringComparison.OrdinalIgnoreCase) ||
            discriminator.Equals("GitHubNuget", StringComparison.OrdinalIgnoreCase))
            return typeof(ConfigurationPublishSegment);

        if (Enum.TryParse<ArtefactType>(discriminator, ignoreCase: true, out _))
            return typeof(ConfigurationArtefactSegment);

        if (Enum.TryParse<ModuleDependencyKind>(discriminator, ignoreCase: true, out _))
            return typeof(ConfigurationModuleSegment);

        throw new JsonException($"Unknown configuration segment type '{discriminator}'.");
    }

    private static void ApplyDiscriminatorFixups(string discriminator, IConfigurationSegment segment)
    {
        if (segment is ConfigurationModuleSegment m &&
            Enum.TryParse<ModuleDependencyKind>(discriminator, ignoreCase: true, out var kind))
        {
            m.Kind = kind;
        }

        if (segment is ConfigurationArtefactSegment a &&
            Enum.TryParse<ArtefactType>(discriminator, ignoreCase: true, out var at))
        {
            a.ArtefactType = at;
        }

        if (segment is ConfigurationPublishSegment p)
        {
            p.Configuration ??= new PublishConfiguration();
            if (discriminator.Equals("GitHubNuget", StringComparison.OrdinalIgnoreCase))
                p.Configuration.Destination = PublishDestination.GitHub;
            else if (discriminator.Equals("GalleryNuget", StringComparison.OrdinalIgnoreCase))
                p.Configuration.Destination = PublishDestination.PowerShellGallery;
        }
    }
}
