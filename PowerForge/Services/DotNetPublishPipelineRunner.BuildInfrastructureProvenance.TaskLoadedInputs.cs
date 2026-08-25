using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const long MaximumControlledBuildTextInputBytes = 4L * 1024L * 1024L;

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
