using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const long MaximumControlledBuildTextInputBytes = 4L * 1024L * 1024L;

    private static readonly IReadOnlyDictionary<string, string[]> ControlledTaskFileInputAttributes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AL"] = ["EmbedResources", "LinkResources", "ResponseFiles", "Sources", "TemplateFile", "Win32Icon", "Win32Resource"],
            ["Copy"] = ["SourceFiles"],
            ["Csc"] = ["ApplicationConfiguration", "Resources", "ResponseFiles", "Sources", "Win32Icon", "Win32Resource"],
            ["Fsc"] = ["ResponseFiles", "Sources", "Win32Icon", "Win32Resource"],
            ["GenerateResource"] = ["References", "Sources", "StateFile"],
            ["GetFileHash"] = ["Files"],
            ["Hash"] = ["Items"],
            ["Vbc"] = ["ApplicationConfiguration", "Resources", "ResponseFiles", "Sources", "Win32Icon", "Win32Resource"],
            ["XslTransformation"] = ["XmlInputPaths", "XslInputPath"]
        };

    private static bool HasOnlyControlledTaskLoadedFileInputs(
        XDocument document,
        string declaringPath,
        string allowedRoot,
        Func<string, string[]?> readLines)
    {
        foreach (XElement task in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("ReadLinesFromFile", StringComparison.OrdinalIgnoreCase) &&
                     element.Ancestors().Any(ancestor =>
                         ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))))
        {
            string? fileValue = task.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "File",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            if (string.IsNullOrWhiteSpace(fileValue) ||
                !TryResolveControlledTaskInputPath(
                    fileValue!,
                    declaringPath,
                    allowedRoot,
                    out string inputPath))
            {
                return false;
            }

            if ((File.Exists(inputPath) || Directory.Exists(inputPath)) &&
                HasReparsePointBelowRoot(inputPath, allowedRoot))
            {
                return false;
            }

            string[]? lines = readLines(inputPath);
            if (lines is null || lines.Any(value =>
                    ContainsRootedBuildValue(value, allowedRoot) ||
                    ContainsEscapingRelativeBuildValue(value, allowedRoot, allowedRoot) ||
                    ContainsUncontrolledEnvironmentReference(value) ||
                    ContainsUncontrolledFileSystemPropertyFunction(value) ||
                    ContainsUnresolvedBuildExpression(value)))
            {
                return false;
            }
            foreach (string value in lines)
            {
                string candidate = DecodeMsBuildEscapes(value).Trim().Trim('\'', '"');
                if (candidate.Length == 0 || ContainsUnresolvedBuildExpression(candidate))
                    continue;
                if (TryResolveControlledTaskInputPath(
                        candidate,
                        declaringPath,
                        allowedRoot,
                        out string loadedInputPath) &&
                    (File.Exists(loadedInputPath) || Directory.Exists(loadedInputPath)) &&
                    HasReparsePointBelowRoot(loadedInputPath, allowedRoot))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasOnlyControlledResourceFileInputs(
        XDocument document,
        string declaringPath,
        string allowedRoot,
        Func<string, bool>? isControlledInput = null)
    {
        foreach (XElement data in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                     element.Attributes().Any(attribute =>
                         attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                         attribute.Value.IndexOf("ResXFileRef", StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            string? value = data.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals(
                    "value",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            string fileValue = value?.Split(new[] { ';' }, 2)[0].Trim() ?? string.Empty;
            if (fileValue.Length == 0 ||
                !TryResolveControlledTaskInputPath(
                    fileValue,
                    declaringPath,
                    allowedRoot,
                    out string inputPath) ||
                (isControlledInput is null
                    ? !File.Exists(inputPath) || HasReparsePointBelowRoot(inputPath, allowedRoot)
                    : !isControlledInput(inputPath)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOnlyControlledLiteralTaskFileInputs(
        XDocument document,
        string declaringPath,
        string allowedRoot,
        Func<string, bool>? isControlledInput = null,
        Func<string, string[]?>? readLines = null)
    {
        foreach (XElement task in document.Descendants().Where(IsControlledBuildTaskElement))
        {
            if (!ControlledTaskFileInputAttributes.TryGetValue(
                    task.Name.LocalName,
                    out string[]? inputAttributes))
            {
                continue;
            }

            foreach (XAttribute attribute in task.Attributes().Where(attribute =>
                         inputAttributes.Contains(attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
            {
                foreach (string value in DecodeMsBuildEscapes(attribute.Value).Split(';'))
                {
                    string candidate = value.Trim().Trim('\'', '"');
                    if (candidate.Length == 0 || ContainsUnresolvedBuildExpression(
                            ReplaceOrdinalIgnoreCase(
                                candidate,
                                "$(MSBuildThisFileDirectory)",
                                string.Empty)))
                        continue;
                    if (candidate.IndexOf('*') >= 0 || candidate.IndexOf('?') >= 0)
                        return false;
                    if (!TryResolveControlledTaskInputPath(
                            candidate,
                            declaringPath,
                            allowedRoot,
                            out string inputPath))
                    {
                        return false;
                    }
                    if (isControlledInput is not null)
                    {
                        if (!isControlledInput(inputPath))
                            return false;
                    }
                    else if ((File.Exists(inputPath) || Directory.Exists(inputPath)) &&
                             HasReparsePointBelowRoot(inputPath, allowedRoot))
                    {
                        return false;
                    }
                    if (attribute.Name.LocalName.Equals("ResponseFiles", StringComparison.OrdinalIgnoreCase))
                    {
                        string[]? lines = readLines?.Invoke(inputPath);
                        if (lines is null || lines.Any(line =>
                                ContainsExecutableResponseFileSwitch(line) ||
                                ContainsRootedBuildValue(line, allowedRoot) ||
                                ContainsEscapingRelativeBuildValue(
                                    line,
                                    Path.GetDirectoryName(inputPath)!,
                                    allowedRoot) ||
                                ContainsUncontrolledEnvironmentReference(line) ||
                                ContainsUncontrolledFileSystemPropertyFunction(line) ||
                                ContainsUnresolvedBuildExpression(line)))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private static bool TryResolveControlledTaskInputPath(
        string value,
        string declaringPath,
        string allowedRoot,
        out string inputPath)
    {
        inputPath = string.Empty;
        try
        {
            string root = Path.GetFullPath(allowedRoot);
            string declaringDirectory = Path.GetDirectoryName(Path.GetFullPath(declaringPath))!;
            if (!IsSameOrBelowBuildInputPath(declaringDirectory, root))
                return false;

            string thisFileDirectory = declaringDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = ReplaceOrdinalIgnoreCase(
                    DecodeMsBuildEscapes(value),
                    "$(MSBuildThisFileDirectory)",
                    thisFileDirectory)
                .Trim()
                .Trim('\'', '"');
            if (candidate.Length == 0 ||
                candidate.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf(';') >= 0 ||
                ContainsUncontrolledEnvironmentReference(candidate) ||
                ContainsUncontrolledFileSystemPropertyFunction(candidate))
            {
                return false;
            }

            inputPath = Path.GetFullPath(
                Path.IsPathRooted(candidate)
                    ? candidate
                    : Path.Combine(declaringDirectory, candidate));
            return IsSameOrBelowBuildInputPath(inputPath, root);
        }
        catch
        {
            inputPath = string.Empty;
            return false;
        }
    }

    private static bool ContainsUnresolvedBuildExpression(string value)
    {
        value = DecodeMsBuildEscapes(value);
        return value.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
               value.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
               value.IndexOf("%(", StringComparison.Ordinal) >= 0;
    }

    private static string[]? ReadControlledCheckoutTextInput(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaximumControlledBuildTextInputBytes
                ? File.ReadAllLines(file.FullName)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
