namespace PowerForge;

/// <summary>
/// Bootstrap/authentication mode used by private gallery onboarding commands.
/// </summary>
public enum PrivateGalleryBootstrapMode
{
    /// <summary>
    /// Choose the best available path: use explicit/prompted credentials when requested, otherwise prefer ExistingSession when Azure Artifacts prerequisites are ready and fall back to CredentialPrompt when they are not.
    /// </summary>
    Auto,
    /// <summary>
    /// Rely on an existing session or credential-provider flow without collecting credentials in the cmdlet.
    /// </summary>
    ExistingSession,
    /// <summary>
    /// Use credentials supplied to the cmdlet or prompt interactively for them.
    /// </summary>
    CredentialPrompt,
    /// <summary>
    /// Use JFrog CLI browser login before probing a JFrog private gallery. This validates whether the local JFrog CLI session can bridge to NuGet/PSResourceGet.
    /// </summary>
    JFrogCli
}
