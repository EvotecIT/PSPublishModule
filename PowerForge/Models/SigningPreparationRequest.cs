namespace PowerForge;

/// <summary>One explicit, local signing-provider preparation operation.</summary>
public sealed class SigningPreparationRequest
{
    /// <summary>Exact approved CurrentUser/My signing certificate thumbprint.</summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>New local directory for the generated signed proof and receipt.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Existing timestamp service used by the signing workflow.</summary>
    public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Optional installed PSPublishModule manifest; otherwise normal module discovery is used.</summary>
    public string? ModulePath { get; set; }
}
