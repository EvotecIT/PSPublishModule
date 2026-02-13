using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates unified package/module hub metadata from .csproj and .psd1 files.</summary>
public static class WebPackageHubGenerator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Generates package hub JSON output.</summary>
    /// <param name="options">Generator options.</param>
    /// <returns>Result payload.</returns>
    public static WebPackageHubResult Generate(WebPackageHubOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var warnings = new List<string>();
        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var outputPath = ResolvePath(options.OutputPath, baseDir, warnings, "OutputPath");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("OutputPath is invalid.", nameof(options));

        var projectPaths = ResolveInputs(options.ProjectPaths, baseDir, warnings, "ProjectPath")
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var modulePaths = ResolveInputs(options.ModulePaths, baseDir, warnings, "ModulePath")
            .Where(path => path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (projectPaths.Count == 0 && modulePaths.Count == 0)
            throw new InvalidOperationException("package-hub requires at least one project or module input.");

        var libraries = new List<WebPackageHubLibrary>();
        foreach (var projectPath in projectPaths)
        {
            if (!File.Exists(projectPath))
            {
                warnings.Add($"Project file not found: {projectPath}");
                continue;
            }

            if (TryParseProject(projectPath, baseDir, warnings, out var library) && library is not null)
                libraries.Add(library);
        }

        var modules = new List<WebPackageHubModule>();
        foreach (var modulePath in modulePaths)
        {
            if (!File.Exists(modulePath))
            {
                warnings.Add($"Module manifest not found: {modulePath}");
                continue;
            }

            if (TryParseModuleManifest(modulePath, baseDir, warnings, out var module) && module is not null)
                modules.Add(module);
        }

        var document = new WebPackageHubDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Package Hub" : options.Title!.Trim(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Libraries = libraries,
            Modules = modules,
            Warnings = warnings
        };

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, WebJson.Options));

        return new WebPackageHubResult
        {
            OutputPath = outputPath,
            LibraryCount = libraries.Count,
            ModuleCount = modules.Count,
            Warnings = warnings.ToArray()
        };
    }

    private static List<string> ResolveInputs(IEnumerable<string>? inputs, string? baseDir, List<string> warnings, string label)
    {
        var results = new List<string>();
        if (inputs is null)
            return results;

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var resolved = ResolvePath(input, baseDir, warnings, label);
            if (!string.IsNullOrWhiteSpace(resolved))
                results.Add(resolved);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseProject(string projectPath, string? baseDir, List<string> warnings, out WebPackageHubLibrary? library)
    {
        library = null;
        try
        {
            var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
            var packageId = FirstElementValue(doc, "PackageId");
            var assemblyName = FirstElementValue(doc, "AssemblyName");
            var name = packageId ?? assemblyName ?? Path.GetFileNameWithoutExtension(projectPath);
            var tfm = FirstElementValue(doc, "TargetFramework");
            var tfms = FirstElementValue(doc, "TargetFrameworks");

            var frameworks = new List<string>();
            if (!string.IsNullOrWhiteSpace(tfms))
                frameworks.AddRange(SplitList(tfms));
            else if (!string.IsNullOrWhiteSpace(tfm))
                frameworks.AddRange(SplitList(tfm));

            var dependencies = new List<WebPackageHubDependency>();
            foreach (var packageReference in doc.Descendants().Where(e => e.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase)))
            {
                var depName = packageReference.Attribute("Include")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(depName))
                    depName = packageReference.Attribute("Update")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(depName))
                    continue;

                var depVersion = packageReference.Attribute("Version")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(depVersion))
                {
                    depVersion = packageReference.Elements()
                        .FirstOrDefault(e => e.Name.LocalName.Equals("Version", StringComparison.OrdinalIgnoreCase))
                        ?.Value?.Trim();
                }

                dependencies.Add(new WebPackageHubDependency
                {
                    Name = depName,
                    Version = string.IsNullOrWhiteSpace(depVersion) ? null : depVersion
                });
            }

            library = new WebPackageHubLibrary
            {
                Path = ToRelativePath(projectPath, baseDir),
                Name = name,
                PackageId = packageId,
                Version = FirstElementValue(doc, "Version") ?? FirstElementValue(doc, "PackageVersion"),
                Description = FirstElementValue(doc, "Description") ?? FirstElementValue(doc, "PackageDescription"),
                Authors = FirstElementValue(doc, "Authors") ?? FirstElementValue(doc, "Author"),
                RepositoryUrl = FirstElementValue(doc, "RepositoryUrl") ?? FirstElementValue(doc, "PackageProjectUrl"),
                TargetFrameworks = frameworks
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Dependencies = dependencies
                    .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse project '{projectPath}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool TryParseModuleManifest(string manifestPath, string? baseDir, List<string> warnings, out WebPackageHubModule? module)
    {
        module = null;
        try
        {
            var content = File.ReadAllText(manifestPath);
            var moduleName = Path.GetFileNameWithoutExtension(manifestPath);
            var version = ReadPsd1Scalar(content, "ModuleVersion");
            var description = ReadPsd1Scalar(content, "Description");
            var author = ReadPsd1Scalar(content, "Author");
            var powershellVersion = ReadPsd1Scalar(content, "PowerShellVersion");
            var editions = ReadPsd1Array(content, "CompatiblePSEditions");
            var requiredModules = ReadPsd1RequiredModules(content)
                .GroupBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var exportedCommands = ReadPsd1Array(content, "CmdletsToExport")
                .Concat(ReadPsd1Array(content, "FunctionsToExport"))
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Equals("*", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            module = new WebPackageHubModule
            {
                Path = ToRelativePath(manifestPath, baseDir),
                Name = moduleName,
                Version = version,
                Description = description,
                Author = author,
                PowerShellVersion = powershellVersion,
                CompatiblePSEditions = editions,
                ExportedCommands = exportedCommands,
                RequiredModules = requiredModules
            };

            return true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse module manifest '{manifestPath}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string? FirstElementValue(XDocument doc, string localName)
    {
        return doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private static string? ReadPsd1Scalar(string content, string key)
    {
        var expression = ReadPsd1ValueExpression(content, key);
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        return NormalizePsd1ScalarValue(expression);
    }

    private static List<string> ReadPsd1Array(string content, string key)
    {
        var expression = ReadPsd1ValueExpression(content, key);
        if (string.IsNullOrWhiteSpace(expression))
            return new List<string>();

        var body = NormalizePsd1ListBody(expression);
        var values = ExtractQuotedStrings(body).ToList();

        if (values.Count == 0)
        {
            values.AddRange(SplitList(body));
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<WebPackageHubDependency> ReadPsd1RequiredModules(string content)
    {
        var expression = ReadPsd1ValueExpression(content, "RequiredModules");
        if (string.IsNullOrWhiteSpace(expression))
            return new List<WebPackageHubDependency>();

        var body = NormalizePsd1ListBody(expression);
        var values = new List<WebPackageHubDependency>();

        foreach (Match match in Regex.Matches(body, @"(?is)@\{(?<body>.*?)\}", RegexOptions.CultureInvariant, RegexTimeout))
        {
            var hashBody = match.Groups["body"].Value;
            var name = ReadHashtableValue(hashBody, "ModuleName") ?? ReadHashtableValue(hashBody, "Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var version = ReadHashtableValue(hashBody, "RequiredVersion") ??
                          ReadHashtableValue(hashBody, "ModuleVersion") ??
                          ReadHashtableValue(hashBody, "MaximumVersion");
            values.Add(new WebPackageHubDependency
            {
                Name = name,
                Version = version
            });
        }

        var bodyWithoutTables = Regex.Replace(body, @"(?is)@\{.*?\}", " ", RegexOptions.CultureInvariant, RegexTimeout);
        foreach (var name in ExtractQuotedStrings(bodyWithoutTables))
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            values.Add(new WebPackageHubDependency
            {
                Name = name
            });
        }

        if (values.Count == 0)
        {
            foreach (var name in SplitList(bodyWithoutTables))
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                values.Add(new WebPackageHubDependency
                {
                    Name = name
                });
            }
        }

        return values;
    }

    private static string? ReadPsd1ValueExpression(string content, string key)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(key))
            return null;

        var keyPattern = $"(?im)^\\s*['\\\"']?{Regex.Escape(key)}['\\\"']?\\s*=\\s*";
        var match = Regex.Match(content, keyPattern, RegexOptions.CultureInvariant, RegexTimeout);
        if (!match.Success)
            return null;

        var start = match.Index + match.Length;
        if (start >= content.Length)
            return null;

        var sb = new StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inComment = false;

        for (var i = start; i < content.Length; i++)
        {
            var ch = content[i];

            if (inComment)
            {
                if (ch == '\r' || ch == '\n')
                {
                    inComment = false;
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                        break;
                    sb.Append(ch);
                }
                continue;
            }

            if (inSingleQuote)
            {
                sb.Append(ch);
                if (ch == '\'')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        continue;
                    }
                    inSingleQuote = false;
                }
                continue;
            }

            if (inDoubleQuote)
            {
                sb.Append(ch);
                if (ch == '`' && i + 1 < content.Length)
                {
                    sb.Append(content[i + 1]);
                    i++;
                    continue;
                }
                if (ch == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }
                    inDoubleQuote = false;
                }
                continue;
            }

            if (ch == '#')
            {
                inComment = true;
                continue;
            }

            if ((ch == '\r' || ch == '\n') && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                break;

            if (sb.Length == 0 && char.IsWhiteSpace(ch))
                continue;

            if (ch == '\'')
            {
                inSingleQuote = true;
                sb.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                sb.Append(ch);
                continue;
            }

            if (ch == '(')
                parenDepth++;
            else if (ch == ')' && parenDepth > 0)
                parenDepth--;
            else if (ch == '[')
                bracketDepth++;
            else if (ch == ']' && bracketDepth > 0)
                bracketDepth--;
            else if (ch == '{')
                braceDepth++;
            else if (ch == '}' && braceDepth > 0)
                braceDepth--;

            sb.Append(ch);
        }

        var expression = sb.ToString().Trim().TrimEnd(',').Trim();
        return string.IsNullOrWhiteSpace(expression) ? null : expression;
    }

    private static string NormalizePsd1ListBody(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;

        var trimmed = expression.Trim();
        if (trimmed.StartsWith("@(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal) && trimmed.Length >= 3)
            return trimmed.Substring(2, trimmed.Length - 3);

        return trimmed;
    }

    private static IEnumerable<string> ExtractQuotedStrings(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (Match match in Regex.Matches(value, "['\"](?<value>[^'\"]+)['\"]", RegexOptions.CultureInvariant, RegexTimeout))
        {
            var extracted = match.Groups["value"].Value?.Trim();
            if (!string.IsNullOrWhiteSpace(extracted))
                yield return extracted;
        }
    }

    private static string? ReadHashtableValue(string body, string key)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(key))
            return null;

        var pattern = $"(?im)\\b{Regex.Escape(key)}\\b\\s*=\\s*(?<value>('([^']|'')*')|(\"([^\"]|\"\")*\")|[^;\\r\\n]+)";
        var match = Regex.Match(body, pattern, RegexOptions.CultureInvariant, RegexTimeout);
        if (!match.Success)
            return null;

        return NormalizePsd1ScalarValue(match.Groups["value"].Value);
    }

    private static string? NormalizePsd1ScalarValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().TrimEnd(',').Trim();
        if (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal) && trimmed.Length >= 2)
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        else if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal) && trimmed.Length >= 2)
            trimmed = trimmed.Substring(1, trimmed.Length - 2);

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.Trim();
    }

    private static List<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('\'', '\"'))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToRelativePath(string path, string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return path;

        try
        {
            var relative = Path.GetRelativePath(baseDir, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
                return relative.Replace('\\', '/');
        }
        catch
        {
            // ignore
        }

        return path;
    }

    private static string? ResolveBaseDirectory(string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return null;
        try
        {
            return Path.GetFullPath(baseDir.Trim().Trim('\"'));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string path, string? baseDir, List<string> warnings, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim().Trim('\"');
        string full;
        try
        {
            full = Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(baseDir)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(baseDir, trimmed));
        }
        catch (Exception ex)
        {
            warnings.Add($"{label} could not be resolved: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(baseDir) && !IsUnderRoot(full, baseDir))
            warnings.Add($"{label} resolves outside base directory: {full}");

        return full;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        normalizedRoot += Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
