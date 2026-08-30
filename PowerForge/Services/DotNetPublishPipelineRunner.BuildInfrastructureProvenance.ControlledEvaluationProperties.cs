namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static IReadOnlyDictionary<string, string> CreateControlledEvaluationProperties(
        IReadOnlyDictionary<string, string> effectiveGlobalProperties,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        IEnumerable<string> configuredEnvironmentNames,
        IReadOnlyCollection<string> controlledEnvironmentNames)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> property in effectiveGlobalProperties)
            properties[property.Key] = property.Value;
        foreach (KeyValuePair<string, string> property in evaluatedProperties)
            properties[property.Key] = property.Value;

        var controlledNames = new HashSet<string>(
            controlledEnvironmentNames,
            StringComparer.OrdinalIgnoreCase);
        foreach (string environmentName in configuredEnvironmentNames)
        {
            if (effectiveGlobalProperties.TryGetValue(environmentName, out string? globalValue))
            {
                properties[environmentName] = globalValue;
            }
            else if (!controlledNames.Contains(environmentName))
            {
                properties.Remove(environmentName);
            }
        }

        return properties;
    }
}
