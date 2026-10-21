namespace PowerForge;

/// <summary>
/// Plain credential representation used for out-of-process PowerShell repository operations.
/// </summary>
public sealed class RepositoryCredential
{
    /// <summary>Username for basic authentication.</summary>
    public string? UserName { get; set; }

    /// <summary>Password or token for basic authentication.</summary>
    public string? Secret { get; set; }

    /// <summary>Secret file read only when a repository operation needs credentials.</summary>
    public string? SecretFilePath { get; set; }

    /// <summary>Process environment variable read only when a repository operation needs credentials.</summary>
    public string? SecretEnvironmentVariable { get; set; }
}

