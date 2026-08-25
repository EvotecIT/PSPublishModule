using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly ISet<string> ControlledSdkTaskFileInputProperties =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AdditionalLibPaths",
            "AppConfig",
            "AppConfigForCompiler",
            "ApplicationIcon",
            "ApplicationManifest",
            "AssemblyOriginatorKeyFile",
            "CodeAnalysisRuleSet",
            "CompilerResponseFile",
            "FrameworkPathOverride",
            "KeyOriginatorFile",
            "ResolvedCodeAnalysisRuleSet",
            "Satellite_EvidenceFile",
            "Satellite_Win32Icon",
            "Satellite_Win32Resource",
            "SourceLink",
            "VBRuntimePath",
            "Win32Icon",
            "Win32Manifest",
            "Win32Resource"
        };

    private static readonly ISet<string> ControlledSdkTaskDirectoryInputProperties =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AdditionalLibPaths",
            "FrameworkPathOverride"
        };

    private static bool HasOnlyControlledSdkTaskPropertyFileInputs(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        Func<string, bool>? isControlledInput,
        Func<string, string[]?> readLines)
    {
        foreach (XElement property in document.Descendants().Where(element =>
                     element.Parent is not null &&
                     element.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
                     ControlledSdkTaskFileInputProperties.Contains(element.Name.LocalName)))
        {
            if (string.IsNullOrWhiteSpace(property.Value))
                continue;
            if (!TryExpandControlledTaskInputValues(
                    property.Value,
                    declaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedValues))
            {
                return false;
            }

            foreach (string candidate in expandedValues
                         .SelectMany(value => DecodeMsBuildEscapes(value).Split(';'))
                         .Select(value => value.Trim().Trim('\'', '"'))
                         .Where(value => value.Length > 0))
            {
                if (candidate.IndexOf('*') >= 0 ||
                    candidate.IndexOf('?') >= 0 ||
                    ContainsUnresolvedBuildExpression(candidate) ||
                    !TryResolveControlledTaskInputPath(
                        candidate,
                        declaringPath,
                        taskInputBaseDirectory,
                        declaringAllowedRoot,
                        taskInputAllowedRoot,
                        out string inputPath))
                {
                    return false;
                }

                if (ControlledSdkTaskDirectoryInputProperties.Contains(property.Name.LocalName))
                {
                    string allowedRoot = IsSameOrBelowBuildInputPath(inputPath, declaringAllowedRoot)
                        ? declaringAllowedRoot
                        : taskInputAllowedRoot;
                    if (!HasOnlyControlledDirectoryTaskInput(inputPath, allowedRoot, isControlledInput))
                        return false;
                }
                else if (isControlledInput is not null)
                {
                    if (!isControlledInput(inputPath))
                        return false;
                }
                else if (File.Exists(inputPath) || Directory.Exists(inputPath))
                {
                    string allowedRoot = IsSameOrBelowBuildInputPath(inputPath, declaringAllowedRoot)
                        ? declaringAllowedRoot
                        : taskInputAllowedRoot;
                    if (HasReparsePointBelowRoot(inputPath, allowedRoot))
                        return false;
                }

                if (property.Name.LocalName.Equals(
                        "CompilerResponseFile",
                        StringComparison.OrdinalIgnoreCase))
                {
                    string[]? lines = readLines(inputPath);
                    if (lines is null ||
                        !HasOnlyControlledCompilerResponseFileInputs(
                            lines,
                            inputPath,
                            taskInputBaseDirectory,
                            declaringAllowedRoot,
                            taskInputAllowedRoot,
                            isControlledInput))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
