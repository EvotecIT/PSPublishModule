using System.Collections.ObjectModel;

namespace PowerForge;

/// <summary>One minimized native PowerShell case that exercises a promoted semantic feature.</summary>
public sealed class PowerShellCompilationSemanticOracleCase
{
    /// <summary>Creates an immutable semantic-oracle case definition.</summary>
    public PowerShellCompilationSemanticOracleCase(
        string caseId,
        string featureId,
        string sourceResourceName,
        IEnumerable<string> profileIds,
        IEnumerable<string>? arguments = null,
        IEnumerable<string>? observedPropertyNames = null,
        string expectedValue = "42",
        string expectedTypeName = "System.Int32")
    {
        CaseId = Require(caseId, nameof(caseId));
        FeatureId = Require(featureId, nameof(featureId));
        SourceResourceName = Require(sourceResourceName, nameof(sourceResourceName));
        ProfileIds = Normalize(profileIds, nameof(profileIds));
        Arguments = Normalize(arguments ?? Array.Empty<string>(), nameof(arguments));
        ObservedPropertyNames = Normalize(observedPropertyNames ?? Array.Empty<string>(), nameof(observedPropertyNames));
        ExpectedValue = expectedValue ?? string.Empty;
        ExpectedTypeName = Require(expectedTypeName, nameof(expectedTypeName));
    }

    /// <summary>Stable case identity.</summary>
    public string CaseId { get; }
    /// <summary>Canonical compiler feature exercised by the case.</summary>
    public string FeatureId { get; }
    /// <summary>Embedded native script resource.</summary>
    public string SourceResourceName { get; }
    /// <summary>Exact semantic profiles for which this case is applicable.</summary>
    public IReadOnlyList<string> ProfileIds { get; }
    /// <summary>Literal positional arguments passed to the case.</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Portable success properties selected by the case.</summary>
    public IReadOnlyList<string> ObservedPropertyNames { get; }
    /// <summary>Expected single success value.</summary>
    public string ExpectedValue { get; }
    /// <summary>Expected runtime type of the single success value.</summary>
    public string ExpectedTypeName { get; }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values, string parameterName)
        => new ReadOnlyCollection<string>((values ?? throw new ArgumentNullException(parameterName))
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray());

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();
}
