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
        string? controlledProjectPath = null)
    {
        try
        {
            string normalizedTaskInputBaseDirectory = Path.GetFullPath(
                taskInputBaseDirectory ?? checkoutRoot);
            if (!IsSameOrBelowBuildInputPath(normalizedTaskInputBaseDirectory, checkoutRoot))
                return false;
            var executableInputs = new HashSet<string>(
                executableMsBuildInputs.Select(Path.GetFullPath),
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(controlledProjectPath))
            {
                controlledProjectPath = Path.GetFullPath(controlledProjectPath!);
                if (!IsSameOrBelowBuildInputPath(controlledProjectPath, checkoutRoot) ||
                    !executableInputs.Contains(controlledProjectPath))
                {
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
                        return false;
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
                if (document.DescendantNodes()
                    .OfType<XText>()
                    .Select(text => text.Value)
                    .Concat(document.Descendants().Attributes().Select(attribute => attribute.Value))
                    .Any(value => ContainsRootedBuildValue(value, checkoutRoot) ||
                                  ContainsEscapingRelativeBuildValue(
                                      value,
                                      Path.GetDirectoryName(path)!,
                                      checkoutRoot) ||
                                  ContainsUncontrolledEnvironmentReference(value) ||
                                  ContainsUncontrolledAmbientPropertyFunction(value) ||
                                  ContainsUncontrolledFileSystemPropertyFunction(value)))
                {
                    return false;
                }
            }

            foreach ((XDocument document, string path) in executableDocuments)
            {
                if (ContainsControlledBuildPropertyEscape(document) ||
                    !HasOnlyControlledDocumentTaskFileInputs(
                        document,
                        path,
                        normalizedTaskInputBaseDirectory,
                        checkoutRoot,
                        checkoutRoot,
                        controlledDocumentSources,
                        evaluatedGlobalProperties,
                        controlledProjectPath,
                        readLines: ReadControlledCheckoutTextInput))
                {
                    return false;
                }
            }

            return !controlledDocuments.Any(document =>
                ContainsUncontrolledControlledBuildTask(document, controlledDocuments));
        }
        catch
        {
            return false;
        }
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

    private static bool ContainsUncontrolledControlledBuildTask(
        XDocument document,
        IReadOnlyCollection<XDocument> relatedDocuments)
    {
        return ContainsUncontrolledImportActivation(document) ||
               ContainsUncontrolledTaskInputPropertyFunction(document, relatedDocuments) ||
               ContainsUncontrolledCompilerOptionOverride(document) ||
               document.Descendants().Any(element =>
            element.Name.LocalName.Equals("UsingTask", StringComparison.OrdinalIgnoreCase) ||
            (IsControlledBuildTaskElement(element) &&
             (!IsModeledControlledBuildTask(element.Name.LocalName) ||
              element.Name.LocalName.Equals("SignFile", StringComparison.OrdinalIgnoreCase) ||
              element.Attributes().Any(attribute =>
                  attribute.Name.LocalName.Equals("ToolPath", StringComparison.OrdinalIgnoreCase) ||
                  attribute.Name.LocalName.Equals("ToolExe", StringComparison.OrdinalIgnoreCase) ||
                  attribute.Name.LocalName.Equals("CompilerTools", StringComparison.OrdinalIgnoreCase) ||
                  attribute.Name.LocalName.Equals("DotnetFscCompilerPath", StringComparison.OrdinalIgnoreCase)) ||
              ContainsUncontrolledTaskEnvironmentOverride(element))) ||
            (element.Ancestors().Any(ancestor =>
                 ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)) &&
             (IsAmbientBuildDiscoveryTask(element.Name.LocalName) ||
              IsExecutableCodeLoadingTask(element.Name.LocalName) ||
              element.Name.LocalName.Equals("DownloadFile", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("Exec", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("MSBuild", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("XmlPeek", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("JsonPeek", StringComparison.OrdinalIgnoreCase))));
    }

    private static bool ContainsUncontrolledCompilerOptionOverride(XDocument document)
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
                element.Parent is not null &&
                element.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
                propertyNames.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(element.Value)))
        {
            return true;
        }

        return document.Descendants().Any(element =>
            element.Parent is not null &&
            element.Parent.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) &&
            (element.Name.LocalName.Equals("CompilerTools", StringComparison.OrdinalIgnoreCase) ||
             element.Name.LocalName.Equals("FscCompilerTools", StringComparison.OrdinalIgnoreCase))) ||
               document.Descendants().Any(element =>
                   IsControlledBuildTaskElement(element) &&
                   IsCompilerOrLinkerTask(element.Name.LocalName) &&
                   element.Attributes().Any(attribute =>
                       (attribute.Name.LocalName.Equals("KeyContainer", StringComparison.OrdinalIgnoreCase) ||
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
            string unresolved = ReplaceOrdinalIgnoreCase(
                DecodeMsBuildEscapes(project),
                "$(MSBuildThisFileDirectory)",
                string.Empty);
            unresolved = ReplaceOrdinalIgnoreCase(
                unresolved,
                "$(MSBuildProjectDirectory)",
                string.Empty);
            if (ContainsUnresolvedBuildExpression(unresolved))
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
            "NuGetLockFilePath",
            "NuGetAudit",
            "RunAnalyzers",
            "RunAnalyzersDuringBuild",
            "RunAnalyzersDuringLiveAnalysis",
            "PreBuildEvent",
            "PostBuildEvent",
            "RunPostBuildEvent",
            "UseSharedCompilation",
            "AlToolPath",
            "AlToolExe",
            "CscToolPath",
            "CscToolExe",
            "VbcToolPath",
            "VbcToolExe",
            "FscToolPath",
            "FscToolExe",
            "KeyContainerName"
        };
        return localProperties!
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Any(protectedProperties.Contains);
    }
}
