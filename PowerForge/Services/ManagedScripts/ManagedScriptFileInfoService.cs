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

    private static readonly string[] CommentHelpKeys =
    [
        "SYNOPSIS",
        "DESCRIPTION",
        "PARAMETER",
        "EXAMPLE",
        "INPUTS",
        "OUTPUTS",
        "NOTES",
        "LINK",
        "COMPONENT",
        "ROLE",
        "FUNCTIONALITY",
        "FORWARDHELPTARGETNAME",
        "FORWARDHELPCATEGORY",
        "REMOTEHELPRUNSPACE",
        "EXTERNALHELP"
    ];

    private static readonly string CommentHelpKeyPattern = string.Join("|", CommentHelpKeys.Select(Regex.Escape));

    private static readonly Regex PSScriptInfoRegex = new(
        @"^\s*<#PSScriptInfo\s*(?<body>.*?)#>\s*",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

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
        var prefix = AnalyzeScriptPrefix(text, match);
        var version = GetValue(values, "VERSION");
        var guidText = GetValue(values, "GUID");
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"Script '{fullPath}' PSScriptInfo block is missing VERSION.");
        ValidateScriptVersion(version!);
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
            Description = prefix.Description,
            RequiredModules = prefix.RequiredModules,
            RequiredModulesSpecified = true,
            ScriptHelp = prefix.ScriptHelp,
            ScriptContent = prefix.ScriptContent
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
            var info = Read(path);
            return !string.IsNullOrWhiteSpace(info.Version) &&
                   info.Guid != Guid.Empty &&
                   !string.IsNullOrWhiteSpace(info.Author) &&
                   !string.IsNullOrWhiteSpace(info.Description);
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
        ValidateScriptVersion(info.Version);

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
        else if (ContainsSignatureBlock(body))
            throw new InvalidOperationException("Script contains an Authenticode signature block. Use RemoveSignature before updating metadata, then re-sign the script.");
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
        ValidateScriptVersion(DefaultIfBlank(info.Version, "1.0.0.0"));

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

        builder.Append(RenderScriptHelp(info));

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
            .Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
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

    private static ScriptPrefixParts AnalyzeScriptPrefix(string text, Match metadataMatch)
    {
        var remainder = text.Substring(metadataMatch.Length);
        var requiredModules = new List<ManagedScriptRequiredModule>();
        var removeSpans = new List<TextSpan>();
        var preHelpRequires = new List<string>();
        var preHelpRequireSpans = new List<TextSpan>();
        var index = 0;
        string? scriptHelp = null;
        string description = string.Empty;

        while (true)
        {
            SkipBlankLines(remainder, ref index);

            if (scriptHelp is null &&
                TryReadCommentBlock(remainder, index, out var helpEnd) &&
                IsCommentHelpBlock(remainder.Substring(index, helpEnd - index)))
            {
                scriptHelp = remainder.Substring(index, helpEnd - index);
                description = ReadDescriptionFromHelp(scriptHelp) ?? string.Empty;
                removeSpans.Add(new TextSpan(index, helpEnd - index));
                index = helpEnd;
                continue;
            }

            if (scriptHelp is null &&
                TryReadLineCommentHelpBlock(remainder, index, out helpEnd, out var lineHelpBlock))
            {
                scriptHelp = lineHelpBlock;
                description = ReadDescriptionFromHelp(scriptHelp) ?? string.Empty;
                removeSpans.Add(new TextSpan(index, helpEnd - index));
                index = helpEnd;
                continue;
            }

            if (!TryReadLine(remainder, index, out var line, out var nextIndex))
                break;

            var trimmed = line.Trim();
            if (IsSkippablePrefixComment(trimmed))
            {
                index = nextIndex;
                continue;
            }

            if (!trimmed.StartsWith("#Requires", StringComparison.OrdinalIgnoreCase))
                break;

            var match = Regex.Match(
                line,
                @"^\s*#Requires\s+-(?<kind>[A-Za-z]+)\s+(?<value>.*?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success &&
                IsModuleRequiresKind(match.Groups["kind"].Value))
            {
                requiredModules.AddRange(ParseRequiredModuleList(match.Groups["value"].Value));
                removeSpans.Add(new TextSpan(index, nextIndex - index));
            }
            else if (match.Success && scriptHelp is null)
            {
                preHelpRequires.Add(line);
                preHelpRequireSpans.Add(new TextSpan(index, nextIndex - index));
            }

            index = nextIndex;
        }

        CollectActiveModuleRequires(remainder, requiredModules, removeSpans);
        if (scriptHelp is not null && preHelpRequires.Count > 0)
        {
            removeSpans.AddRange(preHelpRequireSpans);
            scriptHelp = string.Join(Environment.NewLine, preHelpRequires) + Environment.NewLine + Environment.NewLine + scriptHelp;
        }

        return new ScriptPrefixParts(
            requiredModules,
            scriptHelp,
            description,
            RemoveSpans(remainder, removeSpans).TrimStart('\r', '\n'));
    }

    private static bool IsSkippablePrefixComment(string trimmedLine)
        => trimmedLine.StartsWith("#", StringComparison.Ordinal) &&
           !trimmedLine.StartsWith("#Requires", StringComparison.OrdinalIgnoreCase);

    private static void CollectActiveModuleRequires(
        string text,
        ICollection<ManagedScriptRequiredModule> requiredModules,
        ICollection<TextSpan> removeSpans)
    {
        var index = 0;
        while (TryReadLine(text, index, out var line, out var nextIndex))
        {
            var match = Regex.Match(
                line,
                @"^\s*#Requires\s+-(?<kind>[A-Za-z]+)\s+(?<value>.*?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success &&
                IsModuleRequiresKind(match.Groups["kind"].Value) &&
                !removeSpans.Any(span => span.Overlaps(index, nextIndex - index)))
            {
                foreach (var requiredModule in ParseRequiredModuleList(match.Groups["value"].Value))
                    requiredModules.Add(requiredModule);
                removeSpans.Add(new TextSpan(index, nextIndex - index));
            }

            index = nextIndex;
        }
    }

    private static bool IsModuleRequiresKind(string kind)
        => string.Equals(kind, "Module", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(kind, "Modules", StringComparison.OrdinalIgnoreCase);

    private static void SkipBlankLines(string text, ref int index)
    {
        while (TryReadLine(text, index, out var line, out var nextIndex) &&
               string.IsNullOrWhiteSpace(line))
        {
            index = nextIndex;
        }
    }

    private static bool TryReadLine(string text, int index, out string line, out int nextIndex)
    {
        line = string.Empty;
        nextIndex = index;
        if (index >= text.Length)
            return false;

        var newlineIndex = text.IndexOf('\n', index);
        if (newlineIndex < 0)
        {
            line = text.Substring(index).TrimEnd('\r');
            nextIndex = text.Length;
            return true;
        }

        line = text.Substring(index, newlineIndex - index).TrimEnd('\r');
        nextIndex = newlineIndex + 1;
        return true;
    }

    private static bool TryReadCommentBlock(string text, int index, out int endIndex)
    {
        endIndex = index;
        if (index >= text.Length || !text.Substring(index).StartsWith("<#", StringComparison.Ordinal))
            return false;

        var closeIndex = text.IndexOf("#>", index + 2, StringComparison.Ordinal);
        if (closeIndex < 0)
            return false;

        endIndex = closeIndex + 2;
        return true;
    }

    private static bool IsCommentHelpBlock(string helpBlock)
        => Regex.IsMatch(
            helpBlock,
            $@"(?m)^\s*\.(?:{CommentHelpKeyPattern})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool TryReadLineCommentHelpBlock(string text, int index, out int endIndex, out string helpBlock)
    {
        endIndex = index;
        helpBlock = string.Empty;
        var current = index;
        var candidateBuilder = new StringBuilder();
        var originalBuilder = new StringBuilder();
        var readAnyComment = false;
        while (TryReadLine(text, current, out var line, out var nextIndex))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith("#Requires", StringComparison.OrdinalIgnoreCase))
                break;

            readAnyComment = true;
            var content = trimmed.Length == 1
                ? string.Empty
                : trimmed.Substring(1).TrimStart();
            candidateBuilder.AppendLine(content);
            originalBuilder.AppendLine(line);
            current = nextIndex;
        }

        if (!readAnyComment)
            return false;

        var candidate = candidateBuilder.ToString();
        if (!IsCommentHelpBlock(candidate))
            return false;

        endIndex = current;
        helpBlock = originalBuilder.ToString();
        return true;
    }

    private static string? ReadDescriptionFromHelp(string helpBlock)
    {
        var parseText = NormalizeHelpForParsing(helpBlock);
        var match = Regex.Match(
            parseText,
            $@"(?ms)^\s*\.DESCRIPTION\b\s*(?<description>.*?)(?=^\s*\.(?:{CommentHelpKeyPattern})\b\s*|\s*#>\s*\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? NormalizeBlockValue(match.Groups["description"].Value)
            : null;
    }

    private static IReadOnlyList<ManagedScriptRequiredModule> ParseRequiredModuleList(string value)
    {
        var modules = new List<ManagedScriptRequiredModule>();
        foreach (var entry in SplitTopLevelComma(value))
        {
            var trimmed = entry.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("@{", StringComparison.Ordinal))
                modules.Add(ParseRequiredModuleHashtable(trimmed));
            else
                modules.Add(new ManagedScriptRequiredModule { ModuleName = trimmed.Trim('\'', '"') });
        }

        return modules;
    }

    private static IReadOnlyList<string> SplitTopLevelComma(string value)
    {
        var values = new List<string>();
        var start = 0;
        var braceDepth = 0;
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character == '\'' || character == '"')
            {
                quote = character;
                continue;
            }

            if (character == '{')
                braceDepth++;
            else if (character == '}' && braceDepth > 0)
                braceDepth--;
            else if (character == ',' && braceDepth == 0)
            {
                values.Add(value.Substring(start, i - start));
                start = i + 1;
            }
        }

        values.Add(value.Substring(start));
        return values;
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
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion) &&
            (!string.IsNullOrWhiteSpace(module.ModuleVersion) || !string.IsNullOrWhiteSpace(module.MaximumVersion)))
            throw new InvalidOperationException("Required module entries cannot combine RequiredVersion with ModuleVersion or MaximumVersion.");

        if (string.IsNullOrWhiteSpace(module.Guid) &&
            string.IsNullOrWhiteSpace(module.ModuleVersion) &&
            string.IsNullOrWhiteSpace(module.RequiredVersion) &&
            string.IsNullOrWhiteSpace(module.MaximumVersion))
            return string.Format(CultureInfo.InvariantCulture, "#Requires -Module {0}", module.ModuleName);
        if (!string.IsNullOrWhiteSpace(module.Guid) &&
            string.IsNullOrWhiteSpace(module.ModuleVersion) &&
            string.IsNullOrWhiteSpace(module.RequiredVersion) &&
            string.IsNullOrWhiteSpace(module.MaximumVersion))
            throw new InvalidOperationException("Required module entries with Guid must include ModuleVersion, RequiredVersion, or MaximumVersion.");

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

    private static string RenderScriptHelp(ManagedScriptFileInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.ScriptHelp))
            return UpdateDescriptionInHelp(info.ScriptHelp!, info.Description);

        var builder = new StringBuilder();
        builder.AppendLine("<#");
        builder.AppendLine();
        builder.AppendLine(".DESCRIPTION");
        builder.AppendLine(DefaultIfBlank(info.Description, string.Empty));
        builder.AppendLine();
        builder.AppendLine("#>");
        return EnsureScriptHelpSeparator(builder.ToString());
    }

    private static string UpdateDescriptionInHelp(string helpBlock, string? description)
    {
        var existingDescription = ReadDescriptionFromHelp(helpBlock);
        var normalizedDescription = NormalizeBlockValue(description ?? string.Empty);
        if (string.Equals(existingDescription, normalizedDescription, StringComparison.Ordinal))
            return EnsureScriptHelpSeparator(helpBlock);

        if (IsLineCommentHelpBlock(helpBlock))
            return EnsureScriptHelpSeparator(UpdateDescriptionInLineCommentHelp(helpBlock, normalizedDescription));

        var match = Regex.Match(
            helpBlock,
            $@"(?ms)^\s*\.DESCRIPTION\b\s*(?<description>.*?)(?=^\s*\.(?:{CommentHelpKeyPattern})\b\s*|\s*#>\s*\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            var openingIndex = helpBlock.IndexOf("<#", StringComparison.Ordinal);
            if (openingIndex < 0)
                return EnsureScriptHelpSeparator(InsertDescriptionInLineCommentHelp(helpBlock, normalizedDescription));

            var insertIndex = openingIndex + 2;
            return EnsureScriptHelpSeparator(helpBlock.Insert(insertIndex, Environment.NewLine + Environment.NewLine + ".DESCRIPTION" + Environment.NewLine + normalizedDescription + Environment.NewLine));
        }

        var group = match.Groups["description"];
        var replacement = Environment.NewLine + normalizedDescription + Environment.NewLine + Environment.NewLine;
        return EnsureScriptHelpSeparator(helpBlock.Remove(group.Index, group.Length).Insert(group.Index, replacement));
    }

    private static string RenderGeneratedDescription(string description)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<#");
        builder.AppendLine();
        builder.AppendLine(".DESCRIPTION");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.AppendLine("#>");
        return EnsureScriptHelpSeparator(builder.ToString());
    }

    private static string NormalizeHelpForParsing(string helpBlock)
    {
        if (!IsLineCommentHelpBlock(helpBlock))
            return helpBlock;

        var builder = new StringBuilder();
        foreach (var line in SplitLines(helpBlock))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                builder.AppendLine(trimmed.Length == 1 ? string.Empty : trimmed.Substring(1).TrimStart());
            else
                builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static bool IsLineCommentHelpBlock(string helpBlock)
        => Regex.IsMatch(
            helpBlock,
            $@"(?m)^\s*#\s*\.(?:{CommentHelpKeyPattern})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string UpdateDescriptionInLineCommentHelp(string helpBlock, string description)
    {
        var lines = SplitLines(helpBlock).ToList();
        var descriptionIndex = FindLineCommentHelpKey(lines, "DESCRIPTION");
        if (descriptionIndex < 0)
            return InsertDescriptionInLineCommentHelp(helpBlock, description);

        var nextSectionIndex = lines.Count;
        for (var i = descriptionIndex + 1; i < lines.Count; i++)
        {
            if (TryGetLineCommentHelpKey(lines[i], out _))
            {
                nextSectionIndex = i;
                break;
            }
        }

        var commentPrefix = GetLineCommentPrefix(lines[descriptionIndex]);
        var replacement = RenderLineCommentDescription(commentPrefix, description);
        lines.RemoveRange(descriptionIndex + 1, nextSectionIndex - descriptionIndex - 1);
        lines.InsertRange(descriptionIndex + 1, replacement);
        return string.Join(Environment.NewLine, lines).TrimEnd('\r', '\n');
    }

    private static string InsertDescriptionInLineCommentHelp(string helpBlock, string description)
    {
        var lines = new List<string> { "# .DESCRIPTION" };
        lines.AddRange(RenderLineCommentDescription("#", description));
        lines.AddRange(SplitLines(helpBlock.TrimStart('\r', '\n')));
        return string.Join(Environment.NewLine, lines).TrimEnd('\r', '\n');
    }

    private static IReadOnlyList<string> RenderLineCommentDescription(string commentPrefix, string description)
    {
        var lines = new List<string>();
        foreach (var line in SplitLines(description))
            lines.Add(line.Length == 0 ? commentPrefix : commentPrefix + " " + line);

        lines.Add(commentPrefix);
        return lines;
    }

    private static int FindLineCommentHelpKey(IReadOnlyList<string> lines, string key)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (TryGetLineCommentHelpKey(lines[i], out var candidate) &&
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool TryGetLineCommentHelpKey(string line, out string key)
    {
        key = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            return false;

        var content = trimmed.Length == 1 ? string.Empty : trimmed.Substring(1).TrimStart();
        var match = Regex.Match(
            content,
            $@"^\.(?<key>{CommentHelpKeyPattern})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        key = match.Groups["key"].Value;
        return true;
    }

    private static string GetLineCommentPrefix(string line)
    {
        var hashIndex = line.IndexOf('#');
        return hashIndex < 0 ? "#" : line.Substring(0, hashIndex + 1);
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string EnsureScriptHelpSeparator(string value)
        => value.TrimEnd('\r', '\n') + Environment.NewLine + Environment.NewLine + Environment.NewLine;

    private static string RemoveSpans(string text, IReadOnlyList<TextSpan> spans)
    {
        if (spans.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var index = 0;
        foreach (var span in spans.OrderBy(static span => span.Start))
        {
            if (span.Start < index)
                continue;

            builder.Append(text, index, span.Start - index);
            index = span.Start + span.Length;
        }

        if (index < text.Length)
            builder.Append(text, index, text.Length - index);

        return builder.ToString();
    }

    private static string RemoveSignatureBlock(string text)
    {
        const string begin = "# SIG # Begin signature block";
        var index = text.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? text : text.Substring(0, index).TrimEnd() + Environment.NewLine;
    }

    private static bool ContainsSignatureBlock(string text)
        => text.IndexOf("# SIG # Begin signature block", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void ValidateScriptVersion(string version)
    {
        if (HasWhitespaceAroundSemVerSeparator(version))
            throw new InvalidOperationException($"Script version '{version}' is not a valid version.");

        var plusIndex = version.IndexOf('+');
        var versionWithoutBuild = plusIndex >= 0 ? version.Substring(0, plusIndex) : version;
        var build = plusIndex >= 0 ? version.Substring(plusIndex + 1) : null;
        if (plusIndex >= 0 &&
            (string.IsNullOrWhiteSpace(build) || !HasValidSemVerIdentifiers(build!)))
        {
            throw new InvalidOperationException($"Script version '{version}' is not a valid version.");
        }

        if (!ModuleStateVersion.TryParse(versionWithoutBuild, out _))
            throw new InvalidOperationException($"Script version '{version}' is not a valid version.");

        var prereleaseIndex = versionWithoutBuild.IndexOf('-');
        if (prereleaseIndex < 0)
            return;

        var prerelease = versionWithoutBuild.Substring(prereleaseIndex + 1);
        if (!HasValidSemVerIdentifiers(prerelease))
            throw new InvalidOperationException($"Script version '{version}' is not a valid version.");
    }

    private static bool HasWhitespaceAroundSemVerSeparator(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '+' && character != '-' && character != '.')
                continue;

            if (index > 0 && char.IsWhiteSpace(value[index - 1]))
                return true;
            if (index + 1 < value.Length && char.IsWhiteSpace(value[index + 1]))
                return true;
        }

        return false;
    }

    private static bool HasValidSemVerIdentifiers(string value)
    {
        var identifiers = value.Split('.');
        return identifiers.Length > 0 &&
               identifiers.All(static identifier =>
                   identifier.Length > 0 &&
                   identifier.All(static character =>
                       (character >= 'A' && character <= 'Z') ||
                       (character >= 'a' && character <= 'z') ||
                       (character >= '0' && character <= '9') ||
                       character == '-'));
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
            Tags = MergeList(existing.Tags, updates.Tags, updates.TagsSpecified),
            LicenseUri = updates.LicenseUri ?? existing.LicenseUri,
            ProjectUri = updates.ProjectUri ?? existing.ProjectUri,
            IconUri = updates.IconUri ?? existing.IconUri,
            ExternalModuleDependencies = MergeList(existing.ExternalModuleDependencies, updates.ExternalModuleDependencies, updates.ExternalModuleDependenciesSpecified),
            RequiredScripts = MergeList(existing.RequiredScripts, updates.RequiredScripts, updates.RequiredScriptsSpecified),
            ExternalScriptDependencies = MergeList(existing.ExternalScriptDependencies, updates.ExternalScriptDependencies, updates.ExternalScriptDependenciesSpecified),
            ReleaseNotes = updates.ReleaseNotes ?? existing.ReleaseNotes,
            PrivateData = updates.PrivateData ?? existing.PrivateData,
            Description = updates.Description ?? existing.Description,
            RequiredModules = MergeList(existing.RequiredModules, updates.RequiredModules, updates.RequiredModulesSpecified),
            RequiredModulesSpecified = existing.RequiredModulesSpecified,
            ScriptHelp = existing.ScriptHelp,
            ScriptContent = existing.ScriptContent
        };

    private static IReadOnlyList<T> MergeList<T>(IReadOnlyList<T> existing, IReadOnlyList<T> updates, bool specified)
        => specified || updates.Count > 0 ? updates : existing;

    private sealed class ScriptPrefixParts
    {
        public ScriptPrefixParts(
            IReadOnlyList<ManagedScriptRequiredModule> requiredModules,
            string? scriptHelp,
            string description,
            string scriptContent)
        {
            RequiredModules = requiredModules;
            ScriptHelp = scriptHelp;
            Description = description;
            ScriptContent = scriptContent;
        }

        public IReadOnlyList<ManagedScriptRequiredModule> RequiredModules { get; }

        public string? ScriptHelp { get; }

        public string Description { get; }

        public string ScriptContent { get; }
    }

    private readonly struct TextSpan
    {
        public TextSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }

        public bool Overlaps(int start, int length)
            => Start < start + length && start < Start + Length;
    }
}
