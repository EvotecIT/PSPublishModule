using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly string[] NodeVerbs =
    {
        "exec", "x", "dlx", "install", "i", "in", "ins", "inst", "insta", "instal",
        "isnt", "isnta", "isntal", "isntall", "add", "ci", "config", "c", "conf"
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
        if (Regex.IsMatch(normalized, @"^python\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "python";
        if (Regex.IsMatch(normalized, @"^pip\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "pip";
        return normalized;
    }
}
