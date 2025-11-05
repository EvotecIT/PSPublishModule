using System.Text;

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

        var flagsJson = "{" +
                        "\"RemoveCommentsInParamBlock\":" + (options.RemoveCommentsInParamBlock ? "true" : "false") + "," +
                        "\"RemoveCommentsBeforeParamBlock\":" + (options.RemoveCommentsBeforeParamBlock ? "true" : "false") + "," +
                        "\"RemoveAllEmptyLines\":" + (options.RemoveAllEmptyLines ? "true" : "false") + "," +
                        "\"RemoveEmptyLines\":" + (options.RemoveEmptyLines ? "true" : "false") + "," +
                        "\"Utf8Bom\":" + (options.Utf8Bom ? "true" : "false") +
                        "}";
        var flagsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(flagsJson));
        var args = new List<string>(list.Length + 2) { flagsB64, "--" };
        args.AddRange(list);
        var result = _runner.Run(new PowerShellRunRequest(scriptPath, args, TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds))));
        try { File.Delete(scriptPath); } catch { /* ignore */ }

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
                outputs.Add(new FormatterResult(p, false, "No result returned"));
            }
        }
        return outputs;
    }

    private static string BuildScript()
    {
        return @"
param([string]$FlagsB64,[Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'

try {
  $flags = $null
  if ($FlagsB64) {
    $json = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($FlagsB64))
    $flags = ConvertFrom-Json -InputObject $json
  }
} catch { $flags = $null }

foreach ($f in $Files) {
  try {
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction Stop
    $changed = $false

    if ($flags) {
      $tokens = $null; $errors = $null
      $ast = [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
      $paramStart = $null; $paramEnd = $null
      if ($ast -and $ast.ParamBlock) { $paramStart = $ast.ParamBlock.Extent.StartOffset; $paramEnd = $ast.ParamBlock.Extent.EndOffset }

      if ($flags.RemoveCommentsBeforeParamBlock -and $paramStart -ne $null) {
        $ranges = @()
        foreach ($t in $tokens) {
          if ($t.Kind -eq 'Comment' -and $t.Extent.StartOffset -lt $paramStart) {
            $ranges += ,@($t.Extent.StartOffset, $t.Extent.EndOffset)
          }
        }
        foreach ($r in ($ranges | Sort-Object -Descending { $_[0] })) {
          $text = $text.Remove($r[0], $r[1]-$r[0])
          $changed = $true
        }
      }

      if ($flags.RemoveCommentsInParamBlock -and $paramStart -ne $null -and $paramEnd -ne $null) {
        $ranges = @()
        foreach ($t in $tokens) {
          if ($t.Kind -eq 'Comment' -and $t.Extent.StartOffset -ge $paramStart -and $t.Extent.EndOffset -le $paramEnd) {
            $ranges += ,@($t.Extent.StartOffset, $t.Extent.EndOffset)
          }
        }
        foreach ($r in ($ranges | Sort-Object -Descending { $_[0] })) {
          $text = $text.Remove($r[0], $r[1]-$r[0])
          $changed = $true
        }
      }
    }

    # Empty lines handling
    if ($flags -and ($flags.RemoveAllEmptyLines -or $flags.RemoveEmptyLines)) {
      $lines = @()
      $prevEmpty = $false
      foreach ($line in ($text -split ""`r?`n"")) {
        $isEmpty = ($line.Trim().Length -eq 0)
        if ($flags.RemoveAllEmptyLines) {
          if (-not $isEmpty) { $lines += $line }
        } elseif ($flags.RemoveEmptyLines) {
          if ($isEmpty) { if (-not $prevEmpty) { $lines += '' } } else { $lines += $line }
          $prevEmpty = $isEmpty
        }
      }
      $newText = ($lines -join ""`r`n"")
      if ($newText -ne $text) { $text = $newText; $changed = $true }
    }

    if ($changed) {
      [System.IO.File]::WriteAllText($f, $text, [System.Text.UTF8Encoding]::new($true))
      Write-Output (""PRE::CHANGED::"" + $f)
    } else {
      Write-Output (""PRE::UNCHANGED::"" + $f)
    }
  } catch {
    Write-Output (""PRE::ERROR::"" + $f + ""::"" + $_.Exception.Message)
  }
}
exit 0
";
    }
}
