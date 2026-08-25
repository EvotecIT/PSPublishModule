using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const long MaximumControlledBuildTextInputBytes = 4L * 1024L * 1024L;
    private const int MaximumControlledTaskFileInputExpressions = 4096;

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
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties = null,
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
                if (IsCompilerPluginTaskInput(task.Name.LocalName, attribute.Name.LocalName) &&
                    !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return false;
                }
                if (IsUncontrolledCompilerFreeFormTaskInput(
                        task.Name.LocalName,
                        attribute.Name.LocalName) &&
                    !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return false;
                }
                if (IsControlledReadLinesTaskInput(attribute.Value, relatedDocuments))
                    continue;
                if (!TryExpandControlledTaskInputValues(
                        attribute.Value,
                        declaringPath,
                        taskInputBaseDirectory,
                        relatedDocuments,
                        evaluatedGlobalProperties,
                        out string[] expandedValues))
                {
                    return false;
                }

                foreach (string value in expandedValues.SelectMany(expanded =>
                             DecodeMsBuildEscapes(expanded).Split(';')))
                {
                    string candidate = value.Trim().Trim('\'', '"');
                    if (candidate.Length == 0)
                        continue;
                    if (!TryNormalizeControlledCompilerTaskFileOperand(
                            task.Name.LocalName,
                            attribute.Name.LocalName,
                            candidate,
                            out candidate))
                    {
                        return false;
                    }
                    if (ContainsUnresolvedBuildExpression(
                            ReplaceOrdinalIgnoreCase(
                                candidate,
                                "$(MSBuildThisFileDirectory)",
                                string.Empty)))
                        return false;
                    if (candidate.IndexOf('*') >= 0 || candidate.IndexOf('?') >= 0)
                        return false;
                    if (!TryResolveControlledTaskInputPath(
                            candidate,
                            declaringPath,
                            taskInputBaseDirectory,
                            declaringAllowedRoot,
                            taskInputAllowedRoot,
                            out string inputPath))
                    {
                        return false;
                    }
                    if (IsControlledTaskDirectoryInput(
                            task.Name.LocalName,
                            attribute.Name.LocalName))
                    {
                        string directoryAllowedRoot = IsSameOrBelowBuildInputPath(
                            inputPath,
                            declaringAllowedRoot)
                            ? declaringAllowedRoot
                            : taskInputAllowedRoot;
                        if (!HasOnlyControlledDirectoryTaskInput(
                                inputPath,
                                directoryAllowedRoot,
                                isControlledInput))
                        {
                            return false;
                        }
                    }
                    else if (isControlledInput is not null)
                    {
                        if (!isControlledInput(inputPath))
                            return false;
                    }
                    else if ((File.Exists(inputPath) || Directory.Exists(inputPath)) &&
                             HasReparsePointBelowRoot(inputPath, taskInputAllowedRoot))
                    {
                        return false;
                    }
                    if (attribute.Name.LocalName.Equals("ResponseFiles", StringComparison.OrdinalIgnoreCase))
                    {
                        string[]? lines = readLines?.Invoke(inputPath);
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
        }

        return true;
    }

    private static bool IsCompilerPluginTaskInput(string taskName, string attributeName)
        => (taskName.Equals("Csc", StringComparison.OrdinalIgnoreCase) ||
            taskName.Equals("Vbc", StringComparison.OrdinalIgnoreCase) ||
            taskName.Equals("Fsc", StringComparison.OrdinalIgnoreCase)) &&
           attributeName.Equals("Analyzers", StringComparison.OrdinalIgnoreCase);

    private static bool IsUncontrolledCompilerFreeFormTaskInput(
        string taskName,
        string attributeName)
        => taskName.Equals("Fsc", StringComparison.OrdinalIgnoreCase) &&
           attributeName.Equals("OtherFlags", StringComparison.OrdinalIgnoreCase);

    private static bool IsControlledReadLinesTaskInput(
        string expression,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments)
    {
        Match match = Regex.Match(
            expression,
            @"^\s*@\(\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\)\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        string itemName = match.Groups[1].Value;
        XElement[] matchingOutputs = relatedDocuments
            .SelectMany(related => related.Document.Descendants())
            .Where(element =>
                element.Name.LocalName.Equals("Output", StringComparison.OrdinalIgnoreCase) &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName.Equals("ItemName", StringComparison.OrdinalIgnoreCase) &&
                    DecodeMsBuildEscapes(attribute.Value).Equals(itemName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matchingOutputs.Length == 0 || matchingOutputs.Any(output =>
                output.Parent is null ||
                !output.Parent.Name.LocalName.Equals("ReadLinesFromFile", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !relatedDocuments.SelectMany(related => related.Document.Descendants())
            .Any(element =>
                element.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
                element.Parent is not null &&
                element.Parent.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveControlledThisFileDirectory(string value, string declaringPath)
    {
        string declaringDirectory = Path.GetDirectoryName(Path.GetFullPath(declaringPath))!
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return ReplaceOrdinalIgnoreCase(
            value,
            "$(MSBuildThisFileDirectory)",
            declaringDirectory);
    }

    private static bool TryResolveControlledTaskInputPath(
        string value,
        string declaringPath,
        string allowedRoot,
        out string inputPath)
        => TryResolveControlledTaskInputPath(
            value,
            declaringPath,
            Path.GetDirectoryName(Path.GetFullPath(declaringPath))!,
            allowedRoot,
            out inputPath);

    private static bool TryResolveControlledTaskInputPath(
        string value,
        string declaringPath,
        string taskInputBaseDirectory,
        string allowedRoot,
        out string inputPath)
        => TryResolveControlledTaskInputPath(
            value,
            declaringPath,
            taskInputBaseDirectory,
            allowedRoot,
            allowedRoot,
            out inputPath);

    private static bool TryResolveControlledTaskInputPath(
        string value,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        out string inputPath)
    {
        inputPath = string.Empty;
        try
        {
            string declaringRoot = Path.GetFullPath(declaringAllowedRoot);
            string inputRoot = Path.GetFullPath(taskInputAllowedRoot);
            string declaringDirectory = Path.GetDirectoryName(Path.GetFullPath(declaringPath))!;
            string inputBaseDirectory = Path.GetFullPath(taskInputBaseDirectory);
            if (!IsSameOrBelowBuildInputPath(declaringDirectory, declaringRoot) ||
                !IsSameOrBelowBuildInputPath(inputBaseDirectory, inputRoot))
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
                    : Path.Combine(inputBaseDirectory, candidate));
            return IsSameOrBelowBuildInputPath(inputPath, declaringRoot) ||
                   IsSameOrBelowBuildInputPath(inputPath, inputRoot);
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
