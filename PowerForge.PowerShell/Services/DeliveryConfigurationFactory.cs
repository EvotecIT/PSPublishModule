using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class DeliveryConfigurationFactory
{
    public ConfigurationOptionsSegment? Create(DeliveryConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!request.Enable)
            return null;

        return new ConfigurationOptionsSegment
        {
            Options = new ConfigurationOptions
            {
                Delivery = new DeliveryOptionsConfiguration
                {
                    Enable = true,
                    Sign = request.Sign,
                    InternalsPath = request.InternalsPath,
                    IncludeRootReadme = request.IncludeRootReadme,
                    IncludeRootChangelog = request.IncludeRootChangelog,
                    IncludeRootLicense = request.IncludeRootLicense,
                    ReadmeDestination = request.ReadmeDestination,
                    ChangelogDestination = request.ChangelogDestination,
                    LicenseDestination = request.LicenseDestination,
                    ImportantLinks = NormalizeImportantLinks(request.ImportantLinks),
                    IntroText = request.IntroText,
                    UpgradeText = request.UpgradeText,
                    IntroFile = request.IntroFile,
                    UpgradeFile = request.UpgradeFile,
                    RepositoryPaths = request.RepositoryPaths,
                    RepositoryBranch = request.RepositoryBranch,
                    DocumentationOrder = request.DocumentationOrder,
                    PreservePaths = NormalizeStringArray(request.PreservePaths),
                    OverwritePaths = NormalizeStringArray(request.OverwritePaths),
                    GenerateInstallCommand = request.GenerateInstallCommand || !string.IsNullOrWhiteSpace(request.InstallCommandName),
                    GenerateUpdateCommand = request.GenerateUpdateCommand || !string.IsNullOrWhiteSpace(request.UpdateCommandName),
                    InstallCommandName = string.IsNullOrWhiteSpace(request.InstallCommandName) ? null : request.InstallCommandName!.Trim(),
                    UpdateCommandName = string.IsNullOrWhiteSpace(request.UpdateCommandName) ? null : request.UpdateCommandName!.Trim(),
                    Schema = "1.4"
                }
            }
        };
    }

    private static DeliveryImportantLink[]? NormalizeImportantLinks(DeliveryImportantLink[]? links)
    {
        if (links is null || links.Length == 0)
            return null;

        var output = new List<DeliveryImportantLink>();
        foreach (var link in links)
        {
            if (link is null)
                continue;

            if (string.IsNullOrWhiteSpace(link.Title) || string.IsNullOrWhiteSpace(link.Url))
                continue;

            output.Add(new DeliveryImportantLink
            {
                Title = link.Title.Trim(),
                Url = link.Url.Trim()
            });
        }

        return output.Count == 0 ? null : output.ToArray();
    }

    private static string[]? NormalizeStringArray(string[]? values)
    {
        if (values is null || values.Length == 0)
            return null;

        var output = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return output.Length == 0 ? null : output;
    }
}
