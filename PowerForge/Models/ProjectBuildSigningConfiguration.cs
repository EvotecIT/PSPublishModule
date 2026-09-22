namespace PowerForge;

/// <summary>Non-secret certificate selection declared by a project build configuration.</summary>
public sealed record ProjectBuildSigningConfiguration(
    string? CertificateThumbprint,
    string? CertificateStore,
    string? TimeStampServer);
