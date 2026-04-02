using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static FormatterResult[] FormatPowerShellTree(
        string rootPath,
        string moduleName,
        string manifestPath,
        bool includeMergeFormatting,
        ConfigurationFormattingSegment formatting,
        FormattingPipeline pipeline)
    {
        if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
        if (formatting is null) return Array.Empty<FormatterResult>();

        var cfg = formatting.Options ?? new FormattingOptions();
        var standardPsd1 = cfg.Standard.FormatCodePSD1 ?? (string.IsNullOrWhiteSpace(cfg.Standard.Style?.PSD1)
            ? null
            : new FormatCodeOptions { Enabled = true });
        var mergePsd1 = cfg.Merge.FormatCodePSD1 ?? (string.IsNullOrWhiteSpace(cfg.Merge.Style?.PSD1)
            ? null
            : new FormatCodeOptions { Enabled = true });

        var excludeDirs = MergeExcludeDirectories(
            primary: null,
            extra: new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore", "Lib", "Modules" });

        var enumeration = new ProjectEnumeration(
            rootPath: rootPath,
            kind: ProjectKind.PowerShell,
            customExtensions: null,
            excludeDirectories: excludeDirs);

        var all = ProjectFileEnumerator.Enumerate(enumeration)
            .Where(IsPowerShellSourceFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootPsm1 = Path.Combine(rootPath, $"{moduleName}.psm1");

        string[] ps1Files = all.Where(p => HasExtension(p, ".ps1")).ToArray();
        string[] psm1Files = all.Where(p => HasExtension(p, ".psm1")).ToArray();
        string[] psd1Files = all.Where(p => HasExtension(p, ".psd1")).ToArray();

        // Avoid formatting the same output file twice when merge settings are enabled.
        if (includeMergeFormatting)
        {
            if (cfg.Merge.FormatCodePSM1?.Enabled == true)
                psm1Files = psm1Files.Where(p => !string.Equals(p, rootPsm1, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (mergePsd1?.Enabled == true)
                psd1Files = psd1Files.Where(p => !string.Equals(p, manifestPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var results = new List<FormatterResult>(all.Length + 4);

        // Legacy PSPublishModule defaults did not format standalone PS1 files unless explicitly configured.
        var standardPs1 = cfg.Standard.FormatCodePS1;
        if (standardPs1?.Enabled == true && ps1Files.Length > 0)
            results.AddRange(pipeline.Run(ps1Files, BuildFormatOptions(standardPs1)));

        if (cfg.Standard.FormatCodePSM1?.Enabled == true && psm1Files.Length > 0)
            results.AddRange(pipeline.Run(psm1Files, BuildFormatOptions(cfg.Standard.FormatCodePSM1)));

        if (standardPsd1?.Enabled == true && psd1Files.Length > 0)
            results.AddRange(pipeline.Run(psd1Files, BuildFormatOptions(standardPsd1)));

        if (includeMergeFormatting)
        {
            if (cfg.Merge.FormatCodePSM1?.Enabled == true && File.Exists(rootPsm1))
                results.AddRange(pipeline.Run(new[] { rootPsm1 }, BuildFormatOptions(cfg.Merge.FormatCodePSM1)));

            if (mergePsd1?.Enabled == true && File.Exists(manifestPath))
                results.AddRange(pipeline.Run(new[] { manifestPath }, BuildFormatOptions(mergePsd1)));
        }

        return results.ToArray();

        static bool IsPowerShellSourceFile(string path)
            => HasExtension(path, ".ps1", ".psm1", ".psd1");

        static bool HasExtension(string path, params string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext)) return false;

            foreach (var e in extensions)
            {
                if (ext.Equals(e, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }

    private ModuleSigningResult SignBuiltModuleOutput(
        string moduleName,
        string rootPath,
        SigningOptionsConfiguration? signing)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        if (signing is null)
        {
            throw new InvalidOperationException(
                "Signing is enabled but no signing options were provided. " +
                "Configure a certificate (CertificateThumbprint / CertificatePFXPath / CertificatePFXBase64) or disable signing.");
        }

        if (signing is null)
        {
            throw new InvalidOperationException(
                "Signing is enabled but no signing options were provided. " +
                "Configure a certificate (CertificateThumbprint / CertificatePFXPath / CertificatePFXBase64) or disable signing.");
        }

        var hasCertificate =
            !string.IsNullOrWhiteSpace(signing.CertificateThumbprint) ||
            !string.IsNullOrWhiteSpace(signing.CertificatePFXPath) ||
            !string.IsNullOrWhiteSpace(signing.CertificatePFXBase64);
        if (!hasCertificate)
        {
            throw new InvalidOperationException(
                "Signing is enabled but no signing certificate was configured. " +
                "Configure CertificateThumbprint, CertificatePFXPath, or CertificatePFXBase64 before enabling delivery/module signing.");
        }

        var include = BuildSigningIncludePatterns(signing);
        var exclude = BuildSigningExcludeSubstrings(signing);
        return _hostedOperations.SignModuleOutput(moduleName, rootPath, include, exclude, signing);
    }

    internal static string[] BuildSigningIncludePatterns(SigningOptionsConfiguration signing)
    {
        if (signing.Include is { Length: > 0 })
            return signing.Include.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray();

        var include = new List<string> { "*.ps1", "*.psm1", "*.psd1" };
        // New default: include binaries unless explicitly disabled.
        if (signing.IncludeBinaries != false) include.AddRange(new[] { "*.dll", "*.cat" });
        if (signing.IncludeExe == true) include.Add("*.exe");
        return include.ToArray();
    }

    internal static SigningOptionsConfiguration? ApplyDeliverySigningPreference(
        SigningOptionsConfiguration? signing,
        DeliveryOptionsConfiguration? delivery)
    {
        if (delivery?.Sign != true)
            return signing;

        var effective = CloneSigningOptions(signing) ?? new SigningOptionsConfiguration();
        effective.IncludeInternals = true;

        if (effective.ExcludePaths is { Length: > 0 })
        {
            var internalsPath = ResolveDeliveryInternalsPath(delivery);
            effective.ExcludePaths = effective.ExcludePaths
                .Where(path => !ShouldRemoveInternalsExcludePath(path, internalsPath))
                .ToArray();
        }

        return effective;
    }

    internal static string[] BuildSigningExcludeSubstrings(
        SigningOptionsConfiguration signing,
        DeliveryOptionsConfiguration? delivery = null)
    {
        var list = new List<string>();
        var internalsPath = ResolveDeliveryInternalsPath(delivery);

        // Legacy behavior: Internals are excluded unless explicitly included.
        if (signing.IncludeInternals != true)
            list.Add(internalsPath);

        // Safety: never sign third-party downloaded dependencies by default.
        list.Add("Modules");

        if (signing.ExcludePaths is { Length: > 0 })
            list.AddRange(signing.ExcludePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static SigningOptionsConfiguration? CloneSigningOptions(SigningOptionsConfiguration? signing)
    {
        if (signing is null)
            return null;

        return new SigningOptionsConfiguration
        {
            IncludeInternals = signing.IncludeInternals,
            IncludeBinaries = signing.IncludeBinaries,
            IncludeExe = signing.IncludeExe,
            Include = signing.Include?.ToArray(),
            ExcludePaths = signing.ExcludePaths?.ToArray(),
            OverwriteSigned = signing.OverwriteSigned,
            CertificateThumbprint = signing.CertificateThumbprint,
            CertificatePFXPath = signing.CertificatePFXPath,
            CertificatePFXBase64 = signing.CertificatePFXBase64,
            CertificatePFXPassword = signing.CertificatePFXPassword
        };
    }

    private static string ResolveDeliveryInternalsPath(DeliveryOptionsConfiguration? delivery)
    {
        var value = delivery?.InternalsPath;
        if (string.IsNullOrWhiteSpace(value))
            return "Internals";

        return value!.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool ShouldRemoveInternalsExcludePath(string? value, string internalsPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value!;
        normalized = normalized.Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        normalized = normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);

        var trimmedInternals = internalsPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);

        return normalized.Equals(trimmedInternals, StringComparison.OrdinalIgnoreCase);
    }

    private static FormatOptions BuildFormatOptions(FormatCodeOptions formatCode)
        => new()
        {
            RemoveCommentsInParamBlock = formatCode.RemoveCommentsInParamBlock,
            RemoveCommentsBeforeParamBlock = formatCode.RemoveCommentsBeforeParamBlock,
            RemoveAllEmptyLines = formatCode.RemoveAllEmptyLines,
            RemoveEmptyLines = formatCode.RemoveEmptyLines,
            PssaSettingsJson = PssaFormattingDefaults.SerializeSettings(formatCode.FormatterSettings),
            TimeoutSeconds = 120,
            LineEnding = LineEnding.CRLF,
            Utf8Bom = true
        };

    private static ValidationSeverity ResolveFileConsistencySeverity(FileConsistencySettings settings)
    {
        if (settings is null) return ValidationSeverity.Warning;
        if (settings.Severity.HasValue) return settings.Severity.Value;
        return settings.FailOnInconsistency ? ValidationSeverity.Error : ValidationSeverity.Warning;
    }

    private static ValidationSeverity ResolveCompatibilitySeverity(CompatibilitySettings settings)
    {
        if (settings is null) return ValidationSeverity.Warning;
        if (settings.Severity.HasValue) return settings.Severity.Value;
        return settings.FailOnIncompatibility ? ValidationSeverity.Error : ValidationSeverity.Warning;
    }

    private static CheckStatus EvaluateFileConsistency(
        ProjectConsistencyReport report,
        FileConsistencySettings settings,
        ValidationSeverity severity)
    {
        var total = report.Summary.TotalFiles;
        if (total <= 0) return CheckStatus.Pass;

        var filesWithIssues = CountFileConsistencyIssues(report, settings);

        if (filesWithIssues == 0) return CheckStatus.Pass;

        var max = Clamp(settings.MaxInconsistencyPercentage, 0, 100);
        var percent = (filesWithIssues / (double)total) * 100.0;
        var status = percent <= max ? CheckStatus.Warning : CheckStatus.Fail;

        if (severity == ValidationSeverity.Off) return CheckStatus.Pass;
        if (severity == ValidationSeverity.Warning && status == CheckStatus.Fail)
            return CheckStatus.Warning;
        return status;
    }

    private static string BuildFileConsistencyMessage(ProjectConsistencyReport report, FileConsistencySettings settings)
    {
        var total = report.Summary.TotalFiles;
        var max = Clamp(settings.MaxInconsistencyPercentage, 0, 100);
        var issues = CountFileConsistencyIssues(report, settings);
        var percent = total <= 0 ? 0.0 : Math.Round((issues / (double)total) * 100.0, 1);
        return $"{issues}/{total} files have issues ({percent:0.0}%, max allowed {max}%).";
    }

    private static int CountFileConsistencyIssues(ProjectConsistencyReport report, FileConsistencySettings settings)
    {
        int filesWithIssues = 0;
        foreach (var f in report.ProblematicFiles)
        {
            if (f.NeedsEncodingConversion || f.NeedsLineEndingConversion)
            {
                filesWithIssues++;
                continue;
            }

            if (settings.CheckMissingFinalNewline && f.MissingFinalNewline)
            {
                filesWithIssues++;
                continue;
            }

            if (settings.CheckMixedLineEndings && f.HasMixedLineEndings)
            {
                filesWithIssues++;
            }
        }

        return filesWithIssues;
    }

    private static PowerShellCompatibilityReport ApplyCompatibilitySettings(
        PowerShellCompatibilityReport report,
        CompatibilitySettings settings,
        ValidationSeverity severity)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        if (settings is null) return report;

        var s = report.Summary;
        if (!settings.RequireCrossCompatibility && !settings.RequirePS51Compatibility && !settings.RequirePS7Compatibility)
            return report;

        if (s.TotalFiles == 0)
            return report;

        var failures = new List<string>();

        if (settings.RequirePS51Compatibility && s.PowerShell51Compatible != s.TotalFiles)
            failures.Add($"PS 5.1 compatible {s.PowerShell51Compatible}/{s.TotalFiles}");

        if (settings.RequirePS7Compatibility && s.PowerShell7Compatible != s.TotalFiles)
            failures.Add($"PS 7 compatible {s.PowerShell7Compatible}/{s.TotalFiles}");

        if (settings.RequireCrossCompatibility)
        {
            var min = Clamp(settings.MinimumCompatibilityPercentage, 0, 100);
            if (s.CrossCompatibilityPercentage < min)
                failures.Add($"Cross-compatible {s.CrossCompatibilityPercentage:0.0}% (< {min}%)");
        }

        var status = failures.Count > 0
            ? CheckStatus.Fail
            : s.FilesWithIssues > 0 ? CheckStatus.Warning : CheckStatus.Pass;

        if (severity == ValidationSeverity.Off)
        {
            status = CheckStatus.Pass;
        }
        else if (severity == ValidationSeverity.Warning && status == CheckStatus.Fail)
        {
            status = CheckStatus.Warning;
        }

        var message = status switch
        {
            CheckStatus.Pass => $"All {s.TotalFiles} files meet compatibility requirements",
            CheckStatus.Warning => s.FilesWithIssues == 1
                ? "1 file has compatibility issues, but requirements are met"
                : $"{s.FilesWithIssues} files have compatibility issues, but requirements are met",
            _ => $"Compatibility requirements not met: {string.Join(", ", failures)}"
        };

        var adjusted = new PowerShellCompatibilitySummary(
            status: status,
            analysisDate: s.AnalysisDate,
            totalFiles: s.TotalFiles,
            powerShell51Compatible: s.PowerShell51Compatible,
            powerShell7Compatible: s.PowerShell7Compatible,
            crossCompatible: s.CrossCompatible,
            filesWithIssues: s.FilesWithIssues,
            crossCompatibilityPercentage: s.CrossCompatibilityPercentage,
            message: message,
            recommendations: s.Recommendations);

        return new PowerShellCompatibilityReport(adjusted, report.Files, report.ExportPath);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        return value > max ? max : value;
    }

    private static void ApplyDeliveryMetadata(string manifestPath, DeliveryOptionsConfiguration delivery)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)) return;
        if (delivery is null || !delivery.Enable) return;

        try
        {
            var internals = string.IsNullOrWhiteSpace(delivery.InternalsPath)
                ? "Internals"
                : delivery.InternalsPath.Trim();

            void ApplyTo(string parentKey)
            {
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "Schema", delivery.Schema ?? "1.4");
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "Enable", delivery.Enable);
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "InternalsPath", internals);
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "IncludeRootReadme", delivery.IncludeRootReadme);
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "IncludeRootChangelog", delivery.IncludeRootChangelog);
                ManifestEditor.TrySetPsDataSubBool(manifestPath, parentKey, "IncludeRootLicense", delivery.IncludeRootLicense);
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "ReadmeDestination", delivery.ReadmeDestination.ToString());
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "ChangelogDestination", delivery.ChangelogDestination.ToString());
                ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "LicenseDestination", delivery.LicenseDestination.ToString());

                if (!string.IsNullOrWhiteSpace(delivery.IntroFile))
                    ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "IntroFile", delivery.IntroFile!.Trim());
                if (!string.IsNullOrWhiteSpace(delivery.UpgradeFile))
                    ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "UpgradeFile", delivery.UpgradeFile!.Trim());

                if (delivery.DocumentationOrder is { Length: > 0 })
                {
                    var ordered = delivery.DocumentationOrder
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (ordered.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "DocumentationOrder", ordered);
                }

                if (delivery.RepositoryPaths is { Length: > 0 })
                {
                    var repoPaths = delivery.RepositoryPaths
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (repoPaths.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "RepositoryPaths", repoPaths);
                }

                if (delivery.PreservePaths is { Length: > 0 })
                {
                    var preserve = delivery.PreservePaths
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (preserve.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "PreservePaths", preserve);
                }

                if (delivery.OverwritePaths is { Length: > 0 })
                {
                    var overwrite = delivery.OverwritePaths
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (overwrite.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "OverwritePaths", overwrite);
                }

                if (!string.IsNullOrWhiteSpace(delivery.RepositoryBranch))
                    ManifestEditor.TrySetPsDataSubString(manifestPath, parentKey, "RepositoryBranch", delivery.RepositoryBranch!.Trim());

                if (delivery.IntroText is { Length: > 0 })
                {
                    var intro = delivery.IntroText
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (intro.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "IntroText", intro);
                }

                if (delivery.UpgradeText is { Length: > 0 })
                {
                    var upgrade = delivery.UpgradeText
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim())
                        .ToArray();
                    if (upgrade.Length > 0)
                        ManifestEditor.TrySetPsDataSubStringArray(manifestPath, parentKey, "UpgradeText", upgrade);
                }

                if (delivery.ImportantLinks is { Length: > 0 })
                {
                    var items = delivery.ImportantLinks
                        .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Title) && !string.IsNullOrWhiteSpace(l.Url))
                        .Select(l => new Dictionary<string, string>
                        {
                            ["Title"] = l.Title.Trim(),
                            ["Url"] = l.Url.Trim()
                        })
                        .ToArray();

                    if (items.Length > 0)
                        ManifestEditor.TrySetPsDataSubHashtableArray(manifestPath, parentKey, "ImportantLinks", items);
                }
            }

            ApplyTo("Delivery");

            if (delivery.RepositoryPaths is { Length: > 0 } || !string.IsNullOrWhiteSpace(delivery.RepositoryBranch))
            {
                BuildServices.SetRepository(
                    manifestPath,
                    branch: string.IsNullOrWhiteSpace(delivery.RepositoryBranch) ? null : delivery.RepositoryBranch!.Trim(),
                    paths: delivery.RepositoryPaths);
            }
        }
        catch
        {
            // Best-effort: do not throw in the build pipeline for delivery metadata.
        }
    }
}
