using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PowerForge;

/// <summary>
/// Generates module documentation (markdown help) using the PowerForge built-in generator.
/// </summary>
/// <remarks>
/// <para>
/// The documentation workflow is built around PowerShell-native help extraction:
/// </para>
/// <list type="bullet">
/// <item><description>Imports the staged module and extracts command metadata via <c>Get-Help</c></description></item>
/// <item><description>Enriches missing synopsis/description/examples using C# XML docs (<c>*.xml</c>) from the cmdlet assembly</description></item>
/// <item><description>Writes markdown pages to <see cref="DocumentationConfiguration.Path"/> and optionally creates external help (MAML) under <c>en-US</c></description></item>
/// </list>
/// <para>
/// The engine is used by the module build pipeline and is designed to work for script modules, binary modules, and mixed modules.
/// </para>
/// </remarks>
/// <example>
/// <summary>Generate markdown docs and external help for a staged module</summary>
/// <code>
/// var logger = new ConsoleLogger { IsVerbose = true };
/// var engine = new DocumentationEngine(new PowerShellRunner(), logger);
/// var result = engine.Build(
///     moduleName: "MyModule",
///     stagingPath: @"C:\Temp\PowerForge\build\MyModule",
///     moduleManifestPath: @"C:\Temp\PowerForge\build\MyModule\MyModule.psd1",
///     documentation: new DocumentationConfiguration { Path = "Docs", PathReadme = @"Docs\Readme.md" },
///     buildDocumentation: new BuildDocumentationConfiguration { Enable = true, GenerateExternalHelp = true, ExternalHelpCulture = "en-US" });
/// </code>
/// </example>
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
                readmePath: ResolvePath(stagingPath, documentation.PathReadme, optional: true),
                succeeded: true,
                exitCode: 0,
                markdownFiles: 0,
                externalHelpFilePath: string.Empty,
                errorMessage: null);
        }

        var docsPath = ResolvePath(stagingPath, documentation.Path);
        var readmePath = ResolvePath(stagingPath, documentation.PathReadme, optional: true);
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
                SafeDeleteExistingExternalHelpFile(stagingPath, moduleName, buildDocumentation);
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

            try
            {
                new XmlDocCommentEnricher(_logger).Enrich(extracted);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to enrich help from XML docs. Error: {ex.Message}");
                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            }

            try
            {
                if (buildDocumentation.GenerateFallbackExamples)
                    DocumentationFallbackEnricher.Enrich(extracted, _logger);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to apply documentation fallbacks. Error: {ex.Message}");
                if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            }

            var writer = new MarkdownHelpWriter();
            SafeStart(writeStep);
            try
            {
                writer.WriteCommandHelpFiles(extracted, moduleName, docsPath);
                if (buildDocumentation.IncludeAboutTopics)
                {
                    try { new AboutTopicWriter().Write(stagingPath, docsPath); }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to write about_* topics. Error: {ex.Message}");
                        if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
                    }
                }
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

    internal DocumentationExtractionPayload ExtractHelpPayload(
        string stagingPath,
        string moduleManifestPath,
        TimeSpan? timeout = null)
    {
        return ExtractHelpAsJson(
            stagingPath: stagingPath,
            moduleManifestPath: moduleManifestPath,
            timeout: timeout ?? TimeSpan.FromMinutes(5));
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
                message = AppendRequiredModulesHint(moduleManifestPath, message);
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

    private static string AppendRequiredModulesHint(string moduleManifestPath, string? message)
    {
        var msg = message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(moduleManifestPath) || !File.Exists(moduleManifestPath))
            return msg;

        if (!ManifestEditor.TryGetInvalidRequiredModuleSpecs(moduleManifestPath, out var invalid) ||
            invalid is null || invalid.Length == 0)
            return msg;

        var list = string.Join(", ", invalid);
        var hint = $" RequiredModules contains hashtable entries without ModuleVersion/RequiredVersion/MaximumVersion: {list}. Use string module names or include a version field.";
        return string.IsNullOrWhiteSpace(msg) ? hint.Trim() : msg.TrimEnd() + hint;
    }

    private static string ResolvePath(string baseDir, string path, bool optional = false)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return optional ? string.Empty : Path.GetFullPath(baseDir);
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

    private static void SafeDeleteExistingExternalHelpFile(
        string stagingPath,
        string moduleName,
        BuildDocumentationConfiguration buildDocumentation)
    {
        try
        {
            if (!buildDocumentation.GenerateExternalHelp) return;
            if (string.IsNullOrWhiteSpace(stagingPath)) return;
            if (string.IsNullOrWhiteSpace(moduleName)) return;

            var culture = NormalizeExternalHelpCulture(buildDocumentation.ExternalHelpCulture);
            var externalHelpDir = Path.Combine(stagingPath, culture);
            if (!Directory.Exists(externalHelpDir)) return;

            var fileName = string.IsNullOrWhiteSpace(buildDocumentation.ExternalHelpFileName)
                ? $"{moduleName}-help.xml"
                : Path.GetFileName(buildDocumentation.ExternalHelpFileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)) fileName = $"{moduleName}-help.xml";

            var externalHelpFile = Path.Combine(externalHelpDir, fileName);
            if (File.Exists(externalHelpFile))
            {
                try { File.Delete(externalHelpFile); } catch { /* best effort */ }
            }
        }
        catch
        {
            // best effort
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

    private static string[] SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string BuildHelpExportScript()
    {
        return EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.ps1");
    }
}
