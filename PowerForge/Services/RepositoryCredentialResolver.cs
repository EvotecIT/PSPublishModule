namespace PowerForge;

/// <summary>Resolves deferred repository secrets at the repository-operation boundary.</summary>
internal static class RepositoryCredentialResolver
{
    internal static bool HasSecretSource(RepositoryCredential? credential)
        => credential is not null &&
           (!string.IsNullOrWhiteSpace(credential.Secret) ||
            !string.IsNullOrWhiteSpace(credential.SecretFilePath) ||
            !string.IsNullOrWhiteSpace(credential.SecretEnvironmentVariable));

    internal static RepositoryCredential? Resolve(RepositoryCredential? credential)
    {
        if (credential is null)
            return null;
        if (string.IsNullOrWhiteSpace(credential.SecretFilePath) &&
            string.IsNullOrWhiteSpace(credential.SecretEnvironmentVariable))
            return credential;

        string? secret;
        if (!string.IsNullOrWhiteSpace(credential.SecretFilePath))
        {
            try { secret = File.ReadAllText(credential.SecretFilePath!.Trim()).Trim(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                throw new InvalidOperationException("Repository credential secret file could not be read. Check RepositoryCredentialSecretFilePath and access permissions.");
            }
        }
        else
        {
            secret = Environment.GetEnvironmentVariable(credential.SecretEnvironmentVariable!.Trim())?.Trim();
        }
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Repository credential secret could not be resolved. Check RepositoryCredentialSecretFilePath or RepositoryCredentialSecretEnvironmentVariable in the publishing process.");
        if (secret!.Contains('\r') || secret.Contains('\n'))
            throw new InvalidOperationException("Repository credential source resolved to a multi-line secret. Repository credential secrets must be a single line.");

        // Return a fresh value; never persist a resolved secret into the build configuration.
        return new RepositoryCredential { UserName = credential.UserName, Secret = secret };
    }
}
