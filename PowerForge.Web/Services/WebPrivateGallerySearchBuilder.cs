using PowerForge;

namespace PowerForge.Web;

/// <summary>
/// Builds search data from private gallery documents.
/// </summary>
public static class WebPrivateGallerySearchBuilder
{
    /// <summary>
    /// Builds a private gallery search document.
    /// </summary>
    /// <param name="document">Private gallery document.</param>
    /// <returns>Search document.</returns>
    public static WebPrivateGallerySearchDocument Build(PrivateGalleryDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var entries = new List<WebPrivateGallerySearchEntry>();
        foreach (var package in document.Packages)
        {
            var module = package.Module;
            entries.Add(new WebPrivateGallerySearchEntry
            {
                Id = "module:" + package.Name,
                Kind = "module",
                Title = package.Name,
                Module = package.Name,
                Version = package.LatestVersion,
                Summary = package.Description ?? module?.Description,
                Tags = module?.Tags.ToList() ?? new List<string>()
            });

            foreach (var version in package.Versions)
            {
                entries.Add(new WebPrivateGallerySearchEntry
                {
                    Id = "version:" + package.Name + ":" + version.Version,
                    Kind = "version",
                    Title = package.Name + " " + version.Version,
                    Module = package.Name,
                    Version = version.Version,
                    Summary = version.Description ?? package.Description
                });
            }

            if (module is null)
                continue;

            foreach (var command in module.Commands)
            {
                entries.Add(new WebPrivateGallerySearchEntry
                {
                    Id = "command:" + package.Name + ":" + command.Name,
                    Kind = "command",
                    Title = command.Name,
                    Module = package.Name,
                    Version = module.Version ?? package.LatestVersion,
                    Summary = command.Synopsis,
                    Tags = module.Tags.ToList()
                });
            }

            foreach (var asset in module.Documents)
            {
                entries.Add(new WebPrivateGallerySearchEntry
                {
                    Id = "document:" + package.Name + ":" + asset.Path,
                    Kind = "document",
                    Title = asset.Title ?? asset.Path,
                    Module = package.Name,
                    Version = module.Version ?? package.LatestVersion,
                    Summary = asset.Path,
                    Tags = new List<string> { asset.Kind }
                });
            }
        }

        return new WebPrivateGallerySearchDocument
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Entries = entries
                .GroupBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static entry => entry.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
