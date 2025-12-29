using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PowerForge;

/// <summary>
/// Generates module documentation (markdown help) using the PowerForge built-in generator.
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
        return BuildWithProgress(
            moduleName: moduleName,
            stagingPath: stagingPath,
            moduleManifestPath: moduleManifestPath,
            documentation: documentation,
            buildDocumentation: buildDocumentation,
            timeout: timeout,
            progress: null,
            extractStep: null,
            writeStep: null,
            externalHelpStep: null);
    }

    internal DocumentationBuildResult BuildWithProgress(
        string moduleName,
        string stagingPath,
        string moduleManifestPath,
        DocumentationConfiguration documentation,
        BuildDocumentationConfiguration buildDocumentation,
        TimeSpan? timeout,
        IModulePipelineProgressReporter? progress,
        ModulePipelineStep? extractStep,
        ModulePipelineStep? writeStep,
        ModulePipelineStep? externalHelpStep)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(moduleManifestPath)) throw new ArgumentException("ModuleManifestPath is required.", nameof(moduleManifestPath));
        if (documentation is null) throw new ArgumentNullException(nameof(documentation));
        if (buildDocumentation is null) throw new ArgumentNullException(nameof(buildDocumentation));

        if (!buildDocumentation.Enable)
        {
            return new DocumentationBuildResult(
                enabled: false,
                tool: DocumentationTool.PowerForge,
                docsPath: ResolvePath(stagingPath, documentation.Path),
                readmePath: ResolvePath(stagingPath, documentation.PathReadme),
                succeeded: true,
                exitCode: 0,
                markdownFiles: 0,
                externalHelpFilePath: string.Empty,
                errorMessage: null);
        }

        var docsPath = ResolvePath(stagingPath, documentation.Path);
        var readmePath = ResolvePath(stagingPath, documentation.PathReadme);
        var startClean = buildDocumentation.StartClean;

        void SafeStart(ModulePipelineStep? step)
        {
            if (step is null || progress is null) return;
            try { progress.StepStarting(step); } catch { /* best effort */ }
        }

        void SafeDone(ModulePipelineStep? step)
        {
            if (step is null || progress is null) return;
            try { progress.StepCompleted(step); } catch { /* best effort */ }
        }

        void SafeFail(ModulePipelineStep? step, Exception ex)
        {
            if (step is null || progress is null) return;
            try { progress.StepFailed(step, ex); } catch { /* best effort */ }
        }

        try
        {
            if (startClean)
            {
                SafeDeleteDocsFolder(stagingPath, docsPath);
            }

            Directory.CreateDirectory(docsPath);
            if (!string.IsNullOrWhiteSpace(readmePath))
                Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);

            DocumentationExtractionPayload extracted;
            SafeStart(extractStep);
            try
            {
                extracted = ExtractHelpAsJson(
                    stagingPath: stagingPath,
                    moduleManifestPath: moduleManifestPath,
                    timeout: timeout ?? TimeSpan.FromMinutes(5));
                SafeDone(extractStep);
            }
            catch (Exception ex)
            {
                SafeFail(extractStep, ex);
                throw;
            }

            var writer = new MarkdownHelpWriter();
            SafeStart(writeStep);
            try
            {
                writer.WriteCommandHelpFiles(extracted, moduleName, docsPath);
                if (!string.IsNullOrWhiteSpace(readmePath))
                    writer.WriteModuleReadme(extracted, moduleName, readmePath, docsPath);
                SafeDone(writeStep);
            }
            catch (Exception ex)
            {
                SafeFail(writeStep, ex);
                throw;
            }

            var externalHelpFile = string.Empty;
            if (buildDocumentation.GenerateExternalHelp)
            {
                var culture = NormalizeExternalHelpCulture(buildDocumentation.ExternalHelpCulture);
                var externalHelpDir = Path.Combine(stagingPath, culture);
                var fileName = string.IsNullOrWhiteSpace(buildDocumentation.ExternalHelpFileName)
                    ? null
                    : Path.GetFileName(buildDocumentation.ExternalHelpFileName.Trim());

                var mamlWriter = new MamlHelpWriter();
                SafeStart(externalHelpStep);
                try
                {
                    externalHelpFile = mamlWriter.WriteExternalHelpFile(extracted, moduleName, externalHelpDir, fileName);
                    SafeDone(externalHelpStep);
                }
                catch (Exception ex)
                {
                    SafeFail(externalHelpStep, ex);
                    throw;
                }
            }

            return new DocumentationBuildResult(
                enabled: true,
                tool: DocumentationTool.PowerForge,
                docsPath: docsPath,
                readmePath: readmePath,
                succeeded: true,
                exitCode: 0,
                markdownFiles: CountMarkdownFiles(docsPath),
                externalHelpFilePath: externalHelpFile,
                errorMessage: null);
        }
        catch (Exception ex)
        {
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());

            return new DocumentationBuildResult(
                enabled: true,
                tool: DocumentationTool.PowerForge,
                docsPath: docsPath,
                readmePath: readmePath,
                succeeded: false,
                exitCode: 1,
                markdownFiles: CountMarkdownFiles(docsPath),
                externalHelpFilePath: string.Empty,
                errorMessage: string.IsNullOrWhiteSpace(ex.Message) ? "Documentation generation failed." : ex.Message.Trim());
        }
    }

    private DocumentationExtractionPayload ExtractHelpAsJson(
        string stagingPath,
        string moduleManifestPath,
        TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "docs");
        Directory.CreateDirectory(tempDir);
        var id = Guid.NewGuid().ToString("N");
        var scriptPath = Path.Combine(tempDir, $"docs_extract_{id}.ps1");
        var jsonPath = Path.Combine(tempDir, $"docs_extract_{id}.json");

        File.WriteAllText(scriptPath, BuildHelpExportScript(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            var args = new[]
            {
                Path.GetFullPath(stagingPath),
                Path.GetFullPath(moduleManifestPath),
                Path.GetFullPath(jsonPath)
            };

            var result = _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
            if (result.ExitCode != 0)
            {
                if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
                if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());

                var message = ExtractError(result.StdOut) ?? result.StdErr;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Documentation help extraction failed." : message.Trim());
            }

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Documentation help extraction did not produce output JSON.", jsonPath);

            using var stream = File.OpenRead(jsonPath);
            var serializer = new DataContractJsonSerializer(typeof(DocumentationExtractionPayload));
            var payload = serializer.ReadObject(stream) as DocumentationExtractionPayload;
            return payload ?? new DocumentationExtractionPayload();
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
            try { File.Delete(jsonPath); } catch { /* ignore */ }
        }
    }

    private static string ResolvePath(string baseDir, string path)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return Path.GetFullPath(baseDir);     
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static string NormalizeExternalHelpCulture(string? culture)
    {
        var value = (culture ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value)) value = "en-US";

        // Avoid path traversal and invalid characters; culture should be a folder name like "en-US".
        value = value.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "en-US" : value;
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

    private static void SafeDeleteDocsFolder(string stagingPath, string docsPath)
    {
        if (string.IsNullOrWhiteSpace(docsPath) || !Directory.Exists(docsPath)) return;

        var fullDocs = Path.GetFullPath(docsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullStaging = Path.GetFullPath(stagingPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullDocs, fullStaging, StringComparison.OrdinalIgnoreCase))
        {
            // Never delete the whole staging folder; clean markdown files only.
            foreach (var md in Directory.EnumerateFiles(fullDocs, "*.md", SearchOption.AllDirectories))
            {
                try { File.Delete(md); } catch { /* best effort */ }
            }
            return;
        }

        try { Directory.Delete(fullDocs, recursive: true); } catch { /* best effort */ }
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

    private static string[] SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string BuildHelpExportScript()
    {
        return @"
param(
  [string]$StagingPath,
  [string]$ManifestPath,
  [string]$OutputJsonPath
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

function GetText([object]$obj) {
  if ($null -eq $obj) { return '' }
  if ($obj -is [string]) { return [string]$obj }
  try { if ($obj.PSObject -and $obj.PSObject.Properties['Text']) { return [string]$obj.Text } } catch { }
  try { return [string]$obj } catch { return '' }
}

try {
  if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw ('Manifest not found: ' + $ManifestPath)
  }

  $m = $null
  try { $m = Import-PowerShellDataFile -Path $ManifestPath -ErrorAction Stop } catch { $m = $null }

  $mod = Import-Module -Name $ManifestPath -Force -PassThru -ErrorAction Stop
  $moduleNameResolved = $mod.Name

  $commands = Get-Command -Module $moduleNameResolved -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandType -eq 'Cmdlet' -or $_.CommandType -eq 'Function'
  } | Sort-Object -Property Name

  $result = [ordered]@{
    moduleName = [string]$moduleNameResolved
    moduleGuid = if ($m -and $m.GUID) { [string]$m.GUID } else { $null }
    moduleDescription = if ($m -and $m.Description) { [string]$m.Description } else { $null }
    commands = @()
  }

  foreach ($c in $commands) {
    $help = $null
    try { $help = Get-Help -Name $c.Name -Full -ErrorAction SilentlyContinue } catch { $help = $null }

    $defaultSet = $null
    try { $defaultSet = $c.DefaultParameterSet } catch { $defaultSet = $null }

    $commandParameterSets = @()
    if ($null -ne $c -and $null -ne $c.ParameterSets) { $commandParameterSets = @($c.ParameterSets) }

    $syntax = @()
    foreach ($ps in $commandParameterSets) {
      $syntax += [ordered]@{
        name = [string]$ps.Name
        isDefault = if ($defaultSet) { [bool]($ps.Name -eq $defaultSet) } else { $false }
        text = ([string]$c.Name + ' ' + [string]$ps.ToString())
      }
    }

    $paramSets = @{}
    foreach ($ps in $commandParameterSets) {
      $psParameters = @()
      if ($null -ne $ps -and $null -ne $ps.Parameters) { $psParameters = @($ps.Parameters) }
      foreach ($pp in $psParameters) {
        $pn = [string]$pp.Name
        if (-not $paramSets.ContainsKey($pn)) { $paramSets[$pn] = New-Object System.Collections.Generic.List[string] }
        $null = $paramSets[$pn].Add([string]$ps.Name)
      }
    }

    $parameters = @()
    foreach ($p in @($help.Parameters.Parameter)) {
      $pn = [string]$p.Name
      $aliases = @()
      foreach ($a in @($p.Aliases)) { $aliases += [string]$a }

      $desc = ''
      foreach ($d in @($p.Description)) {
        $t = (GetText $d).Trim()
        if ($t) { if ($desc) { $desc += ""`n`n"" }; $desc += $t }
      }

      $sets = @()
      if ($paramSets.ContainsKey($pn)) { $sets = @($paramSets[$pn]) }
      if (-not $sets -or $sets.Count -eq 0) { $sets = @('(All)') }

      $parameters += [ordered]@{
        name = $pn
        type = $(try { [string]$p.Type.Name } catch { '' })
        description = $desc
        parameterSets = @($sets)
        aliases = @($aliases)
        required = $(try { [bool]$p.Required } catch { $false })
        position = $(try { [string]$p.Position } catch { '' })
        defaultValue = $(try { [string]$p.DefaultValue } catch { '' })
        pipelineInput = $(try { [string]$p.PipelineInput } catch { '' })
        acceptWildcardCharacters = $(try { [bool]$p.Globbing } catch { $false })
      }
    }

    $examples = @()
    foreach ($ex in @($help.Examples.Example)) {
      $remarks = ''
      foreach ($r in @($ex.Remarks)) {
        $t = (GetText $r).Trim()
        if ($t) { if ($remarks) { $remarks += ""`n`n"" }; $remarks += $t }
      }

      $examples += [ordered]@{
        title = $(try { [string]$ex.Title } catch { '' })
        code = $(try { [string]$ex.Code } catch { '' })
        remarks = $remarks
      }
    }

    $descMain = ''
    foreach ($d in @($help.Description)) {
      $t = (GetText $d).Trim()
      if ($t) { if ($descMain) { $descMain += ""`n`n"" }; $descMain += $t }
    }

    $inputs = @()
    try {
      foreach ($it in @($help.InputTypes.InputType)) {
        $typeName = ''
        try { $typeName = [string]$it.Type.Name } catch { $typeName = '' }
        if (-not $typeName) { try { $typeName = [string]$it.Type } catch { $typeName = '' } }

        $typeDesc = ''
        try {
          foreach ($d in @($it.Description)) {
            $t = (GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += ""`n`n"" }; $typeDesc += $t }
          }
        } catch { }

        $inputs += [ordered]@{ name = $typeName; description = $typeDesc }
      }
    } catch { }

    $outputs = @()
    try {
      foreach ($rv in @($help.ReturnValues.ReturnValue)) {
        $typeName = ''
        try { $typeName = [string]$rv.Type.Name } catch { $typeName = '' }
        if (-not $typeName) { try { $typeName = [string]$rv.Type } catch { $typeName = '' } }

        $typeDesc = ''
        try {
          foreach ($d in @($rv.Description)) {
            $t = (GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += ""`n`n"" }; $typeDesc += $t }
          }
        } catch { }

        $outputs += [ordered]@{ name = $typeName; description = $typeDesc }
      }
    } catch { }

    $links = @()
    try {
      foreach ($l in @($help.RelatedLinks.NavigationLink)) {
        $text = ''
        $uri = ''
        try { $text = (GetText $l.LinkText).Trim() } catch { $text = '' }
        try { $uri = (GetText $l.Uri).Trim() } catch { $uri = '' }
        if ($text -or $uri) {
          $links += [ordered]@{ text = $text; uri = $uri }
        }
      }
    } catch { }

    $result.commands += [ordered]@{
      name = [string]$c.Name
      commandType = [string]$c.CommandType
      defaultParameterSet = if ($defaultSet) { [string]$defaultSet } else { $null }
      synopsis = if ($help -and $help.Synopsis) { [string]$help.Synopsis } else { '' }
      description = $descMain
      syntax = @($syntax)
      parameters = @($parameters)
      examples = @($examples)
      inputs = @($inputs)
      outputs = @($outputs)
      relatedLinks = @($links)
    }
  }

  $outDir = Split-Path -Path $OutputJsonPath -Parent
  if ($outDir) { [System.IO.Directory]::CreateDirectory($outDir) | Out-Null }
  $json = $result | ConvertTo-Json -Depth 8
  [System.IO.File]::WriteAllText($OutputJsonPath, $json, [System.Text.UTF8Encoding]::new($false))

  Write-Output 'PFDOCS::OK'
  exit 0
} catch {
  EmitError $_.Exception.Message
  exit 1
}
";
    }
}
