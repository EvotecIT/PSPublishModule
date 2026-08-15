namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private static Func<string, DotNetPublishReleaseArtifactVerifier.AuthenticodeResult> CreateDefaultAuthenticodeVerifier()
    {
        if (!FrameworkCompatibility.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Release artifact Authenticode verification is currently supported only on Windows.");
        }
        return DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode;
    }

    private static void ValidatePortableSourceBinding(string? productVersion, string expectedRevision)
    {
        string value = DotNetPublishReleaseArtifactVerifier.RequireText(productVersion, "signed portable product version");
        int separator = value.IndexOf('+');
        string[] metadata = separator < 0
            ? Array.Empty<string>()
            : value.Substring(separator + 1).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (!metadata.Contains(expectedRevision, StringComparer.OrdinalIgnoreCase))
        {
            throw Invalid(
                "Publisher-signed portable product version does not bind the expected full source revision.");
        }
    }
}
