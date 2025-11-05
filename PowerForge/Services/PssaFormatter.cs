using System.Text;

namespace PowerForge;

/// <summary>
/// Formats PowerShell scripts using PSScriptAnalyzer in an out-of-process PowerShell session.
/// Falls back gracefully if PSSA is missing or times out.
/// </summary>
public sealed class PssaFormatter : IFormatter
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

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

        var args = new List<string>(list.Length + 2) { settingsB64, "--" };
        args.AddRange(list);
        var result = _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout ?? TimeSpan.FromMinutes(2)));

        try { File.Delete(scriptPath); } catch { /* ignore */ }

        if (result.ExitCode == 127)
        {
            _logger.Warn("PSSA: No PowerShell available; skipping formatting.");
            return list.Select(p => new FormatterResult(p, false, "Skipped: No PowerShell runtime")).ToArray();
        }
        if (result.ExitCode == 124)
        {
            _logger.Warn("PSSA: Formatting timed out; skipping.");
            return list.Select(p => new FormatterResult(p, false, "Skipped: Timeout")).ToArray();
        }

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
        }

        // Ensure we return entries for all inputs (in case of missing outputs)
        foreach (var p in list)
        {
            if (!outputs.Any(o => string.Equals(o.Path, p, StringComparison.OrdinalIgnoreCase)))
            {
                outputs.Add(new FormatterResult(p, false, "No result returned"));
            }
        }

        return outputs;
    }

    /// <summary>
    /// PowerShell script executed out of process that imports PSScriptAnalyzer and formats files.
    /// </summary>
    private static string BuildScript()
    {
        return @"
param([string]$SettingsB64,[Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'
try {
    if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
        Write-Output 'PSSA_NOT_FOUND'
        exit 3
    }
    Import-Module PSScriptAnalyzer -ErrorAction Stop
} catch {
    Write-Output 'PSSA_NOT_FOUND'
    exit 3
}

$settings = $null
if ($SettingsB64) {
  try {
    $json = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($SettingsB64))
    $settings = ConvertFrom-Json -InputObject $json
  } catch {
    $settings = $null
  }
}

foreach ($f in $Files) {
  try {
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction Stop
    if ($null -ne $settings) {
        $formatted = Invoke-Formatter -ScriptDefinition $text -Settings $settings
    } else {
        $formatted = Invoke-Formatter -ScriptDefinition $text
    }
    if ($null -ne $formatted -and $formatted -ne $text) {
        [System.IO.File]::WriteAllText($f, $formatted, [System.Text.UTF8Encoding]::new($true))
        Write-Output (""FORMATTED::"" + $f)
    }
    else {
        Write-Output (""UNCHANGED::"" + $f)
    }
  } catch {
    Write-Output (""ERROR::"" + $f + ""::"" + $_.Exception.Message)
  }
}
exit 0
";
    }
}
