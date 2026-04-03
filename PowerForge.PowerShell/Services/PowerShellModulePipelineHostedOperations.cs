using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PowerForge;

internal sealed class PowerShellModulePipelineHostedOperations : IModulePipelineHostedOperations
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    internal PowerShellModulePipelineHostedOperations(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? new NullLogger();
    }

    internal PowerShellModulePipelineHostedOperations(ILogger logger)
        : this(new PowerShellRunner(), logger)
    {
    }

    public IReadOnlyList<ModuleDependencyInstallResult> EnsureDependenciesInstalled(
        ModuleDependency[] dependencies,
        ModuleSkipConfiguration? skipModules,
        bool force,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        var installer = new ModuleDependencyInstaller(_runner, _logger);
        return installer.EnsureInstalled(
            dependencies: dependencies,
            skipModules: skipModules?.IgnoreModuleName,
            force: force,
            repository: repository,
            credential: credential,
            prerelease: prerelease);
    }

    public DocumentationBuildResult BuildDocumentation(
        string moduleName,
        string stagingPath,
        string moduleManifestPath,
        DocumentationConfiguration documentation,
        BuildDocumentationConfiguration buildDocumentation,
        IModulePipelineProgressReporter progress,
        ModulePipelineStep? extractStep,
        ModulePipelineStep? writeStep,
        ModulePipelineStep? externalHelpStep)
    {
        var engine = new DocumentationEngine(_runner, _logger);
        return engine.BuildWithProgress(
            moduleName: moduleName,
            stagingPath: stagingPath,
            moduleManifestPath: moduleManifestPath,
            documentation: documentation,
            buildDocumentation: buildDocumentation,
            timeout: null,
            progress: progress,
            extractStep: extractStep,
            writeStep: writeStep,
            externalHelpStep: externalHelpStep);
    }

    public ModuleValidationReport ValidateModule(ModuleValidationSpec spec)
        => new ModuleValidationService(_logger, _runner).Run(spec);

    public void EnsureBinaryDependenciesValid(string moduleRoot, string powerShellEdition, string? modulePath, string? validationTarget)
    {
        var service = new BinaryDependencyPreflightService(_logger);
        var result = service.Analyze(moduleRoot, powerShellEdition, modulePath);
        if (!result.HasIssues)
            return;

        throw new InvalidOperationException(
            BinaryDependencyPreflightService.BuildFailureMessage(
                result,
                modulePath,
                validationTarget));
    }

    public ModuleTestSuiteResult RunModuleTestSuite(ModuleTestSuiteSpec spec)
        => new ModuleTestSuiteService(_runner, _logger).Run(spec);

    public ModulePublishResult PublishModule(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        IReadOnlyList<ArtefactBuildResult> artefactResults,
        bool includeScriptFolders)
        => new ModulePublisher(_logger, _runner).Publish(publish, plan, buildResult, artefactResults, includeScriptFolders);

    public void ValidateModuleImports(
        string manifestPath,
        ImportModuleEntry[] modules,
        bool importRequired,
        bool importSelf,
        bool verbose,
        ModuleImportValidationTarget[] targets)
    {
        var modulesB64 = EncodeImportModules(modules);
        var args = new List<string>(5)
        {
            modulesB64,
            importRequired ? "1" : "0",
            importSelf ? "1" : "0",
            manifestPath,
            verbose ? "1" : "0"
        };

        var script = EmbeddedScripts.Load("Scripts/ModulePipeline/Import-Modules.ps1");
        foreach (var target in targets ?? Array.Empty<ModuleImportValidationTarget>())
        {
            var result = RunScript(
                scriptText: script,
                args: args,
                timeout: TimeSpan.FromMinutes(5),
                preferPwsh: target.PreferPwsh);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    ModuleImportFailureFormatter.BuildFailureMessage(
                        result,
                        manifestPath,
                        validationTarget: target.Label));
            }
        }
    }

    public ModuleSigningResult SignModuleOutput(
        string moduleName,
        string rootPath,
        string[] includePatterns,
        string[] excludeSubstrings,
        SigningOptionsConfiguration signing)
    {
        var args = new List<string>(8)
        {
            rootPath,
            EncodeLines(includePatterns),
            EncodeLines(excludeSubstrings),
            signing.CertificateThumbprint ?? string.Empty,
            signing.CertificatePFXPath ?? string.Empty,
            signing.CertificatePFXBase64 ?? string.Empty,
            signing.CertificatePFXPassword ?? string.Empty,
            signing.OverwriteSigned == true ? "1" : "0"
        };

        var script = EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");
        var result = RunScript(script, args, TimeSpan.FromMinutes(10), preferPwsh: true);
        var summary = TryExtractSigningSummary(result.StdOut);

        if (result.ExitCode != 0 || (summary?.Failed ?? 0) > 0)
        {
            var message = TryExtractSigningError(result.StdOut) ?? result.StdErr;
            var extra = summary is null ? string.Empty : $" {FormatSigningSummary(summary)}";
            var full = $"Signing failed (exit {result.ExitCode}). {message}{extra}".Trim();

            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());

            throw new ModuleSigningException(full, summary);
        }

        summary ??= new ModuleSigningResult
        {
            Attempted = ParseSignedCount(result.StdOut),
            SignedNew = ParseSignedCount(result.StdOut)
        };

        if (summary.SignedTotal > 0)
        {
            _logger.Success(
                $"Signed {summary.SignedNew} new file(s), re-signed {summary.Resigned} file(s) for '{moduleName}'. " +
                $"(Already signed: {summary.AlreadySignedOther} third-party, {summary.AlreadySignedByThisCert} by this cert)");
        }
        else
        {
            _logger.Info(
                $"No files required signing for '{moduleName}'. " +
                $"(Already signed: {summary.AlreadySignedOther} third-party, {summary.AlreadySignedByThisCert} by this cert)");
        }

        return summary;
    }

    private static string EncodeImportModules(IEnumerable<ImportModuleEntry> modules)
    {
        var list = modules?.Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.Name)).ToArray()
            ?? Array.Empty<ImportModuleEntry>();
        if (list.Length == 0)
            return string.Empty;

        var json = JsonSerializer.Serialize(list);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout, bool preferPwsh)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "modulepipeline");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"modulepipeline_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines ?? Array.Empty<string>());
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static int ParseSignedCount(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFSIGN::COUNT::", StringComparison.Ordinal)) continue;
            var value = line.Substring("PFSIGN::COUNT::".Length);
            if (int.TryParse(value, out var number)) return number;
        }

        return 0;
    }

    private static string? TryExtractSigningError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFSIGN::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFSIGN::ERROR::".Length);
            var decoded = Decode(b64);
            return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
        }

        return null;
    }

    private static ModuleSigningResult? TryExtractSigningSummary(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFSIGN::SUMMARY::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFSIGN::SUMMARY::".Length);
            var decoded = Decode(b64);
            if (string.IsNullOrWhiteSpace(decoded)) return null;

            try
            {
                return JsonSerializer.Deserialize<ModuleSigningResult>(
                    decoded,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string FormatSigningSummary(ModuleSigningResult summary)
        => $"matched {summary.TotalAfterExclude}, signed {summary.SignedNew} new, re-signed {summary.Resigned}, " +
           $"already signed {summary.AlreadySignedOther} third-party/{summary.AlreadySignedByThisCert} by this cert, failed {summary.Failed}.";
}
