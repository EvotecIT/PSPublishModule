using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static bool HasOnlyControlledBuildFileInputs(string checkoutRoot)
    {
        try
        {
            var pending = new Stack<string>();
            var controlledInputs = new List<string>();
            pending.Push(checkoutRoot);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string childDirectory in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                        return false;
                    pending.Push(childDirectory);
                }

                foreach (string path in Directory.EnumerateFiles(directory))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        return false;
                    controlledInputs.Add(path);
                }
            }

            return HasOnlyControlledBuildFileInputs(checkoutRoot, controlledInputs);
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasOnlyControlledBuildFileInputs(
        string checkoutRoot,
        IReadOnlyCollection<string> controlledInputs)
        => HasOnlyControlledBuildFileInputs(
            checkoutRoot,
            controlledInputs,
            controlledInputs,
            evaluatedGlobalProperties: null);

    internal static bool HasOnlyControlledBuildFileInputs(
        string checkoutRoot,
        IReadOnlyCollection<string> controlledInputs,
        IReadOnlyCollection<string> executableMsBuildInputs,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties = null,
        string? taskInputBaseDirectory = null,
        string? controlledProjectPath = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>? evaluatedProjectContexts = null)
        => HasOnlyControlledBuildFileInputs(
            checkoutRoot,
            controlledInputs,
            executableMsBuildInputs,
            evaluatedGlobalProperties,
            taskInputBaseDirectory,
            controlledProjectPath,
            evaluatedProjectContexts,
            executableMsBuildInputs,
            targetGuardContexts: evaluatedProjectContexts is null
                ? null
                : evaluatedProjectContexts.SelectMany(project => project.Value.Select(properties =>
                    new TargetGuardEvaluationContext(
                        project.Key,
                        ReadDirectHelperStableGuardGlobals(
                            evaluatedGlobalProperties, properties),
                        properties,
                        executableMsBuildInputs)))
                    .ToArray(),
            out _);

    private static IReadOnlyDictionary<string, string> ReadDirectHelperStableGuardGlobals(
        IReadOnlyDictionary<string, string>? globalProperties,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        var stable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> property in globalProperties ??
                     new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (!evaluatedProperties.TryGetValue(property.Key, out string? evaluatedValue) ||
                string.Equals(property.Value, evaluatedValue, StringComparison.Ordinal))
                stable[property.Key] = property.Value;
        }
        return stable;
    }

    private static bool HasOnlyControlledBuildFileInputs(
        string checkoutRoot,
        IReadOnlyCollection<string> controlledInputs,
        IReadOnlyCollection<string> executableMsBuildInputs,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        string? taskInputBaseDirectory,
        string? controlledProjectPath,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>? evaluatedProjectContexts,
        IReadOnlyCollection<string> allEvaluatedMsBuildInputs,
        IReadOnlyCollection<TargetGuardEvaluationContext>? targetGuardContexts,
        out string? failureReason)
    {
        failureReason = null;
        try
        {
            string normalizedTaskInputBaseDirectory = Path.GetFullPath(
                taskInputBaseDirectory ?? checkoutRoot);
            if (!IsSameOrBelowBuildInputPath(normalizedTaskInputBaseDirectory, checkoutRoot))
            {
                failureReason = "task input base directory escaped the checkout";
                return false;
            }
            var executableInputs = new HashSet<string>(
                executableMsBuildInputs.Select(Path.GetFullPath),
                FileSystemPathSafety.ExistingPathComparer);
            // Direct helper callers have one known project context. Production checkouts
            // supply the evaluated import set for each project instance below; a missing
            // instance must not inherit proof from the release root.
            IReadOnlyDictionary<string, string> immutableGlobalProperties =
                evaluatedProjectContexts is null
                    ? ReadImmutableTargetGuardProperties(
                        evaluatedGlobalProperties,
                        allEvaluatedMsBuildInputs.Concat(executableMsBuildInputs))
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var proofsByDocument = new Dictionary<string, List<TargetGuardDocumentProof>>(
                FileSystemPathSafety.ExistingPathComparer);
            foreach (TargetGuardEvaluationContext context in targetGuardContexts ??
                     Array.Empty<TargetGuardEvaluationContext>())
            {
                Dictionary<string, string> immutableProperties = ReadImmutableTargetGuardProperties(
                    context.GlobalProperties,
                    // The source evaluation is not authoritative for the isolated invocation:
                    // its global overrides can activate a declared import that was inactive in
                    // the source. A candidate can revoke proof, but can never grant it.
                    context.EvaluatedImports
                        .Append(context.ProjectPath)
                        .Concat(executableMsBuildInputs));
                RemoveControlledInvocationGuardProperties(
                    immutableProperties,
                    context.UnstableGuardProperties);
                var proof = new TargetGuardDocumentProof(
                    context,
                    immutableProperties);
                foreach (string path in context.EvaluatedImports
                             .Append(context.ProjectPath)
                             .Select(Path.GetFullPath)
                             .Distinct(FileSystemPathSafety.ExistingPathComparer))
                {
                    if (!proofsByDocument.TryGetValue(path, out List<TargetGuardDocumentProof>? proofs))
                    {
                        proofs = new List<TargetGuardDocumentProof>();
                        proofsByDocument[path] = proofs;
                    }
                    proofs.Add(proof);
                }
            }
            if (!string.IsNullOrWhiteSpace(controlledProjectPath))
            {
                controlledProjectPath = Path.GetFullPath(controlledProjectPath!);
                if (!IsSameOrBelowBuildInputPath(controlledProjectPath, checkoutRoot) ||
                    !executableInputs.Contains(controlledProjectPath))
                {
                    failureReason = "controlled project is outside the executable MSBuild inputs";
                    return false;
                }
            }
            else
            {
                string[] controlledProjectPaths = executableInputs
                    .Where(IsControlledProjectPath)
                    .ToArray();
                controlledProjectPath = controlledProjectPaths.Length == 1
                    ? controlledProjectPaths[0]
                    : null;
            }
            var controlledDocuments = new List<XDocument>();
            var controlledDocumentSources = new List<(XDocument Document, string DeclaringPath)>();
            var executableDocuments = new List<(XDocument Document, string Path)>();
            foreach (string path in controlledInputs)
            {
                if (!File.Exists(path) ||
                    !IsSameOrBelowBuildInputPath(path, checkoutRoot) ||
                    HasReparsePointBelowRoot(path, checkoutRoot))
                {
                    failureReason = $"controlled input is missing, linked, or outside checkout: '{Path.GetFileName(path)}'";
                    return false;
                }

                string extension = Path.GetExtension(path);
                if (extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsAutomaticMsBuildResponseFile(path) &&
                        File.ReadLines(path).Any(value =>
                            ContainsExecutableResponseFileSwitch(value) ||
                            ContainsRootedBuildValue(value, checkoutRoot) ||
                            ContainsEscapingRelativeBuildValue(
                                value,
                                Path.GetDirectoryName(path)!,
                                checkoutRoot) ||
                            ContainsUncontrolledEnvironmentReference(value) ||
                            ContainsUncontrolledAmbientPropertyFunction(value) ||
                            ContainsUncontrolledFileSystemPropertyFunction(value) ||
                            ContainsUnresolvedBuildExpression(value)))
                    {
                        failureReason = $"automatic response file contains an uncontrolled value: '{Path.GetFileName(path)}'";
                        return false;
                    }
                    continue;
                }

                bool knownProjectExtension =
                    extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".proj", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
                XDocument document;
                try
                {
                    document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                }
                catch when (!knownProjectExtension)
                {
                    continue;
                }
                if (extension.Equals(".resx", StringComparison.OrdinalIgnoreCase))
                {
                    if (!HasOnlyControlledResourceFileInputs(document, path, checkoutRoot))
                    {
                        failureReason = $"resource file contains an uncontrolled input: '{Path.GetFileName(path)}'";
                        return false;
                    }
                    continue;
                }
                if (!knownProjectExtension &&
                    (document.Root is null ||
                     !document.Root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                bool isExecutableMsBuildInput = executableInputs.Contains(Path.GetFullPath(path));
                if (isExecutableMsBuildInput)
                {
                    controlledDocuments.Add(document);
                    controlledDocumentSources.Add((document, path));
                    executableDocuments.Add((document, path));
                }
                var controlledValues = document.DescendantNodes()
                    .OfType<XText>()
                    .Select(text => (
                        Node: (XObject)text,
                        Value: text.Value,
                        BaseDirectory: Path.GetDirectoryName(path)!,
                        ProjectReferenceOperation: false,
                        OverwrittenIntermediateProperty: IsControlledIntermediateOutputProperty(text)))
                    .Concat(document.Descendants().Attributes().Select(attribute => (
                        Node: (XObject)attribute,
                        Value: attribute.Value,
                        BaseDirectory: IsProjectReferenceItemOperationAttribute(attribute)
                            ? normalizedTaskInputBaseDirectory
                            : Path.GetDirectoryName(path)!,
                        ProjectReferenceOperation: IsProjectReferenceItemOperationAttribute(attribute),
                        OverwrittenIntermediateProperty: false)));
                foreach (var entry in controlledValues)
                {
                    if (entry.OverwrittenIntermediateProperty ||
                        (entry.ProjectReferenceOperation &&
                         IsControlledProjectReferencePathFunction(
                             entry.Value,
                             entry.BaseDirectory,
                             checkoutRoot)) ||
                        !((ContainsRootedBuildValue(entry.Value, checkoutRoot) &&
                           !IsPathMapPropertyValue(entry.Node)) ||
                          ContainsEscapingRelativeBuildValue(
                              entry.Value,
                              entry.BaseDirectory,
                              checkoutRoot) ||
                          ContainsUncontrolledEnvironmentReference(entry.Value) ||
                          (ContainsUncontrolledAmbientPropertyFunction(entry.Value) &&
                           !IsControlledLiteralPlatformCondition(entry.Node, entry.Value)) ||
                          ContainsUncontrolledFileSystemPropertyFunction(entry.Value)) ||
                        IsDefinitelyInactiveControlledBuildValue(
                            entry.Node,
                            path,
                            evaluatedGlobalProperties,
                            evaluatedProjectContexts,
                            proofsByDocument,
                            immutableGlobalProperties))
                    {
                        continue;
                    }
                    failureReason = $"MSBuild document contains an uncontrolled value: '{Path.GetFileName(path)}'";
                    return false;
                }
            }

            foreach ((XDocument document, string path) in executableDocuments)
            {
                if (ContainsControlledBuildPropertyEscape(document))
                {
                    failureReason = $"MSBuild document contains a property escape: '{Path.GetFileName(path)}'";
                    return false;
                }
                if (proofsByDocument.TryGetValue(path, out List<TargetGuardDocumentProof>? proofs) &&
                    proofs.Count > 0)
                {
                    foreach (TargetGuardDocumentProof proof in proofs)
                    {
                        var importedPaths = new HashSet<string>(
                            proof.Context.EvaluatedImports.Append(proof.Context.ProjectPath)
                                .Select(Path.GetFullPath),
                            FileSystemPathSafety.ExistingPathComparer);
                        (XDocument Document, string DeclaringPath)[] relatedSources =
                            controlledDocumentSources
                                .Where(source => importedPaths.Contains(Path.GetFullPath(source.DeclaringPath)))
                                .ToArray();
                        XDocument[] relatedDocuments = relatedSources
                            .Select(source => source.Document)
                            .ToArray();
                        if (!HasOnlyControlledDocumentTaskFileInputs(
                                document,
                                path,
                                normalizedTaskInputBaseDirectory,
                                checkoutRoot,
                                checkoutRoot,
                                relatedSources,
                                proof.EvaluatedProperties,
                                proof.Context.ProjectPath,
                                readLines: ReadControlledCheckoutTextInput,
                                immutableGlobalProperties: proof.ImmutableProperties))
                        {
                            failureReason = $"MSBuild document contains an uncontrolled task file input: '{Path.GetFileName(path)}'";
                            return false;
                        }
                        if (ContainsUncontrolledControlledBuildTask(
                                document,
                                relatedDocuments,
                                proof.EvaluatedProperties,
                                proof.ImmutableProperties,
                                proof.Context.ConservativeSdkHooks))
                        {
                            failureReason = "MSBuild graph contains an uncontrolled build task";
                            return false;
                        }
                    }
                    continue;
                }

                // A declared candidate without an authoritative loading context is still
                // checked, but cannot inherit the release root's inactive-target proof.
                IReadOnlyDictionary<string, string>[] propertyContexts =
                    evaluatedProjectContexts is not null &&
                    evaluatedProjectContexts.TryGetValue(
                        Path.GetFullPath(path),
                        out IReadOnlyDictionary<string, string>[]? contexts)
                        ? contexts
                        : [evaluatedProjectContexts is null
                            ? evaluatedGlobalProperties ??
                              new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)];
                string outputProjectPath = IsControlledProjectPath(path)
                    ? path
                    : controlledProjectPath!;
                foreach (IReadOnlyDictionary<string, string> properties in propertyContexts)
                {
                    if (!HasOnlyControlledDocumentTaskFileInputs(
                            document,
                            path,
                            normalizedTaskInputBaseDirectory,
                            checkoutRoot,
                            checkoutRoot,
                            controlledDocumentSources,
                            properties,
                            outputProjectPath,
                            readLines: ReadControlledCheckoutTextInput,
                            immutableGlobalProperties: immutableGlobalProperties))
                    {
                        failureReason = $"MSBuild document contains an uncontrolled task file input: '{Path.GetFileName(path)}'";
                        return false;
                    }
                    if (ContainsUncontrolledControlledBuildTask(
                            document,
                            controlledDocuments,
                            properties,
                            immutableGlobalProperties))
                    {
                        failureReason = "MSBuild graph contains an uncontrolled build task";
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"{exception.GetType().Name} while validating controlled build inputs";
            return false;
        }
    }

    private static bool IsControlledLiteralPlatformCondition(XObject node, string value)
    {
        if (node is not XAttribute attribute ||
            !attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remaining = System.Text.RegularExpressions.Regex.Replace(
            DecodeMsBuildEscapes(value),
            @"\$\(\[MSBuild\]::IsOSPlatform\(\s*([`'""])(Windows|Linux|OSX|FreeBSD)\1\s*\)\)",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        remaining = System.Text.RegularExpressions.Regex.Replace(
            remaining,
            @"\$\(\[MSBuild\]::IsOSUnixLike\(\s*\)\)",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return !ContainsUncontrolledAmbientPropertyFunction(remaining);
    }

    private static bool IsPathMapPropertyValue(XObject node)
        => node is XText text &&
           text.Parent is not null &&
           text.Parent.Name.LocalName.Equals("PathMap", StringComparison.OrdinalIgnoreCase) &&
           text.Parent.Parent is not null &&
           text.Parent.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase);

    private static bool IsDefinitelyInactiveControlledBuildValue(
        XObject node,
        string declaringPath,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]>? evaluatedProjectContexts,
        IReadOnlyDictionary<string, List<TargetGuardDocumentProof>> proofsByDocument,
        IReadOnlyDictionary<string, string> immutableGlobalProperties)
    {
        XElement? element = node switch
        {
            XText text => text.Parent,
            XAttribute attribute => attribute.Parent,
            _ => null
        };
        if (element is null)
            return false;
        if (proofsByDocument.TryGetValue(
                Path.GetFullPath(declaringPath),
                out List<TargetGuardDocumentProof>? proofs) && proofs.Count > 0)
        {
            return proofs.All(proof => IsDefinitelyInactiveControlledBuildOperation(
                element,
                proof.EvaluatedProperties,
                declaringPath,
                immutableGlobalProperties: proof.ImmutableProperties));
        }

        return evaluatedProjectContexts is null &&
               evaluatedGlobalProperties is not null &&
               IsDefinitelyInactiveControlledBuildOperation(
                   element,
                   evaluatedGlobalProperties,
                   declaringPath,
                   immutableGlobalProperties: immutableGlobalProperties);
    }

    private static bool IsProjectReferenceItemOperationAttribute(XAttribute attribute)
        => attribute.Parent is not null &&
           attribute.Parent.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase) &&
           (attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
            attribute.Name.LocalName.Equals("Exclude", StringComparison.OrdinalIgnoreCase) ||
            attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
            attribute.Name.LocalName.Equals("Remove", StringComparison.OrdinalIgnoreCase));

    private static bool IsControlledProjectReferencePathFunction(
        string value,
        string baseDirectory,
        string allowedRoot)
    {
        string decoded = DecodeMsBuildEscapes(value).Trim();
        const string combinePrefix = "$([System.IO.Path]::Combine(";
        const string fullPathPrefix = "$([System.IO.Path]::GetFullPath(";
        string argumentsText;
        bool combine;
        if (decoded.StartsWith(combinePrefix, StringComparison.OrdinalIgnoreCase) &&
            decoded.EndsWith("))", StringComparison.Ordinal))
        {
            argumentsText = decoded.Substring(
                combinePrefix.Length,
                decoded.Length - combinePrefix.Length - 2);
            combine = true;
        }
        else if (decoded.StartsWith(fullPathPrefix, StringComparison.OrdinalIgnoreCase) &&
                 decoded.EndsWith("))", StringComparison.Ordinal))
        {
            argumentsText = decoded.Substring(
                fullPathPrefix.Length,
                decoded.Length - fullPathPrefix.Length - 2);
            combine = false;
        }
        else
        {
            return false;
        }

        var arguments = new List<string>();
        int offset = 0;
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(
                     argumentsText,
                     @"(['""])(.*?)\1",
                     System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            string separator = argumentsText.Substring(offset, match.Index - offset);
            if (separator.Any(character => !char.IsWhiteSpace(character) && character != ','))
                return false;
            arguments.Add(match.Groups[2].Value);
            offset = match.Index + match.Length;
        }
        if (argumentsText.Substring(offset).Any(character =>
                !char.IsWhiteSpace(character) && character != ',') ||
            (combine ? arguments.Count < 2 : arguments.Count != 1))
        {
            return false;
        }

        try
        {
            for (int index = 0; index < arguments.Count; index++)
            {
                string argument = ReplaceOrdinalIgnoreCase(
                    arguments[index],
                    "$(MSBuildProjectDirectory)",
                    baseDirectory);
                argument = ReplaceOrdinalIgnoreCase(
                    argument,
                    "$(MSBuildThisFileDirectory)",
                    baseDirectory + Path.DirectorySeparatorChar);
                if (ContainsUnresolvedBuildExpression(argument))
                    return false;
                arguments[index] = argument;
            }

            string candidate = combine
                ? Path.Combine(arguments.ToArray())
                : arguments[0];
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(candidate)
                    ? candidate
                    : Path.Combine(baseDirectory, candidate));
            return IsSameOrBelowBuildInputPath(fullPath, allowedRoot);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsControlledIntermediateOutputProperty(XText text)
    {
        string? propertyName = text.Parent?.Name.LocalName;
        return propertyName is not null &&
               (propertyName.Equals("BaseIntermediateOutputPath", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Equals("MSBuildProjectExtensionsPath", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Equals("IntermediateOutputPath", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAutomaticMsBuildResponseFile(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Equals("Directory.Build.rsp", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("MSBuild.rsp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControlledProjectPath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".proj", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ContainsUncontrolledControlledBuildTask(
        XDocument document,
        IReadOnlyCollection<XDocument> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedProperties = null,
        IReadOnlyDictionary<string, string>? immutableGlobalProperties = null,
        bool conservativeSdkHooks = false)
    {
        evaluatedProperties ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryCreateReachableControlledBuildDocuments(
                document,
                relatedDocuments,
                evaluatedProperties,
                immutableGlobalProperties,
                conservativeSdkHooks,
                out XDocument reachableDocument,
                out XDocument[] reachableDocuments))
        {
            return true;
        }

        bool importActivation = ContainsUncontrolledImportActivation(reachableDocument);
        bool taskPropertyFunction = ContainsUncontrolledTaskInputPropertyFunction(
            reachableDocument,
            reachableDocuments,
            evaluatedProperties,
            immutableGlobalProperties);
        bool sdkOverride = ContainsUncontrolledSdkTaskExecutionOverride(
            reachableDocument, immutableGlobalProperties);
        bool compilerOverride = ContainsUncontrolledCompilerOptionOverride(
            reachableDocument, immutableGlobalProperties);
        XElement? uncontrolledElement = reachableDocument.Descendants().FirstOrDefault(element =>
            !IsDefinitelyInactiveControlledBuildOperation(
                element, evaluatedProperties, definingProjectPath: null,
                immutableGlobalProperties: immutableGlobalProperties) && (
            element.Name.LocalName.Equals("UsingTask", StringComparison.OrdinalIgnoreCase) ||
            (IsControlledBuildTaskElement(element) &&
             (!IsModeledControlledBuildTask(element.Name.LocalName) ||
              element.Name.LocalName.Equals("SignFile", StringComparison.OrdinalIgnoreCase) ||
              ContainsUncontrolledTaskExecutionOverride(element) ||
              ContainsUncontrolledTaskEnvironmentOverride(element))) ||
            (element.Ancestors().Any(ancestor =>
                 ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)) &&
             (IsAmbientBuildDiscoveryTask(element.Name.LocalName) ||
              IsExecutableCodeLoadingTask(element.Name.LocalName) ||
              element.Name.LocalName.Equals("DownloadFile", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("Exec", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("MSBuild", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("XmlPeek", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("JsonPeek", StringComparison.OrdinalIgnoreCase)))));
        return importActivation || taskPropertyFunction || sdkOverride || compilerOverride ||
               uncontrolledElement is not null;
    }

    private static bool ContainsUncontrolledTaskExecutionOverride(XElement task)
    {
        foreach (XAttribute attribute in task.Attributes())
        {
            string name = attribute.Name.LocalName;
            if ((name.Equals("ToolPath", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("ToolExe", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("CompilerTools", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("DotnetFscCompilerPath", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("SdkToolsPath", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return true;
            }

            if (name.Equals("ExecuteAsTool", StringComparison.OrdinalIgnoreCase) &&
                IsPotentiallyEnabledTaskBoolean(attribute.Value))
            {
                return true;
            }

            if (task.Name.LocalName.Equals("XslTransformation", StringComparison.OrdinalIgnoreCase) &&
                name.Equals("UseTrustedSettings", StringComparison.OrdinalIgnoreCase) &&
                IsPotentiallyEnabledTaskBoolean(attribute.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPotentiallyEnabledTaskBoolean(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUncontrolledSdkTaskExecutionOverride(
        XDocument document,
        IReadOnlyDictionary<string, string>? immutableGlobalProperties)
    {
        string[] booleanProperties =
        {
            "ComReferenceExecuteAsTool",
            "ExecuteAsTool",
            "ResGenExecuteAsTool"
        };
        string[] valueProperties =
        {
            "MSBuildSDKsPath",
            "MSBuildToolsPath",
            "LCEnvironment",
            "LCToolPath",
            "ResGenEnvironment",
            "ResgenToolPath",
            "ResolveComReferenceEnvironment",
            "ResolveComReferenceToolPath",
            "SGenEnvironment",
            "SGenToolPath",
            "WinMDExpEnvironment",
            "WinMDExpToolPath"
        };

        return document.Descendants().Any(element =>
            !IsDefinitelyInactiveControlledBuildOperation(
                element, EmptyTargetGuardProperties, definingProjectPath: null,
                immutableGlobalProperties: immutableGlobalProperties) &&
            element.Parent is not null &&
            element.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
            ((booleanProperties.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase) &&
              IsPotentiallyEnabledTaskBoolean(element.Value)) ||
             (valueProperties.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase) &&
              !string.IsNullOrWhiteSpace(element.Value))));
    }

    private static bool ContainsUncontrolledCompilerOptionOverride(
        XDocument document,
        IReadOnlyDictionary<string, string>? immutableGlobalProperties)
    {
        string[] propertyNames =
        {
            "DotnetFscCompilerPath",
            "FscOtherFlags",
            "KeyContainer",
            "KeyContainerName",
            "OtherFlags"
        };
        if (document.Descendants().Any(element =>
                !IsDefinitelyInactiveControlledBuildOperation(
                    element, EmptyTargetGuardProperties, definingProjectPath: null,
                    immutableGlobalProperties: immutableGlobalProperties) &&
                element.Parent is not null &&
                element.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
                propertyNames.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(element.Value)))
        {
            return true;
        }

        return document.Descendants().Any(element =>
            !IsDefinitelyInactiveControlledBuildOperation(
                element, EmptyTargetGuardProperties, definingProjectPath: null,
                immutableGlobalProperties: immutableGlobalProperties) &&
            element.Parent is not null &&
            element.Parent.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) &&
            (element.Name.LocalName.Equals("CompilerTools", StringComparison.OrdinalIgnoreCase) ||
             element.Name.LocalName.Equals("FscCompilerTools", StringComparison.OrdinalIgnoreCase))) ||
               document.Descendants().Any(element =>
                   !IsDefinitelyInactiveControlledBuildOperation(
                       element, EmptyTargetGuardProperties, definingProjectPath: null,
                       immutableGlobalProperties: immutableGlobalProperties) &&
                   IsControlledBuildTaskElement(element) &&
                   IsCompilerOrLinkerTask(element.Name.LocalName) &&
                   element.Attributes().Any(attribute =>
                       (attribute.Name.LocalName.Equals("Analyzers", StringComparison.OrdinalIgnoreCase) ||
                        attribute.Name.LocalName.Equals("KeyContainer", StringComparison.OrdinalIgnoreCase) ||
                        attribute.Name.LocalName.Equals("KeyContainerName", StringComparison.OrdinalIgnoreCase)) &&
                       !string.IsNullOrWhiteSpace(attribute.Value)));
    }

    private static bool IsCompilerOrLinkerTask(string taskName)
        => taskName.Equals("AL", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("Csc", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("Fsc", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("Vbc", StringComparison.OrdinalIgnoreCase);

    private static bool IsModeledControlledBuildTask(string taskName)
        => ControlledTaskFileInputAttributes.ContainsKey(taskName) ||
           ControlledTaskFileOutputAttributes.ContainsKey(taskName) ||
           ControlledTasksWithoutFilePaths.Contains(taskName) ||
           taskName.Equals("CallTarget", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("ReadLinesFromFile", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUncontrolledImportActivation(XDocument document)
    {
        foreach (XElement import in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
        {
            string project = import.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "Project",
                    StringComparison.OrdinalIgnoreCase))?
                .Value ?? string.Empty;
            // A wildcard can select a new file after controlled overrides change an
            // import condition. The declared-input inventory cannot enumerate that file.
            if (DecodeMsBuildEscapes(project).IndexOfAny(new[] { '*', '?' }) >= 0)
                return true;
            string unresolved = ReplaceOrdinalIgnoreCase(
                DecodeMsBuildEscapes(project),
                "$(MSBuildThisFileDirectory)",
                string.Empty);
            unresolved = ReplaceOrdinalIgnoreCase(
                unresolved,
                "$(MSBuildProjectDirectory)",
                string.Empty);
            if (ContainsUnresolvedBuildExpression(unresolved) &&
                !IsTrustedLegacyMsBuildToolsImport(project))
                return true;

            string[] conditions = import.AncestorsAndSelf()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName.Equals(
                    "Condition",
                    StringComparison.OrdinalIgnoreCase))
                .Select(attribute => attribute.Value)
                .ToArray();
            if (conditions.Any(condition =>
                    ConditionDependsOnControlledEnvironmentRewrite(condition, document)))
                return true;
        }

        return false;
    }

    private static bool IsTrustedLegacyMsBuildToolsImport(string project)
    {
        string normalized = DecodeMsBuildEscapes(project)
            .Replace('\\', '/')
            .Trim();
        string[] trustedImports =
        {
            "$(MSBuildToolsPath)/Microsoft.CSharp.targets",
            "$(MSBuildToolsPath)/Microsoft.VisualBasic.targets",
            "$(MSBuildToolsPath)/Microsoft.Common.props",
            "$(MSBuildToolsPath)/Microsoft.Common.targets"
        };
        return trustedImports.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ConditionDependsOnControlledEnvironmentRewrite(
        string condition,
        XDocument document)
    {
        var pending = new Queue<string>();
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(condition);
        while (pending.Count > 0)
        {
            if (inspected.Count >= MaximumControlledTaskInputExpressions)
                return true;
            string value = pending.Dequeue();
            if (!inspected.Add(value))
                continue;
            if (ContainsControlledEnvironmentRewriteReference(value))
                return true;

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         value,
                         @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                         System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                string propertyName = match.Groups[1].Value;
                foreach (XElement property in document.Descendants().Where(element =>
                             element.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                             element.Parent is not null &&
                             element.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase)))
                {
                    pending.Enqueue(property.Value);
                    foreach (XAttribute attribute in property.AncestorsAndSelf()
                                 .SelectMany(element => element.Attributes())
                                 .Where(attribute => attribute.Name.LocalName.Equals(
                                     "Condition",
                                     StringComparison.OrdinalIgnoreCase)))
                    {
                        pending.Enqueue(attribute.Value);
                    }
                }
            }
        }

        return false;
    }

    private static bool ContainsControlledEnvironmentRewriteReference(string value)
    {
        value = DecodeMsBuildEscapes(value);
        string[] names =
        {
            "ALL_PROXY",
            "DOTNET_ROOT",
            "HOME",
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "LOCALAPPDATA",
            "NO_PROXY",
            "NUGET_PACKAGES",
            "PATH",
            "TEMP",
            "TMP",
            "TMPDIR",
            "USERPROFILE",
            "XDG_CACHE_HOME",
            "XDG_CONFIG_HOME"
        };
        return names.Any(name =>
            value.IndexOf("$(" + name + ")", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("%" + name + "%", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool ContainsUncontrolledTaskEnvironmentOverride(XElement task)
    {
        string? value = task.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                "EnvironmentVariables",
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ContainsUncontrolledEnvironmentAssignments(value!);
    }

    private static bool ContainsUncontrolledEnvironmentAssignments(string value)
    {
        foreach (string assignment in DecodeMsBuildEscapes(value).Split(';'))
        {
            string candidate = assignment.Trim().Trim('\'', '"');
            if (candidate.Length == 0)
                continue;
            int separator = candidate.IndexOf('=');
            if (separator <= 0)
                return true;
            string name = candidate.Substring(0, separator).Trim();
            if (ContainsUnresolvedBuildExpression(name) ||
                IsUncontrolledRuntimeInjectionEnvironmentVariable(name))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsExecutableCodeLoadingTask(string taskName)
        => taskName.Equals("AspNetCompiler", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("LC", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("RegisterAssembly", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("SGen", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("UnregisterAssembly", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbientBuildDiscoveryTask(string taskName)
        => taskName.Equals("ResolveAssemblyReference", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("ResolveComReference", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("ResolveNativeReference", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("ResolveSDKReference", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("GetReferenceAssemblyPaths", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("GetFrameworkPath", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("GetFrameworkSdkPath", StringComparison.OrdinalIgnoreCase) ||
           taskName.Equals("GetInstalledSDKLocations", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsControlledBuildPropertyEscape(XDocument document)
    {
        string? localProperties = document.Root?
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                "TreatAsLocalProperty",
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        if (string.IsNullOrWhiteSpace(localProperties))
            return false;

        var protectedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RestoreConfigFile",
            "RestoreSources",
            "RestoreAdditionalProjectSources",
            "RestoreFallbackFolders",
            "RestoreNoCache",
            "RestoreIgnoreFailedSources",
            "RestoreLockedMode",
            "RestorePackagesWithLockFile",
            "RestoreForceEvaluate",
            "RestoreRecursive",
            "BaseIntermediateOutputPath",
            "MSBuildProjectExtensionsPath",
            "IntermediateOutputPath",
            "NuGetLockFilePath",
            "PowerForgeSdkPackageLockFile",
            "NuGetAudit",
            "RunAnalyzers",
            "RunAnalyzersDuringBuild",
            "RunAnalyzersDuringLiveAnalysis",
            "PreBuildEvent",
            "PostBuildEvent",
            "RunPostBuildEvent",
            "UseSharedCompilation",
            "ComReferenceExecuteAsTool",
            "ExecuteAsTool",
            "ResGenExecuteAsTool",
            "ResGenEnvironment",
            "ResgenToolPath",
            "ResolveComReferenceEnvironment",
            "ResolveComReferenceToolPath",
            "WinMDExpEnvironment",
            "WinMDExpToolPath",
            "LCEnvironment",
            "LCToolPath",
            "SGenEnvironment",
            "SGenToolPath",
            "AlToolPath",
            "AlToolExe",
            "CscToolPath",
            "CscToolExe",
            "VbcToolPath",
            "VbcToolExe",
            "FscToolPath",
            "FscToolExe",
            "KeyContainerName",
            "CustomAfterMicrosoftCommonTargets",
            "MSBuildSDKsPath",
            "MSBuildToolsPath"
        };
        return localProperties!
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Any(protectedProperties.Contains);
    }
}
