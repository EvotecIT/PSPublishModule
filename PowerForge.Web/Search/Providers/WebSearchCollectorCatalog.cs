using System.Collections.Frozen;

namespace PowerForge.Web;

/// <summary>Collector capabilities built into the current PowerForge.Web assembly.</summary>
public static class WebSearchCollectorCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Capabilities =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [GoogleSearchConsoleCollector.ProviderKind] = GoogleSearchConsoleCollector.AvailableCapabilities
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            [BingWebmasterCollector.ProviderKind] = BingWebmasterCollector.AvailableCapabilities
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            [BingWebmasterCsvExportParser.ProviderKind] = BingWebmasterCsvExportParser.AvailableCapabilities
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Implemented capabilities keyed by provider kind.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AvailableCapabilities => Capabilities;
}
