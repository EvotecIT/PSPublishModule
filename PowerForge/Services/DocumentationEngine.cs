using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Generates module documentation (markdown help) using out-of-process tools (PlatyPS/HelpOut).
/// </summary>
public sealed class DocumentationEngine
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance using the provided runner and logger.
    /// </summary>
    public DocumentationEngine(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates documentation for a built module.
    /// </summary>
    public DocumentationBuildResult Build(
        string moduleName,
        string stagingPath,
        string moduleManifestPath,
        DocumentationConfiguration documentation,
        BuildDocumentationConfiguration buildDocumentation,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(moduleManifestPath)) throw new ArgumentException("ModuleManifestPath is required.", nameof(moduleManifestPath));
        if (documentation is null) throw new ArgumentNullException(nameof(documentation));
        if (buildDocumentation is null) throw new ArgumentNullException(nameof(buildDocumentation));

        var enabled = buildDocumentation.Enable;
        if (!enabled)
        {
            return new DocumentationBuildResult(
                enabled: false,
                tool: buildDocumentation.Tool,
                docsPath: ResolvePath(stagingPath, documentation.Path),
                readmePath: ResolvePath(stagingPath, documentation.PathReadme),
                succeeded: true,
                exitCode: 0,
                markdownFiles: 0,
                errorMessage: null);
        }

        var docsPath = ResolvePath(stagingPath, documentation.Path);
        var readmePath = ResolvePath(stagingPath, documentation.PathReadme);
        var tool = buildDocumentation.Tool;
        var startClean = buildDocumentation.StartClean;
        var updateWhenNew = buildDocumentation.UpdateWhenNew;

        var script = BuildDocsScript();
        var args = new[]
        {
            moduleName,
            Path.GetFullPath(stagingPath),
            Path.GetFullPath(moduleManifestPath),
            docsPath,
            readmePath,
            tool.ToString(),
            startClean ? "1" : "0",
            updateWhenNew ? "1" : "0",
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(5));
        if (result.ExitCode != 0)
        {
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());

            var message = ExtractError(result.StdOut) ?? result.StdErr;
            return new DocumentationBuildResult(
                enabled: true,
                tool: tool,
                docsPath: docsPath,
                readmePath: readmePath,
                succeeded: false,
                exitCode: result.ExitCode,
                markdownFiles: CountMarkdownFiles(docsPath),
                errorMessage: string.IsNullOrWhiteSpace(message) ? "Documentation generation failed." : message.Trim());
        }

        return new DocumentationBuildResult(
            enabled: true,
            tool: tool,
            docsPath: docsPath,
            readmePath: readmePath,
            succeeded: true,
            exitCode: 0,
            markdownFiles: CountMarkdownFiles(docsPath),
            errorMessage: null);
    }

    private static string ResolvePath(string baseDir, string path)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static int CountMarkdownFiles(string docsPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(docsPath) || !Directory.Exists(docsPath)) return 0;
            return Directory.EnumerateFiles(docsPath, "*.md", SearchOption.AllDirectories).Count();
        }
        catch { return 0; }
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "docs");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"docs_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static string? ExtractError(string? stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFDOCS::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFDOCS::ERROR::".Length);
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
            catch { return null; }
        }
        return null;
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string BuildDocsScript()
    {
        return @"
param(
  [string]$ModuleName,
  [string]$StagingPath,
  [string]$ManifestPath,
  [string]$DocsPath,
  [string]$ReadmePath,
  [string]$Tool,
  [string]$StartCleanFlag,
  [string]$UpdateWhenNewFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function EmitError([string]$msg) {
  try {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
    Write-Output ('PFDOCS::ERROR::' + $b64)
  } catch {
    Write-Output 'PFDOCS::ERROR::'
  }
}

try {
  if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw ('Manifest not found: ' + $ManifestPath)
  }

  # PlatyPS resolves modules by name via PSModulePath. For staging builds, copy the module
  # to a temporary PSModulePath root (TempRoot/ModuleName) so New-MarkdownHelp can load it.
  $tempRoot = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'PowerForge', 'docs', 'modules', [Guid]::NewGuid().ToString('N'))
  $moduleRoot = [IO.Path]::Combine($tempRoot, $ModuleName)
  New-Item -ItemType Directory -Path $moduleRoot -Force | Out-Null
  Copy-Item -Path ([IO.Path]::Combine($StagingPath, '*')) -Destination $moduleRoot -Recurse -Force -ErrorAction Stop
  $env:PSModulePath = $tempRoot + [IO.Path]::PathSeparator + $env:PSModulePath

  $docs = $DocsPath
  $readme = $ReadmePath

  if ($StartCleanFlag -eq '1') {
    if (Test-Path -LiteralPath $docs) {
      try { Remove-Item -LiteralPath $docs -Recurse -Force -ErrorAction Stop } catch { }
    }
  }

  New-Item -ItemType Directory -Path $docs -Force | Out-Null
  if (-not [string]::IsNullOrWhiteSpace($readme)) {
    $readmeDir = Split-Path -Path $readme -Parent
    if (-not [string]::IsNullOrWhiteSpace($readmeDir)) { New-Item -ItemType Directory -Path $readmeDir -Force | Out-Null }
  }

  if ($Tool -eq 'HelpOut') {
    Import-Module HelpOut -ErrorAction Stop | Out-Null
  } else {
    Import-Module PlatyPS -ErrorAction Stop | Out-Null
  }

  Import-Module $ModuleName -Force -ErrorAction Stop | Out-Null

  if ($StartCleanFlag -eq '1') {
    $params = @{ Module = $ModuleName; OutputFolder = $docs; Force = $true; ErrorAction = 'Stop' }
    if (-not [string]::IsNullOrWhiteSpace($readme)) { $params.WithModulePage = $true; $params.ModulePagePath = $readme }
    New-MarkdownHelp @params | Out-Null

    if ($UpdateWhenNewFlag -eq '1') {
      $u = @{ Path = $docs; Force = $true; ErrorAction = 'Stop' }
      if (-not [string]::IsNullOrWhiteSpace($readme)) { $u.ModulePagePath = $readme; $u.RefreshModulePage = $true }
      Update-MarkdownHelpModule @u | Out-Null
    }
  } else {
    $u = @{ Path = $docs; Force = $true; ErrorAction = 'Stop' }
    if (-not [string]::IsNullOrWhiteSpace($readme)) { $u.ModulePagePath = $readme; $u.RefreshModulePage = $true }
    Update-MarkdownHelpModule @u | Out-Null
  }

  Write-Output 'PFDOCS::OK'
  exit 0
} catch {
  EmitError $_.Exception.Message
  exit 1
} finally {
  if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
    try { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
  }
}
";
    }
}
