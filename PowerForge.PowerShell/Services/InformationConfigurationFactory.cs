using System.Linq;

namespace PowerForge;

internal sealed class InformationConfigurationFactory
{
    public ConfigurationInformationSegment Create(InformationConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return new ConfigurationInformationSegment
        {
            Configuration = new InformationConfiguration
            {
                FunctionsToExportFolder = request.FunctionsToExportFolder,
                AliasesToExportFolder = request.AliasesToExportFolder,
                ExcludeFromPackage = request.ExcludeFromPackage,
                IncludeRoot = request.IncludeRoot,
                IncludePS1 = request.IncludePS1,
                IncludeAll = request.IncludeAll,
                IncludeCustomCode = request.IncludeCustomCode,
                IncludeToArray = NormalizeIncludeToArray(request.IncludeToArray),
                LibrariesCore = request.LibrariesCore,
                LibrariesDefault = request.LibrariesDefault,
                LibrariesStandard = request.LibrariesStandard
            }
        };
    }

    private static IncludeToArrayEntry[]? NormalizeIncludeToArray(IncludeToArrayEntry[]? input)
    {
        if (input is null || input.Length == 0)
            return null;

        var output = new List<IncludeToArrayEntry>();
        foreach (var entry in input)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Key))
                continue;

            var values = (entry.Values ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToArray();

            if (values.Length == 0)
                continue;

            output.Add(new IncludeToArrayEntry
            {
                Key = entry.Key.Trim(),
                Values = values
            });
        }

        return output.Count == 0 ? null : output.ToArray();
    }
}
