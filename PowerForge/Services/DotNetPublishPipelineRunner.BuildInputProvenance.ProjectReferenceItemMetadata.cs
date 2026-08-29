using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class LiteralProjectReferenceItemState
    {
        internal LiteralProjectReferenceItemState(IEnumerable<LiteralProjectReferenceMetadataAssignment> assignments)
        {
            Assignments = assignments.ToList();
        }

        internal List<LiteralProjectReferenceMetadataAssignment> Assignments { get; set; }
    }

    private static bool IsProjectReferenceItemDefinition(System.Xml.Linq.XElement projectReference)
        => projectReference.Parent?.Name.LocalName.Equals(
            "ItemDefinitionGroup",
            StringComparison.OrdinalIgnoreCase) == true;

    private static List<LiteralProjectReferenceMetadataAssignment>
        ExpandCurrentProjectReferenceItemMetadata(
            IEnumerable<LiteralProjectReferenceMetadataAssignment> assignments,
            IReadOnlyCollection<LiteralProjectReferenceMetadataAssignment> currentAssignments,
            string metadataName)
    {
        string pattern = @"%\((?:ProjectReference\.)?" + Regex.Escape(metadataName) + @"\)";
        var results = new List<LiteralProjectReferenceMetadataAssignment>();
        foreach (LiteralProjectReferenceMetadataAssignment assignment in assignments)
        {
            if (!Regex.IsMatch(
                    assignment.Value,
                    pattern,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                results.Add(assignment);
                continue;
            }

            string[] currentValues = currentAssignments.Count == 0
                ? [string.Empty]
                : currentAssignments.Select(current => current.Value).ToArray();
            foreach (string currentValue in currentValues)
            {
                results.Add(new LiteralProjectReferenceMetadataAssignment(
                    Regex.Replace(
                        assignment.Value,
                        pattern,
                        currentValue.Replace("$", "$$"),
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                    assignment));
                if (results.Count > MaximumProjectReferencePropertyContexts)
                    return [assignment];
            }
        }

        return results;
    }
}
