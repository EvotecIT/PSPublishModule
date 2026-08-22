using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadPreprocessedProjectImports(
        ProjectEvaluationRequest request,
        out string[] imports,
        out PreprocessedProjectReferenceDeclaration[] projectReferenceDeclarations)
    {
        imports = Array.Empty<string>();
        projectReferenceDeclarations = Array.Empty<PreprocessedProjectReferenceDeclaration>();
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            "powerforge-msbuild-imports-" + Guid.NewGuid().ToString("N") + ".xml");
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-preprocess:" + outputPath
        };
        if (request.Configuration is not null)
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        if (request.TargetFramework is not null)
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut || !File.Exists(outputPath))
                return false;

            var resolved = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            bool inComment = false;
            bool describesImport = false;
            foreach (string line in File.ReadLines(outputPath))
            {
                if (line.Contains("<!--", StringComparison.Ordinal))
                {
                    inComment = true;
                    describesImport = false;
                }
                if (inComment && line.Contains("<Import", StringComparison.Ordinal))
                    describesImport = true;
                if (inComment && describesImport)
                {
                    string candidate = line.Trim();
                    if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                        resolved.Add(Path.GetFullPath(candidate));
                }
                if (line.Contains("-->", StringComparison.Ordinal))
                {
                    inComment = false;
                    describesImport = false;
                }
            }
            imports = resolved.ToArray();

            XDocument document = XDocument.Load(outputPath, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
                return false;

            var declarationSources = new Stack<string>();
            declarationSources.Push(Path.GetFullPath(request.ProjectPath));
            var propertyDefinitions = new List<PreprocessedProjectPropertyDefinition>();
            var declarationElements = new List<(
                XElement Element,
                string DefiningProjectPath,
                bool IsTargetTime,
                bool RunsBeforeResolveReferences)>();
            foreach (XNode node in document.Root.DescendantNodes())
            {
                if (node is XComment comment)
                {
                    if (comment.Value.Contains("</Import>", StringComparison.Ordinal))
                    {
                        if (declarationSources.Count > 1)
                            declarationSources.Pop();
                        continue;
                    }

                    if (comment.Value.Contains("<Import", StringComparison.Ordinal) &&
                        TryReadPreprocessedImportPath(comment.Value, out string? importedPath))
                    {
                        declarationSources.Push(importedPath!);
                    }
                    continue;
                }

                if (node is not XElement element)
                    continue;

                XElement? containingTarget = element.Ancestors().FirstOrDefault(ancestor =>
                    ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
                bool isTargetTime = containingTarget is not null;
                bool runsBeforeResolveReferences =
                    !string.IsNullOrEmpty(request.TargetFramework) &&
                    containingTarget is not null &&
                    ContainsMsBuildListEntry(
                        containingTarget.Attribute("BeforeTargets")?.Value,
                        "ResolveReferences");

                if (!isTargetTime &&
                    element.Parent?.Name.LocalName.Equals(
                        "PropertyGroup",
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    propertyDefinitions.Add(new PreprocessedProjectPropertyDefinition(
                        element,
                        declarationSources.Peek()));
                }

                if (element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase) &&
                    (element.Parent?.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) == true ||
                     element.Parent?.Name.LocalName.Equals("ItemDefinitionGroup", StringComparison.OrdinalIgnoreCase) == true))
                {
                    declarationElements.Add((
                        element,
                        declarationSources.Peek(),
                        isTargetTime,
                        runsBeforeResolveReferences));
                }
            }

            projectReferenceDeclarations = declarationElements
                .Select(declaration => new PreprocessedProjectReferenceDeclaration(
                    declaration.Element,
                    declaration.DefiningProjectPath,
                    propertyDefinitions,
                    declaration.IsTargetTime,
                    declaration.RunsBeforeResolveReferences))
                .ToArray();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
                // The provenance result already fails closed if preprocessing did not complete.
            }
        }
    }

    private static bool ContainsMsBuildListEntry(string? value, string expected)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(entry => entry.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadPreprocessedImportPath(string comment, out string? importPath)
    {
        importPath = comment
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(candidate => Path.IsPathRooted(candidate) && File.Exists(candidate));
        if (importPath is null)
            return false;

        importPath = Path.GetFullPath(importPath);
        return true;
    }
}
