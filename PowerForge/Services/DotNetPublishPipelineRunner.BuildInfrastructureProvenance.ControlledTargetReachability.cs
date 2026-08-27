using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryCreateReachableControlledBuildDocuments(
        XDocument document,
        IReadOnlyCollection<XDocument> relatedDocuments,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        out XDocument reachableDocument,
        out XDocument[] reachableDocuments)
    {
        var effectiveTargets = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XDocument relatedDocument in relatedDocuments)
        {
            foreach (XElement target in relatedDocument.Descendants().Where(element =>
                         element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(element.Attribute("Name")?.Value)))
            {
                effectiveTargets[target.Attribute("Name")!.Value.Trim()] = target;
            }
        }

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Build",
            "Publish",
            "ComputeFilesToPublish",
            "ResolveReferences",
            "ResolveProjectReferences"
        };
        foreach (XElement project in relatedDocuments
                     .Select(relatedDocument => relatedDocument.Root)
                     .Where(root => root is not null)!)
        {
            foreach (string attributeName in new[] { "InitialTargets", "DefaultTargets" })
            {
                string? expression = project.Attribute(attributeName)?.Value;
                if (HasUnresolvedMsBuildTargetList(expression, evaluatedProperties))
                {
                    reachableDocument = new XDocument();
                    reachableDocuments = Array.Empty<XDocument>();
                    return false;
                }
                reachable.UnionWith(ReadExpandedMsBuildTargetList(expression, evaluatedProperties));
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (KeyValuePair<string, XElement> entry in effectiveTargets)
            {
                XElement target = entry.Value;
                string? before = target.Attribute("BeforeTargets")?.Value;
                string? after = target.Attribute("AfterTargets")?.Value;
                bool unresolvedHook =
                    HasUnresolvedMsBuildTargetList(before, evaluatedProperties) ||
                    HasUnresolvedMsBuildTargetList(after, evaluatedProperties);
                bool hooksReachable = ReadExpandedMsBuildTargetList(before, evaluatedProperties)
                    .Concat(ReadExpandedMsBuildTargetList(after, evaluatedProperties))
                    .Any(reachable.Contains);
                if (!reachable.Contains(entry.Key) && (unresolvedHook || hooksReachable))
                    changed |= reachable.Add(entry.Key);
            }

            foreach (string targetName in reachable.ToArray())
            {
                if (!effectiveTargets.TryGetValue(targetName, out XElement? target))
                    continue;
                string? dependsOn = target.Attribute("DependsOnTargets")?.Value;
                if (HasUnresolvedMsBuildTargetList(dependsOn, evaluatedProperties))
                {
                    reachableDocument = new XDocument();
                    reachableDocuments = Array.Empty<XDocument>();
                    return false;
                }
                foreach (string dependency in ReadExpandedMsBuildTargetList(dependsOn, evaluatedProperties))
                    changed |= reachable.Add(dependency);

                foreach (XElement callTarget in target.Descendants().Where(element =>
                             element.Name.LocalName.Equals("CallTarget", StringComparison.OrdinalIgnoreCase)))
                {
                    string? destinations = callTarget.Attribute("Targets")?.Value;
                    if (HasUnresolvedMsBuildTargetList(destinations, evaluatedProperties))
                    {
                        reachableDocument = new XDocument();
                        reachableDocuments = Array.Empty<XDocument>();
                        return false;
                    }
                    foreach (string destination in ReadExpandedMsBuildTargetList(destinations, evaluatedProperties))
                        changed |= reachable.Add(destination);
                }

                foreach (XElement onError in target.Descendants().Where(element =>
                             element.Name.LocalName.Equals("OnError", StringComparison.OrdinalIgnoreCase)))
                {
                    string? destinations = onError.Attribute("ExecuteTargets")?.Value;
                    if (HasUnresolvedMsBuildTargetList(destinations, evaluatedProperties))
                    {
                        reachableDocument = new XDocument();
                        reachableDocuments = Array.Empty<XDocument>();
                        return false;
                    }
                    foreach (string destination in ReadExpandedMsBuildTargetList(destinations, evaluatedProperties))
                        changed |= reachable.Add(destination);
                }
            }
        }
        while (changed);

        reachableDocuments = relatedDocuments.Select(relatedDocument => new XDocument(relatedDocument)).ToArray();
        int sourceIndex = relatedDocuments
            .Select((relatedDocument, index) => (relatedDocument, index))
            .Where(entry => ReferenceEquals(entry.relatedDocument, document))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        foreach (XDocument scopedDocument in reachableDocuments)
        {
            foreach (XElement target in scopedDocument.Descendants().Where(element =>
                         element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                string? name = target.Attribute("Name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(name) || !reachable.Contains(name!))
                    target.Remove();
            }
        }
        reachableDocument = sourceIndex >= 0
            ? reachableDocuments[sourceIndex]
            : new XDocument(document);
        return true;
    }
}
