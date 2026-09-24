using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class TargetGuardEvaluationContext
    {
        internal TargetGuardEvaluationContext(
            string projectPath,
            IReadOnlyDictionary<string, string> globalProperties,
            IReadOnlyDictionary<string, string> evaluatedProperties,
            IReadOnlyCollection<string> evaluatedImports,
            IReadOnlyCollection<string>? generatedImportRoots = null,
            bool conservativeSdkHooks = false)
        {
            ProjectPath = Path.GetFullPath(projectPath);
            GlobalProperties = globalProperties;
            EvaluatedProperties = evaluatedProperties;
            EvaluatedImports = evaluatedImports;
            GeneratedImportRoots = generatedImportRoots ?? Array.Empty<string>();
            ConservativeSdkHooks = conservativeSdkHooks;
        }

        internal string ProjectPath { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal IReadOnlyDictionary<string, string> EvaluatedProperties { get; }
        internal IReadOnlyCollection<string> EvaluatedImports { get; }
        internal IReadOnlyCollection<string> GeneratedImportRoots { get; }
        internal bool ConservativeSdkHooks { get; }
    }

    private sealed class TargetGuardDocumentProof
    {
        internal TargetGuardDocumentProof(
            TargetGuardEvaluationContext context,
            IReadOnlyDictionary<string, string> immutableProperties)
        {
            Context = context;
            ImmutableProperties = immutableProperties;
            var properties = new Dictionary<string, string>(
                context.GlobalProperties,
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in context.EvaluatedProperties)
                properties[property.Key] = property.Value;
            EvaluatedProperties = properties;
        }

        internal TargetGuardEvaluationContext Context { get; }
        internal IReadOnlyDictionary<string, string> ImmutableProperties { get; }
        internal IReadOnlyDictionary<string, string> EvaluatedProperties { get; }
    }

    private static bool IsDefinitelyInactiveControlledBuildOperation(
        XElement element,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        string? definingProjectPath,
        IEnumerable<XDocument>? relatedDocuments = null,
        IReadOnlyDictionary<string, string>? immutableGlobalProperties = null)
    {
        if (!IsDefinitelyInactiveMsBuildElement(element, evaluatedProperties, definingProjectPath))
            return false;

        XElement? target = element.AncestorsAndSelf().FirstOrDefault(candidate =>
            candidate.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return true;

        // Evaluation values can change before or during a target, including through SDK
        // targets and engine-managed properties. Only a false condition proven from
        // immutable global properties can make a target-time operation unreachable.
        return immutableGlobalProperties is not null &&
               IsDefinitelyInactiveMsBuildElement(
                   element,
                   immutableGlobalProperties,
                   definingProjectPath);
    }

    private static IReadOnlyDictionary<string, string> ReadImmutableTargetGuardProperties(
        IReadOnlyDictionary<string, string>? globalProperties,
        IEnumerable<string> evaluatedMsBuildInputs)
    {
        var immutable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (globalProperties is null)
            return immutable;

        foreach (KeyValuePair<string, string> property in globalProperties)
            immutable[property.Key] = property.Value;
        immutable.Remove("MSBuildLastTaskResult");

        foreach (string path in evaluatedMsBuildInputs.Distinct(FileSystemPathSafety.ExistingPathComparer))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(path, LoadOptions.None);
            }
            catch
            {
                // An incomplete import inventory cannot prove a property immutable.
                immutable.Clear();
                break;
            }

            string? localProperties = document.Root?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "TreatAsLocalProperty",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            if (localProperties is not null)
                localProperties = DecodeMsBuildEscapes(localProperties);
            if (!string.IsNullOrWhiteSpace(localProperties) &&
                ContainsUnresolvedBuildExpression(localProperties!))
            {
                immutable.Clear();
                break;
            }

            foreach (string propertyName in (localProperties ?? string.Empty).Split(';'))
                immutable.Remove(propertyName.Trim());

            foreach (XElement target in document.Descendants().Where(element =>
                         element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (XElement assignment in target.Descendants())
                {
                    if (assignment.Parent?.Name.LocalName.Equals(
                            "PropertyGroup",
                            StringComparison.OrdinalIgnoreCase) == true)
                    {
                        immutable.Remove(assignment.Name.LocalName);
                    }

                    if (!assignment.Name.LocalName.Equals("Output", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string? propertyName = assignment.Attributes()
                        .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                            "PropertyName",
                            StringComparison.OrdinalIgnoreCase))?
                        .Value;
                    if (string.IsNullOrWhiteSpace(propertyName))
                        continue;
                    propertyName = DecodeMsBuildEscapes(propertyName!);
                    if (ContainsUnresolvedBuildExpression(propertyName))
                    {
                        immutable.Clear();
                        return immutable;
                    }
                    immutable.Remove(propertyName.Trim());
                }
            }
        }

        return immutable;
    }

    private static string[] ReadTargetGuardGeneratedImportRoots(
        string projectPath,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        var roots = new HashSet<string>(FileSystemPathSafety.ExistingPathComparer);
        foreach (string name in new[] { "MSBuildProjectExtensionsPath", "BaseIntermediateOutputPath" })
        {
            if (!evaluatedProperties.TryGetValue(name, out string? value) ||
                string.IsNullOrWhiteSpace(value) ||
                ContainsUnresolvedBuildExpression(value))
            {
                continue;
            }

            try
            {
                roots.Add(Path.GetFullPath(Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(Path.GetDirectoryName(projectPath)!, value)));
            }
            catch
            {
                // An unresolved generated path cannot support immutable proof.
            }
        }
        return roots.ToArray();
    }

    internal static bool IsGuardInertGeneratedImportWrapper(string path)
    {
        try
        {
            XDocument document = XDocument.Load(path, LoadOptions.None);
            XElement? root = document.Root;
            return root is not null &&
                   root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase) &&
                   !root.Attributes().Any(attribute =>
                       attribute.Name.LocalName.Equals("TreatAsLocalProperty", StringComparison.OrdinalIgnoreCase) ||
                       attribute.Name.LocalName.Equals("InitialTargets", StringComparison.OrdinalIgnoreCase)) &&
                   !root.Descendants().Any(element =>
                       element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase) ||
                       element.Name.LocalName.Equals("UsingTask", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
