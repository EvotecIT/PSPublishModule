using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(PowerForgeReleaseAssetEntry[]))]
[JsonSerializable(typeof(StoreSubmissionDesktopPackage[]))]
internal partial class ReleaseCatalogJsonContext : JsonSerializerContext;
