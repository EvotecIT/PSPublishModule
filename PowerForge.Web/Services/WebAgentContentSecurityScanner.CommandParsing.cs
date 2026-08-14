using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static void ParseComposer(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("packagist", tokens, path, line, findings))
            return;
        var verbIndex = FindKnownVerbIndex(tokens, 1,
            new[] { "require", "install", "i", "create-project", "config", "repository", "repo", "update", "u", "upgrade", "reinstall", "remove", "rm", "uninstall" },
            "composer", path, line, findings);
        if (verbIndex < 0)
            return;
        if (tokens[verbIndex].Equals("config", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("repository", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("repo", StringComparison.OrdinalIgnoreCase))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command '{string.Join(' ', tokens)}' can change the source used by later installation commands.");
            return;
        }
        if (tokens[verbIndex].Equals("create-project", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("composer create-project", path, line, findings, "project dependency set");
            return;
        }
        if (tokens[verbIndex].Equals("update", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("u", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("upgrade", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("reinstall", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("remove", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("rm", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("uninstall", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("composer " + tokens[verbIndex], path, line, findings, "project dependency set");
            return;
        }
        if (tokens[verbIndex].Equals("install", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("i", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("composer install", path, line, findings, "lockfile dependency set");
            return;
        }
        AddMultipleOperands("packagist", "composer require", tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void ParseBundle(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("rubygems", tokens, path, line, findings))
            return;
        var verbIndex = FindKnownVerbIndex(tokens, 1, new[] { "add", "install", "config", "update" }, "bundle", path, line, findings);
        if (verbIndex < 0)
            return;
        if (tokens[verbIndex].Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command '{string.Join(' ', tokens)}' can change the source used by later installation commands.");
            return;
        }
        if (tokens[verbIndex].Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("bundle install", path, line, findings, "lockfile dependency set");
            return;
        }
        if (tokens[verbIndex].Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("bundle update", path, line, findings, "project dependency set");
            return;
        }
        AddMultipleOperands("rubygems", "bundle add", tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void ParseRubyGems(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2 || !ValidatePackageSourceOptions("rubygems", tokens, path, line, findings))
            return;
        var verbIndex = FindKnownVerbIndex(tokens, 1,
            new[] { "install", "i", "in", "ins", "inst", "insta", "instal", "update", "upd", "upda", "updat", "exec", "ex", "exe" },
            "gem", path, line, findings);
        if (verbIndex < 0)
            return;
        if (tokens[verbIndex].Equals("exec", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("ex", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("exe", StringComparison.OrdinalIgnoreCase))
        {
            var selectedGems = FindOptionValues(tokens, verbIndex + 1, "--gem", "-g");
            if (selectedGems.Count > 1 || selectedGems.Any(string.IsNullOrWhiteSpace))
            {
                AddUnverifiableOperand("gem exec", path, line, findings, "ambiguous --gem package selection");
                return;
            }
            if (selectedGems.Count == 1)
            {
                AddToken("rubygems", "gem exec", selectedGems[0], FindVersionOption(tokens, 0), path, line, references, findings);
                return;
            }
            AddRunnerOperand("rubygems", "gem exec", tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (tokens.Any(static token =>
                token.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnverifiableOperand("gem", path, line, findings, "RubyGems dependency file");
            return;
        }
        AddMultipleOperands("rubygems", "gem " + tokens[verbIndex], tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void AddNodeInitializer(
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var index = FindNextOperand(tokens, start);
        if (index < 0)
        {
            AddUnverifiableOperand("npm init", path, line, findings, "initializer package");
            return;
        }
        var operand = NormalizeToken(tokens[index]);
        var package = operand.StartsWith('@')
            ? operand.Contains('/')
                ? operand[..operand.IndexOf('/')] + "/create-" + operand[(operand.IndexOf('/') + 1)..]
                : operand + "/create"
            : "create-" + operand;
        AddToken("npm", "npm init", package, null, path, line, references, findings);
    }

    private static void AddPowerShellNames(
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
        var version = FindVersionOption(tokens, 0);
        for (var index = start; index < tokens.Length; index++)
        {
            if (tokens[index].StartsWith("-", StringComparison.Ordinal))
                break;
            foreach (var name in tokens[index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddToken(ecosystem, command, name, version, path, line, references, findings);
                added = true;
            }
        }
        if (!added)
            AddUnverifiableOperand(command, path, line, findings);
    }

    private static bool RejectPersistentPackageConfiguration(
        string executable,
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var verb = FindLeadingVerb(tokens, executable);
        var changesConfiguration = executable is "register-psrepository" or "set-psrepository" or
                "register-psresourcerepository" or "set-psresourcerepository" ||
            executable == "dotnet" && tokens.Length > 3 && tokens[1].Equals("nuget", StringComparison.OrdinalIgnoreCase) &&
                tokens[3].Equals("source", StringComparison.OrdinalIgnoreCase) ||
            executable == "gem" && verb == "sources" ||
            executable == "composer" && verb is "config" or "repository" or "repo" ||
            executable == "bundle" && verb == "config" ||
            executable is "npm" or "pnpm" or "yarn" or "bun" && NormalizeNodeVerb(verb ?? string.Empty) is "config" or "set" ||
            executable is "pip" or "pip3" && verb == "config" ||
            executable is "python" or "python3" or "py" && tokens.Length > 3 &&
                tokens[1].Equals("-m", StringComparison.OrdinalIgnoreCase) && tokens[2].Equals("pip", StringComparison.OrdinalIgnoreCase) &&
                tokens[3].Equals("config", StringComparison.OrdinalIgnoreCase) ||
            executable == "cargo" && verb == "config";
        if (!changesConfiguration)
            return false;

        AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
            $"Package-manager configuration command '{string.Join(' ', tokens)}' can change the source used by later installation commands.");
        return true;
    }

    private static string? FindLeadingVerb(string[] tokens, string optionContext)
    {
        for (var index = 1; index < tokens.Length; index++)
        {
            if (!tokens[index].StartsWith("-", StringComparison.Ordinal))
                return tokens[index].ToLowerInvariant();
            if (!TrySkipOption(tokens, ref index, optionContext))
                return null;
        }
        return null;
    }

    private static bool TrySkipOption(string[] tokens, ref int index, string? optionContext = null)
    {
        var option = tokens[index];
        var equals = option.IndexOf('=');
        if (equals > 0)
        {
            var name = option[..equals];
            return OptionConsumesValue(name, optionContext) || OptionIsFlag(name, optionContext);
        }
        if (OptionIsFlag(option, optionContext))
            return true;
        if (!OptionConsumesValue(option, optionContext) || index + 1 >= tokens.Length)
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

    private static readonly string[] NodeVerbs =
    {
        "exec", "x", "dlx", "install", "i", "in", "ins", "inst", "insta", "instal",
        "isnt", "isnta", "isntal", "isntall", "add", "ci", "clean-install", "ic", "install-clean", "isntall-clean",
        "install-test", "it", "ci-test", "cit", "install-ci-test", "install-clean-test", "clean-install-test", "sit",
        "update", "up", "upgrade", "udpate",
        "audit", "link", "ln", "dedupe", "ddp", "rebuild",
        "config", "c", "conf", "set", "init", "create", "innit"
    };

    private static string[] Tokenize(string command)
        => ShellTokenRegex.Matches(StripShellComment(ShellContinuationRegex.Replace(command, " ")))
            .Select(static token => token.Groups["quoted"].Success ? token.Groups["quoted"].Value : token.Groups["plain"].Value)
            .Select(NormalizeToken)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

    private static string NormalizeNodeVerb(string verb)
    {
        verb = verb.ToLowerInvariant();
        return verb switch
        {
            "i" or "in" or "ins" or "inst" or "insta" or "instal" or
                "isnt" or "isnta" or "isntal" or "isntall" => "install",
            "c" or "conf" => "config",
            "clean-install" or "ic" or "install-clean" or "isntall-clean" => "ci",
            "ci-test" or "cit" or "install-ci-test" or "install-clean-test" or "clean-install-test" or "sit" => "ci",
            "install-test" or "it" => "install",
            "up" or "upgrade" or "udpate" => "update",
            "ln" => "link",
            "ddp" => "dedupe",
            "create" or "innit" => "init",
            _ => verb
        };
    }

    private static string StripShellComment(string command)
    {
        char quote = '\0';
        var escaped = false;
        for (var index = 0; index < command.Length; index++)
        {
            var current = command[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (current == '\\' && quote != '\'')
            {
                escaped = true;
                continue;
            }
            if (quote != '\0')
            {
                if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }
            if (current == '#' && (index == 0 || char.IsWhiteSpace(command[index - 1])))
                return command[..index].TrimEnd();
        }
        return command;
    }

    private static string NormalizeExecutable(string executable)
    {
        var normalized = executable.ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.Ordinal) ||
            normalized.EndsWith(".cmd", StringComparison.Ordinal))
            normalized = Path.GetFileNameWithoutExtension(normalized);
        var launcherName = Path.GetFileName(normalized.Replace('\\', '/'));
        normalized = launcherName switch
        {
            "npm-cli.js" => "npm",
            "npx-cli.js" => "npx",
            "pnpm.cjs" => "pnpm",
            "pnpx.cjs" => "pnpx",
            "yarn.js" => "yarn",
            "yarnpkg.js" => "yarn",
            "composer.phar" => "composer",
            _ => normalized
        };
        if (normalized == "yarnpkg")
            return "yarn";
        if (Regex.IsMatch(normalized, @"^python\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "python";
        if (Regex.IsMatch(normalized, @"^pip\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "pip";
        return normalized;
    }
}
