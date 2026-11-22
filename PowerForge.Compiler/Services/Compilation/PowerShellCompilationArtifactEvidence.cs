namespace PowerForge;

/// <summary>Validates the integrity-bound diagnostic and reproduction evidence in a compilation manifest.</summary>
public static class PowerShellCompilationArtifactEvidence
{
    /// <summary>Validates that the manifest evidence is complete and has not been modified.</summary>
    /// <param name="manifest">The compilation manifest to validate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The manifest evidence is incomplete or fails integrity validation.</exception>
    public static void Validate(PowerShellCompilationArtifactManifest manifest)
        => PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
}
