using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(DotNetPublishSpec))]
[JsonSerializable(typeof(PowerForgeToolReleaseSpec))]
[JsonSerializable(typeof(PowerForgeInstallerFileComponent))]
[JsonSerializable(typeof(PowerForgeInstallerFolderComponent))]
[JsonSerializable(typeof(PowerForgeInstallerRemoveFolderComponent))]
[JsonSerializable(typeof(PowerForgeInstallerServiceComponent))]
[JsonSerializable(typeof(PowerForgeInstallerRegistryValueComponent))]
[JsonSerializable(typeof(PowerForgeInstallerShortcutComponent))]
internal sealed partial class DotNetPublishConfigurationJsonContext : JsonSerializerContext
{
}
