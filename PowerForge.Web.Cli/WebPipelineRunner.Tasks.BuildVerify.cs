using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteBuild(
        JsonElement step,
        string baseDir,
        ref string lastBuildOutPath,
        ref string[] lastBuildUpdatedFiles,
        WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
        var cleanOutput = GetBool(step, "clean") ?? false;
        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(outPath))
            throw new InvalidOperationException("build requires config and out.");

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        if (cleanOutput)
            WebCliFileSystem.CleanOutputDirectory(outPath);

        var build = WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
        lastBuildOutPath = Path.GetFullPath(build.OutputPath);
        lastBuildUpdatedFiles = build.UpdatedFiles ?? Array.Empty<string>();

        stepResult.Success = true;
        stepResult.Message = $"Built {build.OutputPath}";
    }

    private static void ExecuteVerify(JsonElement step, string baseDir, bool fast, string effectiveMode, WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("verify requires config.");

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
        var isDev = string.Equals(effectiveMode, "dev", StringComparison.OrdinalIgnoreCase) || fast;
        var ciStrictDefaults = UseCiStrictDefaults(effectiveMode, fast);
        var warningPreviewCount = GetInt(step, "warningPreviewCount") ?? GetInt(step, "warning-preview") ?? (isDev ? 2 : 5);
        var errorPreviewCount = GetInt(step, "errorPreviewCount") ?? GetInt(step, "error-preview") ?? (isDev ? 2 : 5);

        var suppressWarnings = GetArrayOfStrings(step, "suppressWarnings") ?? spec.Verify?.SuppressWarnings;
        var failOnWarnings = GetBool(step, "failOnWarnings") ?? spec.Verify?.FailOnWarnings ?? false;
        var failOnNavLint = GetBool(step, "failOnNavLint") ?? GetBool(step, "failOnNavLintWarnings") ?? spec.Verify?.FailOnNavLint ?? ciStrictDefaults;
        var failOnThemeContract = GetBool(step, "failOnThemeContract") ?? spec.Verify?.FailOnThemeContract ?? ciStrictDefaults;
        var baselineGenerate = GetBool(step, "baselineGenerate") ?? false;
        var baselineUpdate = GetBool(step, "baselineUpdate") ?? false;
        var baselinePath = GetString(step, "baseline") ?? GetString(step, "baselinePath");
        var failOnNewWarnings = GetBool(step, "failOnNewWarnings") ?? GetBool(step, "failOnNew") ?? false;
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        var verify = WebSiteVerifier.Verify(spec, plan);
        var filteredWarnings = WebVerifyPolicy.FilterWarnings(verify.Warnings, suppressWarnings);

        if ((baselineGenerate || baselineUpdate || failOnNewWarnings) && string.IsNullOrWhiteSpace(baselinePath))
            baselinePath = ".powerforge/verify-baseline.json";

        var baselineLoaded = false;
        var baselineKeys = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(baselinePath) || baselineGenerate || baselineUpdate || failOnNewWarnings)
            baselineLoaded = WebVerifyBaselineStore.TryLoadWarningKeys(plan.RootPath, baselinePath, out _, out baselineKeys);

        var baselineSet = baselineLoaded ? new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase) : null;
        var newWarnings = baselineSet is null
            ? Array.Empty<string>()
            : filteredWarnings.Where(w =>
                !string.IsNullOrWhiteSpace(w) &&
                !baselineSet.Contains(WebVerifyBaselineStore.NormalizeWarningKey(w))).ToArray();

        var (verifySuccess, verifyPolicyFailures) = WebVerifyPolicy.EvaluateOutcome(
            verify,
            failOnWarnings,
            failOnNavLint,
            failOnThemeContract,
            suppressWarnings);

        if (failOnNewWarnings)
        {
            if (!baselineLoaded)
            {
                verifySuccess = false;
                verifyPolicyFailures = verifyPolicyFailures
                    .Concat(new[] { "fail-on-new-warnings enabled but verify baseline could not be loaded (missing/empty/bad path). Generate one with baselineGenerate." })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            else if (newWarnings.Length > 0)
            {
                verifySuccess = false;
                verifyPolicyFailures = verifyPolicyFailures
                    .Concat(new[] { $"fail-on-new-warnings enabled and verify produced {newWarnings.Length} new warning(s)." })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        if (!verifySuccess)
        {
            var baselineLabel = string.IsNullOrWhiteSpace(baselinePath) ? null : baselinePath;
            var message = BuildVerifyFailureSummary(
                verify,
                filteredWarnings,
                verifyPolicyFailures,
                baselineLabel,
                baselineKeys.Length,
                newWarnings,
                warningPreviewCount,
                errorPreviewCount);
            throw new InvalidOperationException(message);
        }

        if (baselineGenerate || baselineUpdate)
            WebVerifyBaselineStore.Write(plan.RootPath, baselinePath, filteredWarnings, baselineUpdate, logger: null);

        var warnCount = filteredWarnings.Length;
        stepResult.Success = true;
        var newWarnCount = newWarnings.Length;
        stepResult.Message = warnCount > 0 && baselineSet is not null
            ? $"Verify {warnCount} warnings ({newWarnCount} new)"
            : warnCount > 0
                ? $"Verify {warnCount} warnings"
            : "Verify ok";
    }

    private static void ExecuteMarkdownFix(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        var rootPath = ResolvePath(baseDir, GetString(step, "root") ?? GetString(step, "path") ?? GetString(step, "siteRoot"));
        var include = GetString(step, "include");
        var exclude = GetString(step, "exclude");
        var applyFixes = GetBool(step, "apply") ?? false;
        var failOnChanges = GetBool(step, "failOnChanges") ?? GetBool(step, "fail-on-changes") ?? false;

        if (string.IsNullOrWhiteSpace(rootPath) && !string.IsNullOrWhiteSpace(config))
        {
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            var contentRoot = string.IsNullOrWhiteSpace(spec.ContentRoot) ? "content" : spec.ContentRoot;
            rootPath = Path.IsPathRooted(contentRoot)
                ? contentRoot
                : Path.Combine(plan.RootPath, contentRoot);
        }

        if (string.IsNullOrWhiteSpace(rootPath))
            throw new InvalidOperationException("markdown-fix requires root/path/siteRoot or config.");

        var fix = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
        {
            RootPath = rootPath,
            Include = CliPatternHelper.SplitPatterns(include),
            Exclude = CliPatternHelper.SplitPatterns(exclude),
            ApplyChanges = applyFixes
        });
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteMarkdownFixReport(reportPath, fix);

        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"));
        if (!string.IsNullOrWhiteSpace(summaryPath))
            WriteMarkdownFixSummary(summaryPath, fix);

        stepResult.Success = fix.Success;
        stepResult.Message = BuildMarkdownFixStepMessage(fix, applyFixes);

        if (failOnChanges && !applyFixes && fix.ChangedFileCount > 0)
        {
            var message = $"Markdown fix detected {fix.ChangedFileCount} file(s) requiring updates (failOnChanges=true). Re-run with apply=true or fix markdown hygiene.";
            stepResult.Success = false;
            stepResult.Message = message;
            throw new InvalidOperationException(message);
        }
    }

    private static string BuildMarkdownFixStepMessage(WebMarkdownFixResult fix, bool applyFixes)
    {
        var mode = applyFixes ? "updated" : "dry-run";
        return $"Markdown fix {mode} {fix.ChangedFileCount}/{fix.FileCount} files ({fix.ReplacementCount} replacements; media={fix.MediaTagReplacementCount}, html={fix.SimpleHtmlReplacementCount})";
    }

    private static void WriteMarkdownFixReport(string reportPath, WebMarkdownFixResult result)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void WriteMarkdownFixSummary(string summaryPath, WebMarkdownFixResult result)
    {
        var directory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var summary = WebMarkdownHygieneFixer.BuildSummary(result);
        File.WriteAllText(summaryPath, summary, Encoding.UTF8);
    }
}
