using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads and writes PSResourceGet-compatible script metadata without loading or executing the script.
/// </summary>
public sealed class ManagedScriptFileInfoService
{
    private static readonly string[] MetadataKeys =
    [
        "VERSION",
        "GUID",
        "AUTHOR",
        "COMPANYNAME",
        "COPYRIGHT",
        "TAGS",
        "LICENSEURI",
        "PROJECTURI",
        "ICONURI",
        "EXTERNALMODULEDEPENDENCIES",
        "REQUIREDSCRIPTS",
        "EXTERNALSCRIPTDEPENDENCIES",
        "RELEASENOTES",
        "PRIVATEDATA"
    ];

    private static readonly string MetadataKeyPattern = string.Join("|", MetadataKeys.Select(Regex.Escape));

    private static readonly Regex PSScriptInfoRegex = new(
        @"^\s*<#PSScriptInfo\s*(?<body>.*?)#>\s*",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex DescriptionRegex = new(
        @"<#\s*(?<body>.*?\.DESCRIPTION\s*(?<description>.*?))#>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex RequiresModuleRegex = new(
        @"^\s*#Requires\s+-Modules?\s+(?<value>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads script metadata from a local <c>.ps1</c> file.
    /// </summary>
    /// <param name="path">Script path.</param>
    /// <returns>Parsed script metadata.</returns>
    public ManagedScriptFileInfo Read(string path)
    {
        var fullPath = ResolveExistingScript(path);
        var text = File.ReadAllText(fullPath);
        var match = PSScriptInfoRegex.Match(text);
        if (!match.Success)
            throw new InvalidOperationException($"Script '{fullPath}' does not contain a PSScriptInfo metadata block.");

        var values = ParseMetadata(match.Groups["body"].Value);
        var version = GetValue(values, "VERSION");
        var guidText = GetValue(values, "GUID");
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"Script '{fullPath}' PSScriptInfo block is missing VERSION.");
        if (!Guid.TryParse(guidText, out var guid))
            throw new InvalidOperationException($"Script '{fullPath}' PSScriptInfo block has an invalid GUID.");

        return new ManagedScriptFileInfo
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty,
            Path = fullPath,
            Version = version!,
            Guid = guid,
            Author = GetValue(values, "AUTHOR"),
            CompanyName = GetValue(values, "COMPANYNAME"),
            Copyright = GetValue(values, "COPYRIGHT"),
            Tags = SplitWords(GetValue(values, "TAGS")),
            LicenseUri = GetValue(values, "LICENSEURI"),
            ProjectUri = GetValue(values, "PROJECTURI"),
            IconUri = GetValue(values, "ICONURI"),
            ExternalModuleDependencies = SplitWords(GetValue(values, "EXTERNALMODULEDEPENDENCIES")),
            RequiredScripts = SplitWords(GetValue(values, "REQUIREDSCRIPTS")),
            ExternalScriptDependencies = SplitWords(GetValue(values, "EXTERNALSCRIPTDEPENDENCIES")),
            ReleaseNotes = GetValue(values, "RELEASENOTES"),
            PrivateData = GetValue(values, "PRIVATEDATA"),
            Description = ReadDescription(text, match),
            RequiredModules = ReadRequiredModules(text),
            ScriptContent = RemoveMetadataPrefix(text, match)
        };
    }

    /// <summary>
    /// Validates that a local script file contains readable PSScriptInfo metadata.
    /// </summary>
    /// <param name="path">Script path.</param>
    /// <returns><c>true</c> when the script metadata can be read; otherwise <c>false</c>.</returns>
    public bool Test(string path)
    {
        try
        {
            _ = Read(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a script file with PSResourceGet-compatible metadata.
    /// </summary>
    /// <param name="info">Metadata to write.</param>
    /// <param name="overwrite">Overwrite an existing file.</param>
    /// <returns>Metadata for the written script.</returns>
    public ManagedScriptFileInfo Create(ManagedScriptFileInfo info, bool overwrite)
    {
        if (info is null)
            throw new ArgumentNullException(nameof(info));

        var path = ResolveTargetScript(info.Path);
        if (File.Exists(path) && !overwrite)
            throw new IOException($"Script '{path}' already exists. Use Force to overwrite it.");

        info.Path = path;
        if (info.Guid == Guid.Empty)
            info.Guid = Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(info.Version))
            info.Version = "1.0.0.0";

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Render(info, info.ScriptContent));
        return Read(path);
    }

    /// <summary>
    /// Updates an existing script file while preserving body content after the managed metadata prefix.
    /// </summary>
    /// <param name="path">Script path.</param>
    /// <param name="updates">Metadata values to apply.</param>
    /// <param name="removeSignature">Remove an Authenticode signature block from the preserved script body.</param>
    /// <returns>Updated script metadata.</returns>
    public ManagedScriptFileInfo Update(string path, ManagedScriptFileInfo updates, bool removeSignature)
    {
        if (updates is null)
            throw new ArgumentNullException(nameof(updates));

        var existing = Read(path);
        var merged = Merge(existing, updates);
        var body = existing.ScriptContent ?? string.Empty;
        if (removeSignature)
            body = RemoveSignatureBlock(body);
        merged.Path = existing.Path;
        merged.ScriptContent = body;
        File.WriteAllText(existing.Path, Render(merged, body));
        return Read(existing.Path);
    }

    /// <summary>
    /// Renders script metadata and body text.
    /// </summary>
    /// <param name="info">Metadata to render.</param>
    /// <param name="scriptContent">Optional script body to append.</param>
    /// <returns>Script text.</returns>
    public string Render(ManagedScriptFileInfo info, string? scriptContent = null)
    {
        if (info is null)
            throw new ArgumentNullException(nameof(info));

        var builder = new StringBuilder();
        builder.AppendLine("<#PSScriptInfo");
        builder.AppendLine();
        AppendSingle(builder, "VERSION", DefaultIfBlank(info.Version, "1.0.0.0"));
        AppendSingle(builder, "GUID", info.Guid == Guid.Empty ? Guid.NewGuid().ToString() : info.Guid.ToString());
        AppendSingle(builder, "AUTHOR", info.Author);
        AppendSingle(builder, "COMPANYNAME", info.CompanyName);
        AppendSingle(builder, "COPYRIGHT", info.Copyright);
        AppendSingle(builder, "TAGS", JoinWords(info.Tags));
        AppendSingle(builder, "LICENSEURI", info.LicenseUri);
        AppendSingle(builder, "PROJECTURI", info.ProjectUri);
        AppendSingle(builder, "ICONURI", info.IconUri);
        AppendSingle(builder, "EXTERNALMODULEDEPENDENCIES", JoinWords(info.ExternalModuleDependencies));
        AppendSingle(builder, "REQUIREDSCRIPTS", JoinWords(info.RequiredScripts));
        AppendSingle(builder, "EXTERNALSCRIPTDEPENDENCIES", JoinWords(info.ExternalScriptDependencies));
        AppendMultiline(builder, "RELEASENOTES", info.ReleaseNotes);
        AppendMultiline(builder, "PRIVATEDATA", info.PrivateData);
        builder.AppendLine("#>");
        builder.AppendLine();

        foreach (var requiredModule in info.RequiredModules)
            builder.AppendLine(RenderRequiredModule(requiredModule));

        if (info.RequiredModules.Count > 0)
            builder.AppendLine();

        builder.AppendLine("<#");
        builder.AppendLine();
        builder.AppendLine(".DESCRIPTION");
        builder.AppendLine(DefaultIfBlank(info.Description, string.Empty));
        builder.AppendLine();
        builder.AppendLine("#>");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(scriptContent))
            builder.Append(scriptContent!.TrimStart('\r', '\n'));

        return builder.ToString();
    }

    private static string ResolveExistingScript(string path)
    {
        var fullPath = ResolveTargetScript(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Script file was not found: {fullPath}", fullPath);
        return fullPath;
    }

    private static string ResolveTargetScript(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Script path is required.", nameof(path));

        var fullPath = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        if (!string.Equals(System.IO.Path.GetExtension(fullPath), ".ps1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Script path must end with .ps1.", nameof(path));
        return fullPath;
    }

    private static Dictionary<string, string> ParseMetadata(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(
            body,
            $@"(?ms)^\s*\.(?<key>{MetadataKeyPattern})\b\s*(?<value>.*?)(?=^\s*\.(?:{MetadataKeyPattern})\b\s*|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value.Trim();
            if (!MetadataKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            result[key] = NormalizeBlockValue(match.Groups["value"].Value);
        }

        return result;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string NormalizeBlockValue(string value)
        => value.Replace("\r\n", "\n").Trim('\n', '\r', ' ', '\t');

    private static IReadOnlyList<string> SplitWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value!
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(static entry => entry.Trim())
            .Where(static entry => entry.Length > 0)
            .ToArray();
    }

    private static string JoinWords(IEnumerable<string>? values)
        => values is null ? string.Empty : string.Join(" ", values.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private static string DefaultIfBlank(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : value!.Trim();

    private static void AppendSingle(StringBuilder builder, string key, string? value)
    {
        builder.Append('.').Append(key).Append(' ').AppendLine(value ?? string.Empty);
        builder.AppendLine();
    }

    private static void AppendMultiline(StringBuilder builder, string key, string? value)
    {
        builder.Append('.').AppendLine(key);
        builder.AppendLine(value ?? string.Empty);
        builder.AppendLine();
    }

    private static string ReadDescription(string text, Match metadataMatch)
    {
        var scriptHelpText = text.Substring(metadataMatch.Length);
        while (true)
        {
            var previous = scriptHelpText;
            scriptHelpText = RequiresModuleRegex.Replace(scriptHelpText, string.Empty, 1).TrimStart('\r', '\n', ' ', '\t');
            if (string.Equals(previous, scriptHelpText, StringComparison.Ordinal))
                break;
        }

        var match = Regex.Match(
            scriptHelpText,
            @"^\s*<#\s*(?<body>.*?\.DESCRIPTION\s*(?<description>.*?))#>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
            return string.Empty;

        return NormalizeDescription(match.Groups["description"].Value);
    }

    private static string NormalizeDescription(string value)
    {
        var index = value.IndexOf("\n.", StringComparison.Ordinal);
        if (index >= 0)
            value = value.Substring(0, index);
        return value.Replace("\r\n", "\n").Trim('\n', '\r', ' ', '\t');
    }

    private static IReadOnlyList<ManagedScriptRequiredModule> ReadRequiredModules(string text)
    {
        var modules = new List<ManagedScriptRequiredModule>();
        foreach (Match match in RequiresModuleRegex.Matches(text))
        {
            var value = match.Groups["value"].Value.Trim();
            if (value.StartsWith("@{", StringComparison.Ordinal))
            {
                modules.Add(ParseRequiredModuleHashtable(value));
            }
            else
            {
                modules.Add(new ManagedScriptRequiredModule { ModuleName = value.Trim('\'', '"') });
            }
        }

        return modules;
    }

    private static ManagedScriptRequiredModule ParseRequiredModuleHashtable(string value)
    {
        var module = new ManagedScriptRequiredModule();
        foreach (Match match in Regex.Matches(
                     value,
                     @"(?<key>ModuleName|Guid|ModuleVersion|RequiredVersion|MaximumVersion)\s*=\s*(?<quote>['""])(?<value>.*?)\k<quote>",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var key = match.Groups["key"].Value;
            var entryValue = match.Groups["value"].Value;
            if (string.Equals(key, "ModuleName", StringComparison.OrdinalIgnoreCase))
                module.ModuleName = entryValue;
            else if (string.Equals(key, "Guid", StringComparison.OrdinalIgnoreCase))
                module.Guid = entryValue;
            else if (string.Equals(key, "ModuleVersion", StringComparison.OrdinalIgnoreCase))
                module.ModuleVersion = entryValue;
            else if (string.Equals(key, "RequiredVersion", StringComparison.OrdinalIgnoreCase))
                module.RequiredVersion = entryValue;
            else if (string.Equals(key, "MaximumVersion", StringComparison.OrdinalIgnoreCase))
                module.MaximumVersion = entryValue;
        }

        if (string.IsNullOrWhiteSpace(module.ModuleName))
            module.ModuleName = value;

        return module;
    }

    private static string RenderRequiredModule(ManagedScriptRequiredModule module)
    {
        if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
            throw new InvalidOperationException("Required module entries must include ModuleName.");

        if (string.IsNullOrWhiteSpace(module.Guid) &&
            string.IsNullOrWhiteSpace(module.ModuleVersion) &&
            string.IsNullOrWhiteSpace(module.RequiredVersion) &&
            string.IsNullOrWhiteSpace(module.MaximumVersion))
            return string.Format(CultureInfo.InvariantCulture, "#Requires -Module {0}", module.ModuleName);

        var values = new List<string> { $"ModuleName = '{EscapeSingleQuoted(module.ModuleName)}'" };
        if (!string.IsNullOrWhiteSpace(module.Guid))
            values.Add($"Guid = '{EscapeSingleQuoted(module.Guid!)}'");
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
            values.Add($"ModuleVersion = '{EscapeSingleQuoted(module.ModuleVersion!)}'");
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion))
            values.Add($"RequiredVersion = '{EscapeSingleQuoted(module.RequiredVersion!)}'");
        if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
            values.Add($"MaximumVersion = '{EscapeSingleQuoted(module.MaximumVersion!)}'");

        return string.Format(CultureInfo.InvariantCulture, "#Requires -Module @{{ {0} }}", string.Join("; ", values));
    }

    private static string EscapeSingleQuoted(string value)
        => value.Replace("'", "''");

    private static string RemoveMetadataPrefix(string text, Match metadataMatch)
    {
        var remainder = text.Substring(metadataMatch.Length);
        remainder = RequiresModuleRegex.Replace(remainder, string.Empty);
        remainder = DescriptionRegex.Replace(remainder, string.Empty, 1);
        return remainder.TrimStart('\r', '\n');
    }

    private static string RemoveSignatureBlock(string text)
    {
        const string begin = "# SIG # Begin signature block";
        var index = text.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? text : text.Substring(0, index).TrimEnd() + Environment.NewLine;
    }

    private static ManagedScriptFileInfo Merge(ManagedScriptFileInfo existing, ManagedScriptFileInfo updates)
        => new()
        {
            Name = existing.Name,
            Path = existing.Path,
            Version = string.IsNullOrWhiteSpace(updates.Version) ? existing.Version : updates.Version,
            Guid = updates.Guid == Guid.Empty ? existing.Guid : updates.Guid,
            Author = updates.Author ?? existing.Author,
            CompanyName = updates.CompanyName ?? existing.CompanyName,
            Copyright = updates.Copyright ?? existing.Copyright,
            Tags = updates.Tags.Count == 0 ? existing.Tags : updates.Tags,
            LicenseUri = updates.LicenseUri ?? existing.LicenseUri,
            ProjectUri = updates.ProjectUri ?? existing.ProjectUri,
            IconUri = updates.IconUri ?? existing.IconUri,
            ExternalModuleDependencies = updates.ExternalModuleDependencies.Count == 0 ? existing.ExternalModuleDependencies : updates.ExternalModuleDependencies,
            RequiredScripts = updates.RequiredScripts.Count == 0 ? existing.RequiredScripts : updates.RequiredScripts,
            ExternalScriptDependencies = updates.ExternalScriptDependencies.Count == 0 ? existing.ExternalScriptDependencies : updates.ExternalScriptDependencies,
            ReleaseNotes = updates.ReleaseNotes ?? existing.ReleaseNotes,
            PrivateData = updates.PrivateData ?? existing.PrivateData,
            Description = updates.Description ?? existing.Description,
            RequiredModules = updates.RequiredModules.Count == 0 ? existing.RequiredModules : updates.RequiredModules,
            ScriptContent = existing.ScriptContent
        };
}
