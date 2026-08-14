using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex CommandSegmentRegex = new(
        @"(?<![A-Za-z0-9_.-])(?<command>(?:dotnet|dnx|Install-Module|Install-PSResource|Update-Module|Update-PSResource|Register-PSRepository|Set-PSRepository|Register-PSResourceRepository|Set-PSResourceRepository|npm|npx|pnpx|pnpm|yarn|bun|bunx|python(?:\d+(?:\.\d+)*)?|py|pip(?:\d+(?:\.\d+)*)?|uv|uvx|pipx|cargo|gem|composer|bundle)\b(?:[^\x5C`\^\r\n;&|]|\^(?!\r?\n)|[\x5C`\^]\r?\n)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ObfuscatedExecutableRegex = new(
        @"(?<![A-Za-z0-9_.-])(?<command>(?:[A-Za-z0-9_.-]+[\x5C`\^][A-Za-z0-9_.\x5C`\^-]+|[A-Za-z0-9_.-]+(?:['""][A-Za-z0-9_.-]*['""][A-Za-z0-9_.-]*)+))(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex ShellContinuationRegex = new(
        @"[\x5C\`\^]\r?\n",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShellTokenRegex = new(
        @"['""](?<quoted>[^'""]+)['""]|(?<plain>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IEnumerable<WebAgentPackageReference> ExtractPackageReferences(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        List<WebAgentContentSecurityFinding> findings)
    {
        var references = new List<WebAgentPackageReference>();
        ScanObfuscatedPackageExecutables(content, path, lineOffset, countLogicalLines, findings);
        foreach (Match match in CommandSegmentRegex.Matches(content))
        {
            if (HasCommandScopedEnvironmentPrefix(content, match.Index))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Package commands with command-scoped environment assignments cannot be proven to use the canonical public registry.");
                continue;
            }
            var tokens = Tokenize(match.Groups["command"].Value);
            if (tokens.Length == 0)
                continue;

            tokens[0] = NormalizeExecutable(tokens[0]);
            var executable = tokens[0];
            var line = GetReportedLine(content, match.Index, lineOffset, countLogicalLines);
            if (tokens.Length < 2)
            {
                if (executable == "yarn")
                    AddUnverifiableOperand("yarn", path, line, findings, "lockfile dependency set");
                continue;
            }
            if (RejectPersistentPackageConfiguration(executable, tokens, path, line, findings))
                continue;
            switch (executable)
            {
                case "dotnet":
                    ParseDotNet(tokens, path, line, references, findings);
                    break;
                case "dnx":
                    if (ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
                        AddRunnerOperand("nuget", "dnx", tokens, 1, path, line, references, findings);
                    break;
                case "install-module":
                case "install-psresource":
                case "update-module":
                case "update-psresource":
                    ParsePowerShell(tokens, path, line, references, findings);
                    break;
                case "npm":
                case "pnpm":
                case "yarn":
                case "bun":
                    ParseNode(tokens, path, line, references, findings);
                    break;
                case "npx":
                case "pnpx":
                case "bunx":
                    if (ValidatePackageSourceOptions("npm", tokens, path, line, findings))
                        AddRunnerOperand("npm", tokens[0], tokens, 1, path, line, references, findings);
                    break;
                case "python":
                case "python3":
                case "py":
                case "pip":
                case "pip3":
                case "uv":
                case "pipx":
                    ParsePython(tokens, path, line, references, findings);
                    break;
                case "uvx":
                    if (ValidatePackageSourceOptions("pypi", tokens, path, line, findings))
                        AddRunnerOperand("pypi", "uvx", tokens, 1, path, line, references, findings);
                    break;
                case "cargo":
                    ParsePositionalInstall("crates", "cargo", tokens, new[] { "add", "install" }, path, line, references, findings);
                    break;
                case "gem":
                    ParsePositionalInstall("rubygems", "gem", tokens, new[] { "install", "i" }, path, line, references, findings);
                    break;
                case "composer":
                    ParseComposer(tokens, path, line, references, findings);
                    break;
                case "bundle":
                    ParseBundle(tokens, path, line, references, findings);
                    break;
            }
        }
        return references;
    }

    private static void ScanObfuscatedPackageExecutables(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (Match match in ObfuscatedExecutableRegex.Matches(content))
        {
            var escaped = match.Groups["command"].Value;
            var normalized = NormalizeExecutable(escaped.Replace("\\", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal)
                .Replace("^", string.Empty, StringComparison.Ordinal)
                .Replace("'", string.Empty, StringComparison.Ordinal)
                .Replace("\"", string.Empty, StringComparison.Ordinal));
            if (!IsSupportedPackageExecutable(normalized))
                continue;

            AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                $"Package-manager executable '{escaped}' uses shell token construction that obscures the command from static verification.");
        }
    }

    private static bool IsSupportedPackageExecutable(string executable)
        => executable is "dotnet" or "dnx" or "install-module" or "install-psresource" or
            "update-module" or "update-psresource" or
            "register-psrepository" or "set-psrepository" or "register-psresourcerepository" or
            "set-psresourcerepository" or "npm" or "npx" or "pnpx" or "pnpm" or "yarn" or
            "bun" or "bunx" or "python" or "py" or "pip" or "uv" or "uvx" or "pipx" or
            "cargo" or "gem" or "composer" or "bundle";

    private static void ParseDotNet(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 3)
            return;
        if (!ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
            return;
        if (tokens[1].Equals("tool", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("restore", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("dotnet tool restore", path, line, findings, "tool manifest dependency set");
            return;
        }
        if (tokens[1].Equals("tool", StringComparison.OrdinalIgnoreCase) &&
            (tokens[2].Equals("install", StringComparison.OrdinalIgnoreCase) ||
             tokens[2].Equals("update", StringComparison.OrdinalIgnoreCase)))
        {
            AddSingleOperand("nuget", "dotnet tool " + tokens[2], tokens, 3, path, line, references, findings);
            return;
        }
        if (tokens[1].Equals("package", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            AddSingleOperand("nuget", "dotnet package add", tokens, 3, path, line, references, findings);
            return;
        }
        if (!tokens[1].Equals("add", StringComparison.OrdinalIgnoreCase))
            return;
        var packageIndex = Array.FindIndex(tokens, 2, token => token.Equals("package", StringComparison.OrdinalIgnoreCase));
        if (packageIndex >= 0)
            AddSingleOperand("nuget", "dotnet add package", tokens, packageIndex + 1, path, line, references, findings);
    }

    private static void ParsePowerShell(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("powershellgallery", tokens, path, line, findings))
            return;
        var nameIndex = Array.FindIndex(tokens, 1, token => token.Equals("-Name", StringComparison.OrdinalIgnoreCase));
        AddPowerShellNames(tokens[0], tokens, nameIndex >= 0 ? nameIndex + 1 : 1, path, line, references, findings);
    }

    private static void ParseNode(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2)
            return;
        if (!ValidatePackageSourceOptions("npm", tokens, path, line, findings))
            return;
        var verbIndex = FindKnownVerbIndex(
            tokens, 1, NodeVerbs,
            tokens[0], path, line, findings);
        if (verbIndex < 0)
        {
            if (tokens[0].Equals("yarn", StringComparison.OrdinalIgnoreCase) &&
                !IsYarnInformationalInvocation(tokens))
            {
                AddUnverifiableOperand("yarn", path, line, findings, "lockfile dependency set");
            }
            return;
        }
        var verb = NormalizeNodeVerb(tokens[verbIndex]);
        if (verb is "config" or "set")
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command '{string.Join(' ', tokens)}' can change the registry used by later installation commands.");
            return;
        }
        if (verb == "ci")
        {
            AddUnverifiableOperand("npm ci", path, line, findings,
                "lockfile dependency set");
            return;
        }
        if ((tokens[0].Equals("npm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("pnpm", StringComparison.OrdinalIgnoreCase)) &&
            verb is "exec" or "x")
        {
            var packageOptions = FindOptionValues(tokens, verbIndex + 1, "--package", "-p");
            if (packageOptions.Count > 0)
            {
                foreach (var packageOption in packageOptions)
                {
                    if (string.IsNullOrWhiteSpace(packageOption))
                        AddUnverifiableOperand("npm exec", path, line, findings, "--package");
                    else
                        AddToken("npm", "npm exec", packageOption, null, path, line, references, findings);
                }
            }
            else
                AddRunnerOperand("npm", "npm exec", tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if ((tokens[0].Equals("pnpm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("yarn", StringComparison.OrdinalIgnoreCase)) && verb == "dlx")
        {
            AddRunnerOperand("npm", tokens[0] + " dlx", tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (verb == "init")
        {
            AddNodeInitializer(tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (verb is not ("install" or "add"))
            return;
        AddMultipleOperands("npm", $"{tokens[0]} {tokens[verbIndex]}", tokens, verbIndex + 1, path, line, references, findings);
    }

    private static bool IsYarnInformationalInvocation(string[] tokens)
        => tokens.Length > 1 && tokens.Skip(1).All(static token =>
            token.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("-h", StringComparison.OrdinalIgnoreCase));

    private static void ParsePython(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("pypi", tokens, path, line, findings))
            return;
        var command = tokens[0].ToLowerInvariant();
        if (command is "python" or "python3" or "py")
        {
            var moduleIndex = FindPythonModuleIndex(tokens, path, line, findings);
            if (moduleIndex < 0 || moduleIndex + 1 >= tokens.Length ||
                !tokens[moduleIndex + 1].Equals("pip", StringComparison.OrdinalIgnoreCase))
                return;
            var installIndex = FindVerbIndex(tokens, moduleIndex + 2, "install", $"{tokens[0]} -m pip", path, line, findings);
            if (installIndex >= 0)
                AddMultipleOperands("pypi", $"{tokens[0]} -m pip install", tokens, installIndex + 1, path, line, references, findings);
            return;
        }
        if (command is "pip" or "pip3")
        {
            var installIndex = FindVerbIndex(tokens, 1, "install", tokens[0], path, line, findings);
            if (installIndex >= 0)
                AddMultipleOperands("pypi", tokens[0] + " install", tokens, installIndex + 1, path, line, references, findings);
            return;
        }
        if (command == "uv")
        {
            var verbIndex = FindKnownVerbIndex(tokens, 1, new[] { "pip", "add", "tool", "run", "sync" }, "uv", path, line, findings);
            var start = -1;
            if (verbIndex >= 0 && tokens[verbIndex].Equals("pip", StringComparison.OrdinalIgnoreCase))
            {
                var installIndex = FindVerbIndex(tokens, verbIndex + 1, "install", "uv pip", path, line, findings);
                if (installIndex >= 0)
                    start = installIndex + 1;
            }
            else if (verbIndex >= 0 && tokens[verbIndex].Equals("tool", StringComparison.OrdinalIgnoreCase))
            {
                var installIndex = FindVerbIndex(tokens, verbIndex + 1, "install", "uv tool", path, line, findings);
                if (installIndex >= 0)
                    AddRunnerOperand("pypi", "uv tool install", tokens, installIndex + 1, path, line, references, findings);
                return;
            }
            else if (verbIndex >= 0)
            {
                if (tokens[verbIndex].Equals("sync", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnverifiableOperand("uv sync", path, line, findings, "lockfile or project dependency set");
                    return;
                }
                if (tokens[verbIndex].Equals("run", StringComparison.OrdinalIgnoreCase))
                {
                    var requirementInput = FindOptionValue(tokens, verbIndex + 1, "--with-requirements", "--with-editable");
                    if (requirementInput is not null)
                    {
                        AddUnverifiableOperand("uv run", path, line, findings, "external or editable dependency input");
                        return;
                    }
                    var dependencies = FindOptionValues(tokens, verbIndex + 1, "--with");
                    if (dependencies.Count == 0)
                        AddUnverifiableOperand("uv run", path, line, findings, "project dependency set");
                    else
                        foreach (var dependency in dependencies)
                            AddToken("pypi", "uv run --with", dependency, null, path, line, references, findings);
                    return;
                }
                start = verbIndex + 1;
            }
            if (start >= 0)
                AddMultipleOperands("pypi", "uv", tokens, start, path, line, references, findings);
            return;
        }
        if (command == "pipx")
        {
            if (FindOptionValue(tokens, 1, "--pip-args") is not null)
            {
                AddUnverifiableOperand("pipx", path, line, findings, "--pip-args");
                return;
            }
            var verbIndex = FindKnownVerbIndex(tokens, 1, new[] { "install", "run", "inject" }, "pipx", path, line, findings);
            if (verbIndex < 0)
                return;
            if (!tokens[verbIndex].Equals("inject", StringComparison.OrdinalIgnoreCase))
            {
                AddRunnerOperand("pypi", "pipx " + tokens[verbIndex], tokens, verbIndex + 1, path, line, references, findings);
                return;
            }

            var environmentIndex = FindNextOperand(tokens, verbIndex + 1);
            AddMultipleOperands("pypi", "pipx inject", tokens, environmentIndex < 0 ? tokens.Length : environmentIndex + 1,
                path, line, references, findings);
        }
    }

    private static void ParsePositionalInstall(
        string ecosystem,
        string command,
        string[] tokens,
        string[] verbs,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 3)
            return;
        if (!ValidatePackageSourceOptions(ecosystem, tokens, path, line, findings))
            return;
        if (ecosystem == "rubygems" && tokens.Any(static token =>
                token.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnverifiableOperand(command, path, line, findings, "RubyGems dependency file");
            return;
        }
        var verbIndex = FindKnownVerbIndex(tokens, 1, verbs, command, path, line, findings);
        if (verbIndex >= 0)
            AddMultipleOperands(ecosystem, command + " " + tokens[verbIndex], tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void AddRunnerOperand(
        string ecosystem,
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
        => AddSingleOperand(ecosystem, command, tokens, start, path, line, references, findings);

    private static void AddSingleOperand(
        string ecosystem,
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings,
        bool takeImmediateValue = false)
    {
        var index = takeImmediateValue ? start : FindNextOperand(tokens, start);
        if (index < 0 || index >= tokens.Length)
        {
            AddUnverifiableOperand(command, path, line, findings);
            return;
        }
        var version = FindVersionOption(tokens, 0);
        AddToken(ecosystem, command, tokens[index], version, path, line, references, findings);
    }

    private static void AddMultipleOperands(
        string ecosystem,
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var added = false;
        for (var index = start; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
                continue;
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                if (ecosystem == "rubygems" &&
                    (token.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                     token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase)))
                {
                    AddUnverifiableOperand(command, path, line, findings, "RubyGems dependency file");
                    return;
                }
                if (ecosystem == "rubygems" && token.Equals("-v", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length)
                {
                    index++;
                    continue;
                }
                if (!TrySkipOption(tokens, ref index, ecosystem))
                {
                    AddUnverifiableOperand(command, path, line, findings, token);
                    return;
                }
                continue;
            }
            var optionVersion = ecosystem is "crates" or "rubygems"
                ? FindVersionOption(tokens, 0)
                : null;
            AddToken(ecosystem, command, token, optionVersion, path, line, references, findings);
            added = true;
        }
        if (!added)
            AddUnverifiableOperand(command, path, line, findings);
    }

    private static void AddToken(
        string ecosystem,
        string command,
        string token,
        string? optionVersion,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        token = NormalizeToken(token);
        if (ecosystem == "npm" && TryGetNpmSelector(token, out var npmSelector) && !IsNpmRegistrySelector(npmSelector))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"npm package operand '{token}' uses a non-registry or unsupported package selector.");
            return;
        }
        var (id, embeddedVersion) = SplitPackageVersion(ecosystem, token);
        if (!IsCandidatePackageId(id))
        {
            AddUnverifiableOperand(command, path, line, findings, token);
            return;
        }
        if (ecosystem == "npm" && embeddedVersion is not null && !IsNpmRegistrySelector(embeddedVersion))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"npm package operand '{token}' uses a non-registry or unsupported package selector.");
            return;
        }
        references.Add(new WebAgentPackageReference
        {
            Ecosystem = ecosystem,
            Id = id,
            Version = optionVersion ?? embeddedVersion,
            Path = path,
            Line = line,
            Command = command
        });
    }

    private static int FindNextOperand(string[] tokens, int start, string? optionContext = null)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
                continue;
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return index;
            if (!TrySkipOption(tokens, ref index, optionContext))
                return -1;
        }
        return -1;
    }

    private static string? FindOptionValue(string[] tokens, int start, params string[] names)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            foreach (var name in names)
            {
                if (tokens[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return index + 1 < tokens.Length ? tokens[index + 1] : string.Empty;
                if (tokens[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return tokens[index][(name.Length + 1)..];
            }
        }
        return null;
    }

    private static List<string> FindOptionValues(string[] tokens, int start, params string[] names)
    {
        var values = new List<string>();
        for (var index = start; index < tokens.Length; index++)
        {
            foreach (var name in names)
            {
                if (tokens[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(index + 1 < tokens.Length ? tokens[++index] : string.Empty);
                    break;
                }
                if (tokens[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(tokens[index][(name.Length + 1)..]);
                    break;
                }
            }
        }
        return values;
    }

    private static int FindVerbIndex(
        string[] tokens,
        int start,
        string verb,
        string command,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            if (tokens[index].Equals(verb, StringComparison.OrdinalIgnoreCase))
                return index;
            if (tokens[index].StartsWith("-", StringComparison.Ordinal) && TrySkipOption(tokens, ref index, command))
                continue;
            AddUnverifiableOperand(command, path, line, findings, tokens[index]);
            return -1;
        }
        AddUnverifiableOperand(command, path, line, findings);
        return -1;
    }

    private static int FindKnownVerbIndex(
        string[] tokens,
        int start,
        IReadOnlyCollection<string> verbs,
        string command,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var candidateVerbIndex = Array.FindIndex(
            tokens,
            start,
            token => verbs.Contains(token, StringComparer.OrdinalIgnoreCase));
        if (candidateVerbIndex < 0)
            return -1;
        for (var index = start; index < tokens.Length; index++)
        {
            if (verbs.Contains(tokens[index], StringComparer.OrdinalIgnoreCase))
                return index;
            if (tokens[index].StartsWith("-", StringComparison.Ordinal) && TrySkipOption(tokens, ref index, command))
                continue;
            AddUnverifiableOperand(command, path, line, findings, tokens[index]);
            return -1;
        }
        return -1;
    }

    private static int FindPythonModuleIndex(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var moduleIndex = Array.FindIndex(tokens, 1, static token => token.Equals("-m", StringComparison.OrdinalIgnoreCase));
        if (moduleIndex < 0)
            return -1;
        for (var index = 1; index < moduleIndex; index++)
        {
            if (Regex.IsMatch(tokens[index], @"^-\d+(?:\.\d+)?$", RegexOptions.CultureInvariant))
                continue;
            if (tokens[index].StartsWith("-", StringComparison.Ordinal) && TrySkipOption(tokens, ref index))
                continue;
            AddUnverifiableOperand(tokens[0] + " -m pip", path, line, findings, tokens[index]);
            return -1;
        }
        return moduleIndex;
    }

    private static string? FindVersionOption(string[] tokens, int start)
        => FindOptionValue(tokens, start, "--version", "-v", "-Version", "-RequiredVersion");

    private static void AddUnverifiableOperand(
        string command,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings,
        string? operand = null)
        => AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND", path, line,
            string.IsNullOrWhiteSpace(operand)
                ? $"Package command '{command}' does not contain a statically verifiable package identifier."
                : $"Package command '{command}' uses dynamic or unsupported operand '{operand}'.");

    private static (string Id, string? Version) SplitPackageVersion(string ecosystem, string token)
    {
        token = NormalizeToken(token);
        if (ecosystem == "npm" || ecosystem == "nuget" && token.Contains('@'))
        {
            var separator = token.StartsWith('@') ? token.IndexOf('@', 1) : token.LastIndexOf('@');
            return separator > 0
                ? (token[..separator], NormalizeVersion(token[(separator + 1)..]))
                : (token, null);
        }
        if (ecosystem == "pypi")
        {
            var separator = token.IndexOfAny(new[] { '=', '<', '>', '!', '~' });
            if (separator > 0)
            {
                var id = TrimPythonExtras(token[..separator]);
                var suffix = token[separator..];
                return suffix.StartsWith("==", StringComparison.Ordinal)
                    ? (id, NormalizeVersion(suffix[2..]))
                    : (id, NormalizeVersion(suffix));
            }
            return (TrimPythonExtras(token), null);
        }
        if (ecosystem == "crates")
        {
            var separator = token.LastIndexOf('@');
            return separator > 0
                ? (token[..separator], NormalizeVersion(token[(separator + 1)..]))
                : (token, null);
        }
        if (ecosystem == "packagist")
        {
            var colon = token.IndexOf(':');
            var equals = token.IndexOf('=');
            var separator = colon < 0 ? equals : equals < 0 ? colon : Math.Min(colon, equals);
            return separator > 0
                ? (token[..separator], NormalizeVersion(token[(separator + 1)..]))
                : (token, null);
        }
        return (token, null);
    }

    private static string TrimPythonExtras(string token)
    {
        var bracket = token.IndexOf('[');
        return bracket > 0 ? token[..bracket] : token;
    }

    private static bool IsNpmRegistrySelector(string selector)
    {
        selector = selector.Trim();
        if (string.IsNullOrWhiteSpace(selector))
            return false;
        return !selector.Contains(':', StringComparison.Ordinal) &&
               !selector.StartsWith(".", StringComparison.Ordinal) &&
               !selector.StartsWith("/", StringComparison.Ordinal) &&
               !selector.StartsWith("\\", StringComparison.Ordinal) &&
               !selector.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetNpmSelector(string token, out string selector)
    {
        var separator = token.StartsWith('@') ? token.IndexOf('@', 1) : token.IndexOf('@');
        if (separator <= 0)
        {
            selector = string.Empty;
            return false;
        }
        selector = token[(separator + 1)..];
        return true;
    }

}
