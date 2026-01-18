namespace PowerForge.Blazor;

/// <summary>
/// Defines a source of documentation pages. Implementations can load from XML docs,
/// markdown folders, PowerShell MAML files, or custom sources.
/// </summary>
public interface IDocumentationSource
{
    /// <summary>
    /// Unique identifier for this documentation source (e.g., "api", "guides", "about").
    /// Used for routing and navigation.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name for the documentation source (e.g., "API Reference", "Guides").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Optional description of this documentation source.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Sort order for navigation. Lower values appear first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Loads all documentation pages from this source.
    /// </summary>
    Task<IReadOnlyList<DocPage>> LoadPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific page by its slug/path.
    /// </summary>
    Task<DocPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the navigation tree for this source.
    /// </summary>
    Task<DocNavigation> GetNavigationAsync(CancellationToken cancellationToken = default);
}
