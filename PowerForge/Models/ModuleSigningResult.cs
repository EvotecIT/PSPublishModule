using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Summary of module code-signing performed during a pipeline run.
/// </summary>
public sealed class ModuleSigningResult
{
    /// <summary>Number of files matched by include patterns before excludes were applied.</summary>
    [JsonPropertyName("totalMatched")]
    public int TotalMatched { get; set; }

    /// <summary>Number of files remaining after excludes were applied.</summary>
    [JsonPropertyName("totalAfterExclude")]
    public int TotalAfterExclude { get; set; }

    /// <summary>Number of files that were already signed by the configured certificate.</summary>
    [JsonPropertyName("alreadySignedByThisCert")]
    public int AlreadySignedByThisCert { get; set; }

    /// <summary>Number of files that were already signed by a different (third-party) certificate.</summary>
    [JsonPropertyName("alreadySignedOther")]
    public int AlreadySignedOther { get; set; }

    /// <summary>Number of files attempted for signing during this run.</summary>
    [JsonPropertyName("attempted")]
    public int Attempted { get; set; }

    /// <summary>Number of previously unsigned files successfully signed.</summary>
    [JsonPropertyName("signedNew")]
    public int SignedNew { get; set; }

    /// <summary>Number of previously signed files successfully re-signed (overwrite mode).</summary>
    [JsonPropertyName("resigned")]
    public int Resigned { get; set; }

    /// <summary>Number of files that failed signing.</summary>
    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    /// <summary>Number of sign operations that returned an "UnknownError" status.</summary>
    [JsonPropertyName("unknownError")]
    public int UnknownError { get; set; }

    /// <summary>Thumbprint of the certificate used for signing (if available).</summary>
    [JsonPropertyName("certificateThumbprint")]
    public string? CertificateThumbprint { get; set; }

    /// <summary>List of file paths that failed signing (truncated).</summary>
    [JsonPropertyName("failedFiles")]
    public string[] FailedFiles { get; set; } = Array.Empty<string>();

    /// <summary>Total number of files already signed (by this cert + third-party).</summary>
    [JsonIgnore]
    public int AlreadySigned => Math.Max(0, AlreadySignedByThisCert + AlreadySignedOther);

    /// <summary>Total number of files successfully signed during this run (new + re-signed).</summary>
    [JsonIgnore]
    public int SignedTotal => Math.Max(0, SignedNew + Resigned);

    /// <summary>True when no signing failures were reported.</summary>
    [JsonIgnore]
    public bool Success => Failed <= 0;

    /// <inheritdoc />
    public override string ToString()
        => $"matched={TotalMatched}, afterExclude={TotalAfterExclude}, alreadySignedOther={AlreadySignedOther}, " +
           $"alreadySignedByThisCert={AlreadySignedByThisCert}, signedNew={SignedNew}, resigned={Resigned}, failed={Failed}";
}
