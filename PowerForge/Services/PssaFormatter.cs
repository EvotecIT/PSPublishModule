using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Formats PowerShell scripts using PSScriptAnalyzer in an out-of-process PowerShell session.
/// Falls back gracefully if PSSA is missing or times out.
/// </summary>
public sealed class PssaFormatter : IFormatter
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;
    private PssaFormatterSession? _session;

    /// <summary>
    /// Receives (finished files, total files, file name) as the formatter reports each file. Called from a reader thread.
    /// </summary>
    internal Action<int, int, string>? Progress { get; set; }

    /// <summary>
    /// Creates a new formatter that uses the provided runner and logger.
    /// </summary>
    public PssaFormatter(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<FormatterResult> FormatFiles(IEnumerable<string> files, TimeSpan? timeout = null)
    {
        return FormatFilesWithSettings(files, settingsJson: null, timeout);
    }

    /// <inheritdoc />
    public IReadOnlyList<FormatterResult> FormatFilesWithSettings(IEnumerable<string> files, string? settingsJson, TimeSpan? timeout = null)
        => RunFormatter(files, settingsJson, batches: null, timeout);

    internal IReadOnlyList<FormatterResult> FormatBatches(IReadOnlyList<FormattingBatch> batches)
        => RunFormatter(
            batches.SelectMany(batch => batch.Files),
            settingsJson: null,
            batches,
            TimeSpan.FromSeconds(batches.Sum(batch => Math.Max(1, batch.Options.TimeoutSeconds))));

    /// <summary>Reuses a formatter host only within an explicitly owned build-phase scope.</summary>
    internal IDisposable? BeginSession()
    {
        if (_runner is not ICancellablePowerShellRunner runner) return null;
        if (_session is not null) throw new InvalidOperationException("A formatter session is already active.");
        _session = new PssaFormatterSession(runner, () => _session = null);
        return _session;
    }

    private IReadOnlyList<FormatterResult> RunFormatter(
        IEnumerable<string> files,
        string? settingsJson,
        IReadOnlyList<FormattingBatch>? batches,
        TimeSpan? timeout)
    {
        var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();  
        if (list.Length == 0) return Array.Empty<FormatterResult>();

        var script = BuildScript();
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "pssa");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"format_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));

        string settingsB64 = string.Empty;
        if (!string.IsNullOrEmpty(settingsJson))
        {
            var bytes = Encoding.UTF8.GetBytes(settingsJson);
            settingsB64 = Convert.ToBase64String(bytes);
        }

        var args = new List<string>(list.Length + 1) { settingsB64 };
        args.AddRange(list);
        string? batchPath = null;
        if (batches is not null)
        {
            batchPath = Path.Combine(tempDir, $"batch_{Guid.NewGuid():N}.json");
            File.WriteAllText(batchPath, JsonSerializer.Serialize(batches.Select(batch => new
            {
                batch.Files,
                SettingsJson = batch.Options.PssaSettingsJson
            })), new UTF8Encoding(false));
            args = new List<string> { "-BatchPath", batchPath };
        }
        var outputLineReceived = CreateProgressReader(Progress, list.Length);
        PowerShellRunResult result;
        try
        {
            result = _session is not null && batchPath is not null
                ? _session.Run(script, batchPath, timeout ?? TimeSpan.FromMinutes(2), outputLineReceived)
                : _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout ?? TimeSpan.FromMinutes(2))
                    .WithOutputLineReceived(outputLineReceived));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
            if (batchPath is not null)
            {
                try { File.Delete(batchPath); } catch { /* ignore */ }
            }
        }

        if (result.ExitCode == 127)
        {
            _logger.Warn("PSSA: No PowerShell available; skipping formatting.");
            return list.Select(p => new FormatterResult(p, false, "Skipped: No PowerShell runtime")).ToArray();
        }
        if (result.ExitCode == 124)
        {
            _logger.Warn("PSSA: Formatting timed out; skipping unfinished files.");
        }
        if (result.ExitCode == 3 || (result.StdOut ?? string.Empty).Contains("PSSA_NOT_FOUND", StringComparison.Ordinal))
        {
            _logger.Warn("PSSA: PSScriptAnalyzer not found; skipping formatting.");
            return list.Select(p => new FormatterResult(p, false, "Skipped: PSScriptAnalyzer not found")).ToArray();
        }
        LogFormatterFailure(result);

        // Parse lines: FORMATTED::<path> | UNCHANGED::<path> | ERROR::<path>::<message>
        var lines = (result.StdOut ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var outputs = new List<FormatterResult>(list.Length);
        foreach (var line in lines)
        {
            if (line.StartsWith("FORMATTED::", StringComparison.Ordinal))
            {
                outputs.Add(new FormatterResult(line.Substring("FORMATTED::".Length), true, "Formatted"));
            }
            else if (line.StartsWith("UNCHANGED::", StringComparison.Ordinal))
            {
                outputs.Add(new FormatterResult(line.Substring("UNCHANGED::".Length), false, "Unchanged"));
            }
            else if (line.StartsWith("ERROR::", StringComparison.Ordinal))
            {
                var rest = line.Substring("ERROR::".Length);
                var idx = rest.IndexOf("::", StringComparison.Ordinal);
                if (idx > 0)
                {
                    var p = rest.Substring(0, idx);
                    var msg = rest.Substring(idx + 2);
                    outputs.Add(new FormatterResult(p, false, $"Error: {msg}"));
                }
            }
            else if (line.StartsWith("WARNING::", StringComparison.Ordinal))
            {
                var rest = line.Substring("WARNING::".Length);
                var idx = rest.IndexOf("::", StringComparison.Ordinal);
                if (idx > 0)
                {
                    var p = rest.Substring(0, idx);
                    var msg = rest.Substring(idx + 2);
                    _logger.Warn($"PSSA: {p} - {msg}");
                }
            }
        }

        // Ensure we return entries for all inputs (in case of missing outputs)
        foreach (var p in list)
        {
            if (!outputs.Any(o => string.Equals(o.Path, p, StringComparison.OrdinalIgnoreCase)))
            {
                if (result.ExitCode == 124)
                    outputs.Add(new FormatterResult(p, false, "Skipped: Timeout"));
                else if (result.ExitCode != 0)
                    outputs.Add(new FormatterResult(p, false, $"Skipped: PSSA failed (exit {result.ExitCode})"));
                else
                    outputs.Add(new FormatterResult(p, false, "Error: PSSA returned no result"));
            }
        }

        return outputs;
    }

    // Files without a result are reported as skipped; the formatter error output says why.
    private void LogFormatterFailure(PowerShellRunResult result)
    {
        if (result.ExitCode == 0) return;
        var cause = ChildProcessOutput.Tail(result.StdErr);
        if (result.ExitCode == 124)
        {
            if (cause is not null) _logger.Warn($"PSSA: Formatter errors before the timeout:{Environment.NewLine}{cause}");
            return;
        }

        var message = $"PSSA: Formatter failed (exit {result.ExitCode}); files without a result are skipped.";
        _logger.Warn(cause is null ? message : $"{message}{Environment.NewLine}{cause}");
    }

    private static Action<string>? CreateProgressReader(Action<int, int, string>? progress, int total)
    {
        if (progress is null) return null;
        var finished = 0;
        return line =>
        {
            var path = ReadResultPath(line);
            if (path is null) return;
            var count = Interlocked.Increment(ref finished);
            try { progress(Math.Min(count, total), total, Path.GetFileName(path)); } catch { /* progress is best effort */ }
        };
    }

    private static string? ReadResultPath(string? line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        if (line!.StartsWith("FORMATTED::", StringComparison.Ordinal)) return line.Substring("FORMATTED::".Length);
        if (line.StartsWith("UNCHANGED::", StringComparison.Ordinal)) return line.Substring("UNCHANGED::".Length);
        if (!line.StartsWith("ERROR::", StringComparison.Ordinal)) return null;
        var rest = line.Substring("ERROR::".Length);
        var separator = rest.IndexOf("::", StringComparison.Ordinal);
        return separator > 0 ? rest.Substring(0, separator) : null;
    }

    /// <summary>
    /// PowerShell script executed out of process that imports PSScriptAnalyzer and formats files.
    /// </summary>
    private static string BuildScript()
    {
        return EmbeddedScripts.Load("Scripts/Formatting/Invoke-PssaFormatter.ps1");
    }
}
