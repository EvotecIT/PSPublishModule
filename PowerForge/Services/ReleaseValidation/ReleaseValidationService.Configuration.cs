using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    /// <summary>Loads a bounded, BOM-aware JSON validation contract.</summary>
    public static ReleaseValidationSpec Load(string configPath)
        => LoadAsync(configPath).GetAwaiter().GetResult();

    /// <summary>Loads a bounded JSON validation contract, passing cancellation through file reads and deserialization.</summary>
    /// <param name="configPath">Path to the validation configuration.</param>
    /// <param name="cancellationToken">Cancels reading or parsing the configuration.</param>
    /// <returns>The typed validation contract.</returns>
    public static async Task<ReleaseValidationSpec> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var json = await DotNetPublishReleaseArtifactVerifier.ReadBoundedTextAsync(configPath,
            "Release validation configuration", DotNetPublishReleaseArtifactVerifier.MaxConfigurationBytes,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        var spec = await JsonSerializer.DeserializeAsync(input, ReleaseValidationJsonContext.Default.ReleaseValidationSpec,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return spec ?? throw new InvalidOperationException("Validation configuration is empty.");
    }
}
