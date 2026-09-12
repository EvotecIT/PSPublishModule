using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    // Inspect dotnet's installation artifacts without running an unrequested command or parsing console tables.
    private static async Task ValidateInstalledToolAsync(DotNetToolValidation spec, string version, string workspace,
        string toolRoot, bool manifestInstall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!manifestInstall)
        {
            var shim = Within(toolRoot, spec.CommandName + (FrameworkCompatibility.IsWindows() ? ".exe" : string.Empty));
            FileSystemPathSafety.RejectReparsePoints(shim, workspace, "Installed tool command");
            if (!File.Exists(shim) || new FileInfo(shim).Length == 0)
                throw new InvalidOperationException($"Installed tool '{spec.PackageId}' did not provide command '{spec.CommandName}'.");
            return;
        }

        // SDK templates use either a root manifest or the older hidden .config location.
        var manifests = new[] { Path.Combine(workspace, "dotnet-tools.json"), Path.Combine(workspace, ".config", "dotnet-tools.json") }
            .Where(File.Exists).ToArray();
        if (manifests.Length != 1) throw new InvalidOperationException("Installed tool manifest is missing or ambiguous.");
        var path = manifests[0];
        FileSystemPathSafety.RejectReparsePoints(path, workspace, "Installed tool manifest");
        using var manifest = JsonDocument.Parse(await DotNetPublishReleaseArtifactVerifier.ReadBoundedTextAsync(path,
            "Installed tool manifest", DotNetPublishReleaseArtifactVerifier.MaxManifestBytes, cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        if (GetProperty(manifest.RootElement, "tools", out var tools) && tools.ValueKind == JsonValueKind.Object)
        {
            var packages = tools.EnumerateObject().Where(item => string.Equals(item.Name, spec.PackageId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (packages.Length == 1 && NuGetVersion.TryParse(Text(packages[0].Value, "version"), out var installedVersion) &&
                installedVersion == NuGetVersion.Parse(version) &&
                GetProperty(packages[0].Value, "commands", out var commands) && commands.ValueKind == JsonValueKind.Array &&
                commands.EnumerateArray().Any(command => command.ValueKind == JsonValueKind.String &&
                    string.Equals(command.GetString(), spec.CommandName, StringComparison.Ordinal)))
                return;
        }
        throw new InvalidOperationException($"Installed tool manifest does not declare '{spec.PackageId}/{version}' command '{spec.CommandName}'.");
    }
}
