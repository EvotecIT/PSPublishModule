using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ModuleRepositoryProfileDocument))]
internal sealed partial class ModuleRepositoryProfileJson : JsonSerializerContext;
