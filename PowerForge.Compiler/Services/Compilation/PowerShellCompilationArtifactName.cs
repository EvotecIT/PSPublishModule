namespace PowerForge;

/// <summary>Keeps shipped artifact basenames identical across direct builds and project receipt validation.</summary>
internal static class PowerShellCompilationArtifactName
{
    /// <summary>Applies the builder's filename normalization and rejects names that overlap generated or publisher-owned files.</summary>
    internal static string Normalize(string value, string parameterName)
    {
        var sanitized = new string(value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            throw new ArgumentException("Artifact name does not contain a usable file name.", parameterName);
        if (new[] { ".exe", ".dll", ".pdb", ".generated", ".powerforge-compilation.json" }
            .Any(suffix => sanitized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Artifact name must not end with a generated artifact suffix because it can overlap another artifact set.", parameterName);
        PowerShellArtifactSetPublisher.EnsureArtifactNameIsNotReserved(sanitized, parameterName);
        return sanitized;
    }

    /// <summary>Resolves the shipped basename without changing authored project/target lookup names or distinct variant package names.</summary>
    internal static string ResolveProjectArtifactName(
        PowerShellCompilationProjectManifest manifest,
        PowerShellCompilationProjectArtifact artifact)
        => Normalize(artifact.ArtifactName ?? manifest.Name + "." + artifact.Name, nameof(artifact.ArtifactName));
}
