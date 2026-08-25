using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const long MaximumControlledBuildTextInputBytes = 4L * 1024L * 1024L;
    private const int MaximumControlledTaskFileInputExpressions = 4096;

    private static bool HasOnlyControlledTaskLoadedFileInputs(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
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
                    taskInputBaseDirectory,
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
                        taskInputBaseDirectory,
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
        string taskInputBaseDirectory,
        string allowedRoot,
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
                if (IsControlledReadLinesTaskInput(attribute.Value, relatedDocuments))
                    continue;
                if (!TryExpandControlledTaskInputValues(
                        attribute.Value,
                        declaringPath,
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

    private static bool TryExpandControlledTaskInputValues(
        string expression,
        string declaringPath,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        out string[] expandedValues)
    {
        var pending = new Queue<string>();
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        pending.Enqueue(expression);
        while (pending.Count > 0)
        {
            if (inspected.Count >= MaximumControlledTaskFileInputExpressions)
            {
                expandedValues = Array.Empty<string>();
                return false;
            }

            string value = pending.Dequeue();
            if (!inspected.Add(value))
                continue;
            bool expanded = false;

            foreach (Match match in Regex.Matches(
                         value,
                         @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                         RegexOptions.CultureInvariant))
            {
                string propertyName = match.Groups[1].Value;
                if (propertyName.Equals("MSBuildThisFileDirectory", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (evaluatedGlobalProperties is not null &&
                    evaluatedGlobalProperties.TryGetValue(propertyName, out string? evaluatedValue))
                {
                    pending.Enqueue(value.Replace(match.Value, evaluatedValue));
                    expanded = true;
                    continue;
                }
                foreach ((XDocument relatedDocument, string relatedPath) in relatedDocuments)
                {
                    foreach (XElement property in relatedDocument.Descendants().Where(property =>
                                 property.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                                 property.Parent is not null &&
                                 property.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase)))
                    {
                        pending.Enqueue(value.Replace(
                            match.Value,
                            ResolveControlledThisFileDirectory(property.Value, relatedPath)));
                        expanded = true;
                    }
                }
            }

            foreach (Match match in Regex.Matches(
                         value,
                         @"@\(\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\)",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups[1].Value;
                foreach (string itemValue in EnumerateStaticItemValues(itemName, relatedDocuments))
                {
                    pending.Enqueue(value.Replace(match.Value, itemValue));
                    expanded = true;
                }
            }

            foreach (Match match in Regex.Matches(
                         value,
                         @"@\(\s*([A-Za-z_][A-Za-z0-9_.-]*?)(?=\s*(?:->|,|\)))",
                         RegexOptions.CultureInvariant))
            {
                foreach (string itemValue in EnumerateStaticItemValues(match.Groups[1].Value, relatedDocuments))
                {
                    pending.Enqueue(itemValue);
                    expanded = true;
                }
            }

            foreach (Match match in Regex.Matches(
                         value,
                         @"%\(\s*(?:([A-Za-z_][A-Za-z0-9_.-]*)\.)?([A-Za-z_][A-Za-z0-9_.-]*)\s*\)",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups[1].Value;
                string metadataName = match.Groups[2].Value;
                foreach ((XDocument relatedDocument, string relatedPath) in relatedDocuments)
                {
                    foreach (XElement metadata in relatedDocument.Descendants().Where(element =>
                                 element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
                                 (itemName.Length == 0 ||
                                  element.Ancestors().Any(ancestor =>
                                      ancestor.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase)))))
                    {
                        string metadataValue = ResolveControlledThisFileDirectory(metadata.Value, relatedPath);
                        pending.Enqueue(metadataValue);
                        pending.Enqueue(value.Replace(match.Value, metadataValue));
                        expanded = true;
                    }
                }
            }

            if (!expanded)
                values.Add(ResolveControlledThisFileDirectory(value, declaringPath));
        }

        expandedValues = values.ToArray();
        return values.Count > 0;
    }

    private static IEnumerable<string> EnumerateStaticItemValues(
        string itemName,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments)
    {
        foreach ((XDocument relatedDocument, string relatedPath) in relatedDocuments)
        {
            foreach (XElement item in relatedDocument.Descendants().Where(element =>
                         element.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
                         element.Parent is not null &&
                         element.Parent.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)))
            {
                XAttribute? include = item.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase));
                if (include is not null)
                    yield return ResolveControlledThisFileDirectory(include.Value, relatedPath);
                else if (!string.IsNullOrWhiteSpace(item.Value))
                    yield return ResolveControlledThisFileDirectory(item.Value, relatedPath);
            }
        }
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
    {
        inputPath = string.Empty;
        try
        {
            string root = Path.GetFullPath(allowedRoot);
            string declaringDirectory = Path.GetDirectoryName(Path.GetFullPath(declaringPath))!;
            string inputBaseDirectory = Path.GetFullPath(taskInputBaseDirectory);
            if (!IsSameOrBelowBuildInputPath(declaringDirectory, root) ||
                !IsSameOrBelowBuildInputPath(inputBaseDirectory, root))
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
