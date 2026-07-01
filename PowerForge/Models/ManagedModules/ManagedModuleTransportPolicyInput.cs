namespace PowerForge;

/// <summary>
/// Inputs used to decide whether module delivery should use the managed engine or compatibility transport.
/// </summary>
public sealed class ManagedModuleTransportPolicyInput
{
    /// <summary>
    /// Requested delivery transport.
    /// </summary>
    public ModuleStateDeliveryTransport RequestedTransport { get; set; } = ModuleStateDeliveryTransport.PrivateModule;

    /// <summary>
    /// True when the request is using a private-gallery provider profile.
    /// </summary>
    public bool UsesPrivateGalleryProvider { get; set; }

    /// <summary>
    /// Private-gallery provider associated with the request.
    /// </summary>
    public PrivateGalleryProvider PrivateGalleryProvider { get; set; } = PrivateGalleryProvider.AzureArtifacts;

    /// <summary>
    /// True when the existing Microsoft Artifact Registry workflow owns repository setup.
    /// </summary>
    public bool UsesMicrosoftArtifactRegistry { get; set; }

    /// <summary>
    /// True when a repository source URI or local feed path is available to the managed engine.
    /// </summary>
    public bool HasRepositorySource { get; set; }

    /// <summary>
    /// True when the caller requested options that only the managed engine supports.
    /// </summary>
    public bool HasManagedOnlyOptions { get; set; }
}
