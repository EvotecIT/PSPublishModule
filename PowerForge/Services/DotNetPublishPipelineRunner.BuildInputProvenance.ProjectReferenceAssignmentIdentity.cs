using System.Text;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class LiteralProjectReferenceMetadataAssignment
    {
        internal LiteralProjectReferenceMetadataAssignment(
            string value,
            PreprocessedProjectReferenceDeclaration declaration,
            IReadOnlyDictionary<string, string> conditionProperties)
        {
            Value = value;
            DefiningProjectPath = declaration.DefiningProjectPath;
            PropertyDefinitions = declaration.PropertyDefinitions;
            InitialProperties = declaration.InitialProperties;
            ConditionProperties = conditionProperties;
            IsPreResolveTargetTime = declaration.IsTargetTime && declaration.RunsBeforeResolveReferences;
        }

        internal LiteralProjectReferenceMetadataAssignment(
            string value,
            LiteralProjectReferenceMetadataAssignment source)
        {
            Value = value;
            DefiningProjectPath = source.DefiningProjectPath;
            PropertyDefinitions = source.PropertyDefinitions;
            InitialProperties = source.InitialProperties;
            ConditionProperties = source.ConditionProperties;
            IsPreResolveTargetTime = source.IsPreResolveTargetTime;
        }

        internal string Value { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }

        internal IReadOnlyDictionary<string, string> InitialProperties { get; }

        internal IReadOnlyDictionary<string, string> ConditionProperties { get; }

        internal bool IsPreResolveTargetTime { get; }
    }

    private static List<LiteralProjectReferenceMetadataAssignment>
        MergeLiteralProjectReferenceMetadataAssignments(
            IEnumerable<LiteralProjectReferenceMetadataAssignment> first,
            IEnumerable<LiteralProjectReferenceMetadataAssignment> second)
    {
        return first.Concat(second)
            .GroupBy(BuildLiteralProjectReferenceMetadataAssignmentKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static string BuildLiteralProjectReferenceMetadataAssignmentKey(
        LiteralProjectReferenceMetadataAssignment assignment)
    {
        var key = new StringBuilder();
        AppendAssignmentKeyPart(
            key,
            IsWindows()
                ? assignment.DefiningProjectPath.ToUpperInvariant()
                : assignment.DefiningProjectPath);
        AppendAssignmentKeyPart(key, assignment.Value);
        AppendAssignmentKeyPart(key, assignment.IsPreResolveTargetTime ? "1" : "0");
        AppendAssignmentPropertyTable(key, assignment.InitialProperties);
        AppendAssignmentPropertyTable(key, assignment.ConditionProperties);
        foreach (PreprocessedProjectPropertyDefinition definition in assignment.PropertyDefinitions)
        {
            AppendAssignmentKeyPart(
                key,
                IsWindows()
                    ? definition.DefiningProjectPath.ToUpperInvariant()
                    : definition.DefiningProjectPath);
            AppendAssignmentKeyPart(
                key,
                definition.Element.ToString(SaveOptions.DisableFormatting));
        }
        return key.ToString();
    }

    private static void AppendAssignmentPropertyTable(
        StringBuilder key,
        IReadOnlyDictionary<string, string> properties)
    {
        foreach (KeyValuePair<string, string> property in properties.OrderBy(
                     item => item.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            AppendAssignmentKeyPart(key, property.Key.ToUpperInvariant());
            AppendAssignmentKeyPart(key, property.Value);
        }
        AppendAssignmentKeyPart(key, string.Empty);
    }

    private static void AppendAssignmentKeyPart(StringBuilder key, string value)
    {
        key.Append(value.Length);
        key.Append(':');
        key.Append(value);
    }
}
