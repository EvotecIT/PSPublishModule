using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static readonly Regex SimpleRuntimeRequirement = new(
        @"^\s*#requires\s+-(?<kind>Version|PSEdition)\s+(?<value>[^\s#]+)\s*(?:#.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ConsolidateScriptRuntimeRequirements(
        string manifestRuntimePreamble,
        ref string preScriptContent,
        ref string moduleContent,
        ref string postScriptContent,
        string newline)
    {
        var requirements = new ScriptRuntimeRequirements();
        _ = ExtractSimpleRuntimeRequirements(manifestRuntimePreamble, requirements, newline);
        preScriptContent = ExtractSimpleRuntimeRequirements(preScriptContent, requirements, newline);
        moduleContent = ExtractSimpleRuntimeRequirements(moduleContent, requirements, newline);
        postScriptContent = ExtractSimpleRuntimeRequirements(postScriptContent, requirements, newline);
        return requirements.CreatePreamble(newline);
    }

    private static string ExtractSimpleRuntimeRequirements(
        string content,
        ScriptRuntimeRequirements requirements,
        string newline)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex] ?? string.Empty;
            if (state == ScriptLexicalState.Normal && blockCommentDepth == 0)
            {
                Match match = SimpleRuntimeRequirement.Match(line);
                if (match.Success && requirements.TryAdd(match.Groups["kind"].Value, match.Groups["value"].Value))
                    lines[lineIndex] = string.Empty;
            }

            UpdateScriptLexicalState(line, ref state, ref blockCommentDepth);
        }

        return string.Join(newline, lines).Trim('\r', '\n');
    }

    private sealed class ScriptRuntimeRequirements
    {
        private Version? _minimumVersion;
        private readonly HashSet<string> _editions = new(StringComparer.OrdinalIgnoreCase);

        internal bool TryAdd(string kind, string value)
        {
            if (string.Equals(kind, "Version", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = value.Trim('"', '\'');
                if (candidate.IndexOf('.') < 0)
                    candidate += ".0";
                if (!Version.TryParse(candidate, out Version? parsed))
                    return false;

                if (_minimumVersion is null || parsed > _minimumVersion)
                    _minimumVersion = parsed;
                return true;
            }

            if (!string.Equals(kind, "PSEdition", StringComparison.OrdinalIgnoreCase))
                return false;

            string edition = value.Trim('"', '\'');
            if (!string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(edition, "Desktop", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _editions.Add(edition);
            return true;
        }

        internal string CreatePreamble(string newline)
        {
            if (_editions.Count > 1)
            {
                throw new InvalidOperationException(
                    "Script artefact runtime requirements conflict: both Core and Desktop PSEditions are required.");
            }

            var lines = new List<string>(2);
            if (_minimumVersion is not null)
                lines.Add("#requires -Version " + _minimumVersion);
            if (_editions.Count == 1)
                lines.Add("#requires -PSEdition " + (_editions.Contains("Core") ? "Core" : "Desktop"));
            return string.Join(newline, lines);
        }
    }
}
