using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(PowerForgeReleaseAssetEntry[]))]
[JsonSerializable(typeof(StoreSubmissionDesktopPackage[]))]
[JsonSerializable(typeof(CatalogUpdateSpec))]
[JsonSerializable(typeof(CatalogUpdateReceipt))]
internal partial class ReleaseCatalogJsonContext : JsonSerializerContext;
