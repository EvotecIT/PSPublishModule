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
    {
        try
        {
            var controlledDocuments = new List<XDocument>();
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
                    if (File.ReadLines(path).Any(value =>
                            ContainsExecutableResponseFileSwitch(value) ||
                            ContainsRootedBuildValue(value, checkoutRoot) ||
                            ContainsEscapingRelativeBuildValue(
                                value,
                                Path.GetDirectoryName(path)!,
                                checkoutRoot) ||
                            ContainsUncontrolledEnvironmentReference(value) ||
                            ContainsUncontrolledFileSystemPropertyFunction(value)))
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

                controlledDocuments.Add(document);
                if (ContainsControlledBuildPropertyEscape(document) ||
                    !HasOnlyControlledTaskLoadedFileInputs(
                        document,
                        path,
                        checkoutRoot,
                        ReadControlledCheckoutTextInput) ||
                    !HasOnlyControlledLiteralTaskFileInputs(document, path, checkoutRoot))
                {
                    return false;
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
                                  ContainsUncontrolledFileSystemPropertyFunction(value)))
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

    private static bool ContainsUncontrolledControlledBuildTask(
        XDocument document,
        IReadOnlyCollection<XDocument> relatedDocuments)
    {
        return ContainsUncontrolledTaskInputPropertyFunction(document, relatedDocuments) ||
               document.Descendants().Any(element =>
            element.Name.LocalName.Equals("UsingTask", StringComparison.OrdinalIgnoreCase) ||
            (IsControlledBuildTaskElement(element) &&
             element.Attributes().Any(attribute =>
                 attribute.Name.LocalName.Equals("ToolPath", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Name.LocalName.Equals("ToolExe", StringComparison.OrdinalIgnoreCase))) ||
            (element.Ancestors().Any(ancestor =>
                 ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase)) &&
             (IsAmbientBuildDiscoveryTask(element.Name.LocalName) ||
              element.Name.LocalName.Equals("DownloadFile", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("Exec", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("MSBuild", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("XmlPeek", StringComparison.OrdinalIgnoreCase) ||
              element.Name.LocalName.Equals("JsonPeek", StringComparison.OrdinalIgnoreCase))));
    }

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
            "CscToolPath",
            "CscToolExe",
            "VbcToolPath",
            "VbcToolExe",
            "FscToolPath",
            "FscToolExe"
        };
        return localProperties!
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Any(protectedProperties.Contains);
    }
}
