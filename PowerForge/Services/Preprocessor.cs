using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Preprocesses PowerShell files by removing comments (scoped to param blocks or preamble)
/// and trimming empty lines as configured. Runs out-of-process to leverage PS AST reliably.
/// </summary>
internal sealed class Preprocessor
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    public Preprocessor(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public IReadOnlyList<FormatterResult> Process(IEnumerable<string> files, FormatOptions options)
    {
        var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (list.Length == 0) return Array.Empty<FormatterResult>();

        var script = BuildScript();
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "pre");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"pre_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));

        var flagsJson = JsonSerializer.Serialize(new PreprocessorFlags
        {
            RemoveCommentsInParamBlock = options.RemoveCommentsInParamBlock,
            RemoveCommentsBeforeParamBlock = options.RemoveCommentsBeforeParamBlock,
            RemoveAllEmptyLines = options.RemoveAllEmptyLines,
            RemoveEmptyLines = options.RemoveEmptyLines,
            Utf8Bom = options.Utf8Bom
        });
        var flagsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(flagsJson));
        var args = new List<string>(list.Length + 1) { flagsB64 };
        args.AddRange(list);
        var result = _runner.Run(new PowerShellRunRequest(scriptPath, args, TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds))));
        try { File.Delete(scriptPath); } catch { /* ignore */ }

        if (result.ExitCode != 0)
        {
            var reason = ExtractFirstLine(result.StdErr) ?? ExtractFirstLine(result.StdOut);
            var msg = string.IsNullOrWhiteSpace(reason)
                ? $"Preprocessor failed (exit {result.ExitCode})."
                : $"Preprocessor failed (exit {result.ExitCode}): {reason}";
            _logger.Error(msg);
            if (_logger.IsVerbose)
            {
                if (!string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
                if (!string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            }
        }

        if (result.ExitCode == 127)
        {
            _logger.Warn("Preprocessor: No PowerShell available; skipping.");
            return list.Select(p => new FormatterResult(p, false, "Skipped: No PowerShell runtime")).ToArray();
        }
        if (result.ExitCode == 124)
        {
            _logger.Warn("Preprocessor: Timeout; skipping.");
            return list.Select(p => new FormatterResult(p, false, "Skipped: Timeout")).ToArray();
        }

        var outputs = new List<FormatterResult>(list.Length);
        var lines = (result.StdOut ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("PRE::CHANGED::", StringComparison.Ordinal))
            {
                outputs.Add(new FormatterResult(line.Substring("PRE::CHANGED::".Length), true, "Preprocessed"));
            }
            else if (line.StartsWith("PRE::UNCHANGED::", StringComparison.Ordinal))
            {
                outputs.Add(new FormatterResult(line.Substring("PRE::UNCHANGED::".Length), false, "Unchanged"));
            }
            else if (line.StartsWith("PRE::ERROR::", StringComparison.Ordinal))
            {
                var rest = line.Substring("PRE::ERROR::".Length);
                var idx = rest.IndexOf("::", StringComparison.Ordinal);
                if (idx > 0)
                {
                    var p = rest.Substring(0, idx);
                    var msg = rest.Substring(idx + 2);
                    outputs.Add(new FormatterResult(p, false, $"Error: {msg}"));
                }
            }
        }
        foreach (var p in list)
        {
            if (!outputs.Any(o => string.Equals(o.Path, p, StringComparison.OrdinalIgnoreCase)))
            {
                if (result.ExitCode != 0)
                {
                    var reason = ExtractFirstLine(result.StdErr) ?? ExtractFirstLine(result.StdOut);
                    var extra = string.IsNullOrWhiteSpace(reason) ? string.Empty : $": {reason}";
                    outputs.Add(new FormatterResult(p, false, $"Error: Preprocessor failed (exit {result.ExitCode}){extra}"));
                }
                else
                {
                    outputs.Add(new FormatterResult(p, false, "Error: Preprocessor returned no result"));
                }
            }
        }
        return outputs;
    }

    private static string? ExtractFirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var line = part.Trim();
            if (line.Length == 0) continue;
            return line.Length > 200 ? line.Substring(0, 200) + "..." : line;
        }
        return null;
    }

    private static string BuildScript()
    {
        return EmbeddedScripts.Load("Scripts/Formatting/Invoke-Preprocessor.ps1");
    }

    private sealed class PreprocessorFlags
    {
        public bool RemoveCommentsInParamBlock { get; set; }
        public bool RemoveCommentsBeforeParamBlock { get; set; }
        public bool RemoveAllEmptyLines { get; set; }
        public bool RemoveEmptyLines { get; set; }
        public bool Utf8Bom { get; set; }
    }
}
