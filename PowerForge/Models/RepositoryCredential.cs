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
}

