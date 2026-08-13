using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex CommandSegmentRegex = new(
        @"(?<command>(?:dotnet|dnx|Install-Module|Install-PSResource|npm|npx|pnpx|pnpm|yarn|bun|bunx|python(?:\d+(?:\.\d+)*)?|py|pip(?:\d+(?:\.\d+)*)?|uv|uvx|pipx|cargo|gem|composer)\b[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ShellTokenRegex = new(
        @"['""](?<quoted>[^'""]+)['""]|(?<plain>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IEnumerable<WebAgentPackageReference> ExtractPackageReferences(
        string content,
        string path,
        int lineOffset,
        List<WebAgentContentSecurityFinding> findings)
    {
        var references = new List<WebAgentPackageReference>();
        foreach (Match match in CommandSegmentRegex.Matches(content))
        {
            var tokens = Tokenize(match.Groups["command"].Value);
            if (tokens.Length < 2)
                continue;

            tokens[0] = NormalizeExecutable(tokens[0]);
            var executable = tokens[0];
            var line = lineOffset + GetLineNumber(content, match.Index);
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
                    ParsePositionalInstall("rubygems", "gem", tokens, new[] { "install" }, path, line, references, findings);
                    break;
                case "composer":
                    ParsePositionalInstall("packagist", "composer", tokens, new[] { "require" }, path, line, references, findings);
                    break;
            }
        }
        return references;
    }

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
        AddSingleOperand(
            "powershellgallery",
            tokens[0],
            tokens,
            nameIndex >= 0 ? nameIndex + 1 : 1,
            path,
            line,
            references,
            findings,
            nameIndex >= 0);
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
        var verb = tokens[1].ToLowerInvariant();
        if ((tokens[0].Equals("npm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("pnpm", StringComparison.OrdinalIgnoreCase)) &&
            verb is "exec" or "x")
        {
            var packageOption = FindOptionValue(tokens, 2, "--package", "-p");
            if (packageOption is not null)
                AddToken("npm", "npm exec", packageOption, null, path, line, references, findings);
            else
                AddRunnerOperand("npm", "npm exec", tokens, 2, path, line, references, findings);
            return;
        }
        if ((tokens[0].Equals("pnpm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("yarn", StringComparison.OrdinalIgnoreCase)) && verb == "dlx")
        {
            AddRunnerOperand("npm", tokens[0] + " dlx", tokens, 2, path, line, references, findings);
            return;
        }
        if (verb is not ("install" or "i" or "add"))
            return;
        AddMultipleOperands("npm", $"{tokens[0]} {tokens[1]}", tokens, 2, path, line, references, findings);
    }

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
            if (tokens.Length < 4 || tokens[1] != "-m" || !tokens[2].Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                !tokens[3].Equals("install", StringComparison.OrdinalIgnoreCase))
                return;
            AddMultipleOperands("pypi", $"{tokens[0]} -m pip install", tokens, 4, path, line, references, findings);
            return;
        }
        if (command is "pip" or "pip3")
        {
            if (tokens[1].Equals("install", StringComparison.OrdinalIgnoreCase))
                AddMultipleOperands("pypi", tokens[0] + " install", tokens, 2, path, line, references, findings);
            return;
        }
        if (command == "uv")
        {
            var start = tokens.Length > 2 && tokens[1].Equals("pip", StringComparison.OrdinalIgnoreCase) &&
                        tokens[2].Equals("install", StringComparison.OrdinalIgnoreCase)
                ? 3
                : tokens[1].Equals("add", StringComparison.OrdinalIgnoreCase) ? 2 : -1;
            if (start >= 0)
                AddMultipleOperands("pypi", "uv", tokens, start, path, line, references, findings);
            return;
        }
        if (command == "pipx" &&
            (tokens[1].Equals("install", StringComparison.OrdinalIgnoreCase) ||
             tokens[1].Equals("run", StringComparison.OrdinalIgnoreCase)))
            AddRunnerOperand("pypi", "pipx " + tokens[1], tokens, 2, path, line, references, findings);
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
        if (tokens.Length < 3 || !verbs.Contains(tokens[1], StringComparer.OrdinalIgnoreCase))
            return;
        if (!ValidatePackageSourceOptions(ecosystem, tokens, path, line, findings))
            return;
        AddMultipleOperands(ecosystem, command + " " + tokens[1], tokens, 2, path, line, references, findings);
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
                if (!TrySkipOption(tokens, ref index))
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
            if (ecosystem == "rubygems")
                break;
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
        if (!IsCandidatePackageId(token))
        {
            AddUnverifiableOperand(command, path, line, findings, token);
            return;
        }
        var (id, embeddedVersion) = SplitPackageVersion(ecosystem, token);
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

    private static int FindNextOperand(string[] tokens, int start)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
                continue;
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return index;
            if (!TrySkipOption(tokens, ref index))
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

    private static string? FindVersionOption(string[] tokens, int start)
        => FindOptionValue(tokens, start, "--version", "-v", "-Version", "-RequiredVersion");

    private static bool ValidatePackageSourceOptions(
        string ecosystem,
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        for (var index = 1; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var separator = token.IndexOf('=');
            var option = separator > 0 ? token[..separator] : token;
            if (!IsPackageSourceOption(ecosystem, option))
                continue;

            var value = separator > 0
                ? token[(separator + 1)..]
                : index + 1 < tokens.Length ? tokens[index + 1] : string.Empty;
            if (IsCanonicalPackageSource(ecosystem, option, value))
                continue;

            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                string.IsNullOrWhiteSpace(value)
                    ? $"Package source option '{option}' does not have a statically verifiable public-registry value."
                    : $"Package source option '{option}' redirects installation to untrusted source '{value}'.");
            return false;
        }
        return true;
    }

    private static bool IsPackageSourceOption(string ecosystem, string option)
        => ecosystem switch
        {
            "nuget" => option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--add-source", StringComparison.OrdinalIgnoreCase) ||
                       option.Equals("--configfile", StringComparison.OrdinalIgnoreCase),
            "powershellgallery" => option.Equals("-Repository", StringComparison.OrdinalIgnoreCase),
            "npm" => option.Equals("--registry", StringComparison.OrdinalIgnoreCase),
            "pypi" => option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--extra-index-url", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--find-links", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                      option.Equals("--config-file", StringComparison.OrdinalIgnoreCase),
            "crates" => option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("--index", StringComparison.OrdinalIgnoreCase),
            "rubygems" => option.Equals("--source", StringComparison.OrdinalIgnoreCase),
            "packagist" => option.Equals("--repository", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool IsCanonicalPackageSource(string ecosystem, string option, string value)
    {
        value = NormalizeToken(value).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ecosystem switch
        {
            "nuget" when option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
                          option.Equals("-s", StringComparison.OrdinalIgnoreCase) =>
                value.Equals("nuget.org", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("https://api.nuget.org/v3/index.json", StringComparison.OrdinalIgnoreCase),
            "powershellgallery" => value.Equals("PSGallery", StringComparison.OrdinalIgnoreCase),
            "npm" => value.Equals("https://registry.npmjs.org", StringComparison.OrdinalIgnoreCase),
            "pypi" when option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
                        option.Equals("-i", StringComparison.OrdinalIgnoreCase) =>
                value.Equals("https://pypi.org/simple", StringComparison.OrdinalIgnoreCase),
            "crates" when option.Equals("--registry", StringComparison.OrdinalIgnoreCase) =>
                value.Equals("crates-io", StringComparison.OrdinalIgnoreCase),
            "rubygems" => value.Equals("https://rubygems.org", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string[] Tokenize(string command)
        => ShellTokenRegex.Matches(command)
            .Select(static token => token.Groups["quoted"].Success ? token.Groups["quoted"].Value : token.Groups["plain"].Value)
            .Select(NormalizeToken)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

    private static string NormalizeExecutable(string executable)
    {
        var normalized = executable.ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.Ordinal) ||
            normalized.EndsWith(".cmd", StringComparison.Ordinal))
            normalized = Path.GetFileNameWithoutExtension(normalized);
        if (Regex.IsMatch(normalized, @"^python\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "python";
        if (Regex.IsMatch(normalized, @"^pip\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "pip";
        return normalized;
    }

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
            var separator = token.IndexOf(':');
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

    private static bool OptionConsumesValue(string option)
        => option.Equals("--index-url", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--extra-index-url", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--prefix", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--workspace", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--directory", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-C", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--requirement", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--constraint", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--registry", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--source", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--index", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Version", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-RequiredVersion", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--tag", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--group", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--python", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--repository", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Repository", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--find-links", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--config-file", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--scope", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Scope", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--framework", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--arch", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--runtime", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--project", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--configfile", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--tool-manifest", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--tool-path", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--add-source", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-MinimumVersion", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-MaximumVersion", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Credential", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Proxy", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-ProxyCredential", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Destination", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-DestinationPath", StringComparison.OrdinalIgnoreCase);

    private static bool OptionIsFlag(string option)
        => option.Equals("--global", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--local", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--prerelease", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-restore", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--interactive", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--ignore-failed-sources", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-cache", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--save-dev", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-D", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-save", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--exact", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-E", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--ignore-scripts", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--user", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--upgrade", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-U", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--pre", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-deps", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--dev", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--build", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--optional", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--locked", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--force", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-Force", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--user-install", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-document", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--no-interaction", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("--update-with-all-dependencies", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-W", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-AllowClobber", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-SkipPublisherCheck", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-AcceptLicense", StringComparison.OrdinalIgnoreCase) ||
           option.Equals("-TrustRepository", StringComparison.OrdinalIgnoreCase);

    private static bool TrySkipOption(string[] tokens, ref int index)
    {
        var option = tokens[index];
        if (option.Contains('=') || OptionIsFlag(option))
            return true;
        if (!OptionConsumesValue(option) || index + 1 >= tokens.Length)
            return false;
        index++;
        return true;
    }

    private static string NormalizeToken(string token)
        => token.Trim().Trim('\'', '"', (char)0x60, ',', ';');

    private static string? NormalizeVersion(string token)
    {
        var normalized = NormalizeToken(token).TrimStart('v');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsCandidatePackageId(string? id)
        => !string.IsNullOrWhiteSpace(id) &&
           id.Length <= 256 &&
           id[0] != '-' &&
           id.IndexOfAny(new[] { '$', '{', '}', '<', '>', '*', '?', '%', '(', ')' }) < 0 &&
           !Uri.TryCreate(id, UriKind.Absolute, out _);
}
