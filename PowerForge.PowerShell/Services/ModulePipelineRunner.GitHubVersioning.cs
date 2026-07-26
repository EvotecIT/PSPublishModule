using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private string ResolveGitHubReleaseVersion(
        string expectedVersion,
        string candidateVersion,
        IReadOnlyList<ConfigurationPublishSegment> publishes,
        string projectRoot,
        string moduleName,
        string? preRelease)
    {
        var gitHubPublishes = (publishes ?? Array.Empty<ConfigurationPublishSegment>())
            .Where(static segment => segment?.Configuration is
            {
                Enabled: true,
                Destination: PublishDestination.GitHub
            })
            .Select(static segment => segment.Configuration)
            .ToArray();
        if (gitHubPublishes.Length == 0)
            return candidateVersion;

        var candidate = candidateVersion;
        for (var pass = 0; pass < 24; pass++)
        {
            var before = candidate;
            foreach (var publish in gitHubPublishes)
            {
                candidate = _gitHubVersionAvailabilityResolver(
                    expectedVersion,
                    candidate,
                    publish,
                    projectRoot,
                    moduleName,
                    preRelease);
            }

            if (string.Equals(before, candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new InvalidOperationException(
            $"GitHub release version coordination did not stabilize after 24 passes for module '{moduleName}'.");
    }
}
