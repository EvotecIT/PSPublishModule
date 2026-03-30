using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static Dictionary<string, string> BuildPublishMsBuildProperties(
        IReadOnlyDictionary<string, string>? globalProperties,
        DotNetPublishPublishOptions publish,
        DotNetPublishStyle style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            result[entry.Key] = entry.Value ?? string.Empty;
        }

        foreach (var entry in publish?.MsBuildProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            result[entry.Key] = entry.Value ?? string.Empty;
        }

        if (publish?.StyleOverrides is not null
            && publish.StyleOverrides.TryGetValue(style.ToString(), out var styleOverride)
            && styleOverride?.MsBuildProperties is not null)
        {
            foreach (var entry in styleOverride.MsBuildProperties)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                result[entry.Key] = entry.Value ?? string.Empty;
            }
        }

        return result;
    }

    private static void AppendPublishStyleArgs(List<string> args, DotNetPublishPublishOptions publish, DotNetPublishStyle style)
    {
        switch (style)
        {
            case DotNetPublishStyle.Portable:
            case DotNetPublishStyle.PortableCompat:
            case DotNetPublishStyle.PortableSize:
            {
                args.Add("--self-contained");
                args.Add("true");
                args.Add("/p:PublishSingleFile=true");
                args.Add("/p:IncludeNativeLibrariesForSelfExtract=true");

                // Match TestimoX pattern: use project-controlled trimming via PortableTrim.
                var trim = style == DotNetPublishStyle.PortableSize;
                var trimMode = style == DotNetPublishStyle.PortableSize ? "full" : "partial";
                args.Add($"/p:PortableTrim={trim.ToString().ToLowerInvariant()}");
                args.Add($"/p:PortableTrimMode={trimMode}");

                if (publish.ReadyToRun.HasValue)
                    args.Add($"/p:PublishReadyToRun={publish.ReadyToRun.Value.ToString().ToLowerInvariant()}");

                break;
            }
            case DotNetPublishStyle.AotSpeed:
            case DotNetPublishStyle.AotSize:
            {
                args.Add("--self-contained");
                args.Add("true");
                args.Add("/p:NativeAotPublish=true");
                args.Add("/p:StripSymbols=true");
                args.Add($"/p:IlcOptimizationPreference={(style == DotNetPublishStyle.AotSize ? "Size" : "Speed")}");
                args.Add("/p:InvariantGlobalization=false");
                break;
            }
        }
    }

    private DotNetPublishCleanupResult ApplyCleanup(string publishDir, DotNetPublishPublishOptions publish)
    {
        int pdbRemoved = 0;
        int docsRemoved = 0;
        bool refPruned = false;

        try
        {
            var recurse = publish.Slim ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (!publish.KeepSymbols)
            {
                foreach (var file in EnumerateFilesSafe(publishDir, "*.pdb", recurse))
                    TryDeleteFile(file, ref pdbRemoved);
            }

            if (!publish.KeepDocs)
            {
                foreach (var file in EnumerateFilesSafe(publishDir, "*.xml", recurse))
                    TryDeleteFile(file, ref docsRemoved);
                foreach (var file in EnumerateFilesSafe(publishDir, "*.pdf", recurse))
                    TryDeleteFile(file, ref docsRemoved);
            }

            if (publish.PruneReferences)
            {
                var refDir = Path.Combine(publishDir, "ref");
                if (Directory.Exists(refDir))
                {
                    try
                    {
                        Directory.Delete(refDir, recursive: true);
                        refPruned = true;
                    }
                    catch { /* best effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Cleanup failed. Error: {ex.Message}");
        }

        return new DotNetPublishCleanupResult { PdbRemoved = pdbRemoved, DocsRemoved = docsRemoved, RefPruned = refPruned };
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
    {
        try { return Directory.EnumerateFiles(root, pattern, option); }
        catch { return Array.Empty<string>(); }
    }

    private static void TryDeleteFile(string path, ref int counter)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            counter++;
        }
        catch { /* best effort */ }
    }

    private void TryRenameMainExecutable(string publishDir, string rid, string renameTo)
    {
        var isWindowsRid = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        var desired = renameTo;
        if (isWindowsRid && !desired.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            desired += ".exe";
        if (!isWindowsRid && desired.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            desired = Path.GetFileNameWithoutExtension(desired);

        var candidate = FindMainExecutable(publishDir, rid);
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            return;

        var dest = Path.Combine(Path.GetDirectoryName(candidate)!, desired);
        if (string.Equals(candidate, dest, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(candidate, dest);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to rename executable ({rid}). Error: {ex.Message}");
        }
    }

    private static string? FindMainExecutable(string root, string rid)
    {
        var isWindowsRid = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (isWindowsRid)
            {
                var exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.Length)
                    .ToArray();
                return exes.FirstOrDefault()?.FullName;
            }

            var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(f => string.IsNullOrWhiteSpace(f.Extension))
                .OrderByDescending(f => f.Length)
                .ToArray();
            return files.FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static (int Files, long TotalBytes, string? ExePath, long? ExeBytes) SummarizeDirectory(string dir, string rid)
    {
        try
        {
            var all = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .ToArray();

            long total = 0;
            foreach (var f in all) total += f.Length;

            var exe = FindMainExecutable(dir, rid);
            long? exeBytes = null;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                exeBytes = new FileInfo(exe).Length;

            return (all.Length, total, exe, exeBytes);
        }
        catch
        {
            return (0, 0, null, null);
        }
    }

    private string? CreateZip(string outputDir, DotNetPublishPlan plan, DotNetPublishTargetPlan target, string rid, IReadOnlyDictionary<string, string> tokens)
    {
        try
        {
            var nameTemplate = string.IsNullOrWhiteSpace(target.Publish.ZipNameTemplate)
                ? "{target}-{framework}-{rid}-{style}.zip"
                : target.Publish.ZipNameTemplate!;

            var zipName = ApplyTemplate(nameTemplate, tokens);
            if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipName += ".zip";

            var zipPath = string.IsNullOrWhiteSpace(target.Publish.ZipPath)
                ? Path.Combine(Path.GetDirectoryName(outputDir)!, zipName)
                : ResolvePath(plan.ProjectRoot, ApplyTemplate(target.Publish.ZipPath!, tokens));

            if (!plan.AllowOutputOutsideProjectRoot)
                EnsurePathWithinRoot(plan.ProjectRoot, zipPath, $"Target '{target.Name}' zip path");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            ZipFile.CreateFromDirectory(outputDir, zipPath);
            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to create zip for '{target.Name}' ({rid}). Error: {ex.Message}");
            return null;
        }
    }

    private DotNetPublishServicePackageResult TryCreateServicePackage(
        string outputDir,
        string targetName,
        string rid,
        DotNetPublishServicePackageOptions service)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
            throw new ArgumentException("Output directory must not be empty.", nameof(outputDir));
        if (service is null)
            throw new ArgumentNullException(nameof(service));

        var serviceName = string.IsNullOrWhiteSpace(service.ServiceName)
            ? targetName
            : service.ServiceName!.Trim();
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new InvalidOperationException("Service package requires a non-empty service name.");

        var displayName = string.IsNullOrWhiteSpace(service.DisplayName)
            ? serviceName
            : service.DisplayName!.Trim();
        var description = string.IsNullOrWhiteSpace(service.Description)
            ? $"{serviceName} service"
            : service.Description!.Trim();
        var arguments = string.IsNullOrWhiteSpace(service.Arguments)
            ? null
            : service.Arguments!.Trim();

        var executablePath = ResolveServiceExecutablePath(outputDir, rid, service);
        EnsurePathWithinRoot(outputDir, executablePath, "Service executable path");
        var executableRelativePath = GetRelativePath(outputDir, executablePath)
            .Replace('/', '\\');

        var generateInstall = service.GenerateInstallScript || service.GenerateRunOnceScript;
        var installPath = generateInstall ? Path.Combine(outputDir, "Install-Service.ps1") : null;
        var uninstallPath = service.GenerateUninstallScript ? Path.Combine(outputDir, "Uninstall-Service.ps1") : null;
        var runOncePath = service.GenerateRunOnceScript ? Path.Combine(outputDir, "Run-Once.ps1") : null;
        var metadataPath = Path.Combine(outputDir, "ServicePackage.json");

        if (!string.IsNullOrWhiteSpace(installPath))
            File.WriteAllText(
                installPath!,
                BuildInstallServiceScript(serviceName, displayName, description, executableRelativePath, arguments),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!string.IsNullOrWhiteSpace(uninstallPath))
            File.WriteAllText(
                uninstallPath!,
                BuildUninstallServiceScript(serviceName),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!string.IsNullOrWhiteSpace(runOncePath))
            File.WriteAllText(
                runOncePath!,
                BuildRunOnceServiceScript(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var bootstrapFiles = ApplyConfigBootstrapRules(outputDir, service.ConfigBootstrap);

        var package = new DotNetPublishServicePackageResult
        {
            ServiceName = serviceName,
            DisplayName = displayName,
            Description = description,
            ExecutablePath = executableRelativePath,
            Arguments = arguments,
            InstallScriptPath = installPath,
            UninstallScriptPath = uninstallPath,
            RunOnceScriptPath = runOncePath,
            MetadataPath = metadataPath,
            Recovery = service.Recovery is null
                ? null
                : new DotNetPublishServiceRecoveryOptions
                {
                    Enabled = service.Recovery.Enabled,
                    ResetPeriodSeconds = service.Recovery.ResetPeriodSeconds,
                    RestartDelaySeconds = service.Recovery.RestartDelaySeconds,
                    ApplyToNonCrashFailures = service.Recovery.ApplyToNonCrashFailures,
                    OnFailure = service.Recovery.OnFailure
                },
            ConfigBootstrapFiles = bootstrapFiles
        };

        var metadataJson = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metadataPath, metadataJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _logger.Info($"Generated service package metadata for '{targetName}' ({rid}) -> {metadataPath}");
        if (!string.IsNullOrWhiteSpace(installPath))
            _logger.Info($"Generated service install script -> {installPath}");
        if (!string.IsNullOrWhiteSpace(uninstallPath))
            _logger.Info($"Generated service uninstall script -> {uninstallPath}");
        if (!string.IsNullOrWhiteSpace(runOncePath))
            _logger.Info($"Generated service run-once script -> {runOncePath}");
        if (bootstrapFiles.Length > 0)
            _logger.Info($"Applied {bootstrapFiles.Length} config bootstrap file(s) for '{targetName}' ({rid}).");

        return package;
    }

    private string[] ApplyConfigBootstrapRules(string outputDir, DotNetPublishConfigBootstrapRule[]? rules)
    {
        var applied = new List<string>();
        foreach (var rule in rules ?? Array.Empty<DotNetPublishConfigBootstrapRule>())
        {
            if (rule is null) continue;

            var sourceRel = (rule.SourcePath ?? string.Empty).Trim();
            var destinationRel = (rule.DestinationPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceRel) || string.IsNullOrWhiteSpace(destinationRel))
                continue;

            var source = ResolvePath(outputDir, sourceRel);
            var destination = ResolvePath(outputDir, destinationRel);

            EnsurePathWithinRoot(outputDir, source, $"Config bootstrap source '{sourceRel}'");
            EnsurePathWithinRoot(outputDir, destination, $"Config bootstrap destination '{destinationRel}'");

            if (!File.Exists(source))
            {
                HandlePolicy(
                    rule.OnMissingSource,
                    $"Config bootstrap source file not found: {source}.");
                continue;
            }

            if (File.Exists(destination) && !rule.Overwrite)
            {
                _logger.Info($"Config bootstrap skipped existing file: {destination}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: rule.Overwrite);

            var relative = GetRelativePath(outputDir, destination).Replace('/', '\\');
            applied.Add(relative);
            _logger.Info($"Config bootstrap applied: {sourceRel} -> {relative}");
        }

        return applied
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveServiceExecutablePath(string outputDir, string rid, DotNetPublishServicePackageOptions service)
    {
        if (!string.IsNullOrWhiteSpace(service.ExecutablePath))
        {
            var resolved = ResolvePath(outputDir, service.ExecutablePath!);
            if (!File.Exists(resolved))
                throw new FileNotFoundException(
                    $"Service executable path not found: {resolved}. " +
                    "Set Publish.Service.ExecutablePath to a valid file in the output folder.",
                    resolved);
            return resolved;
        }

        var detected = FindMainExecutable(outputDir, rid);
        if (string.IsNullOrWhiteSpace(detected) || !File.Exists(detected))
        {
            throw new FileNotFoundException(
                $"Failed to auto-detect service executable in output: {outputDir}. " +
                "Set Publish.Service.ExecutablePath explicitly.");
        }

        return detected!;
    }

    private static string BuildInstallServiceScript(
        string serviceName,
        string displayName,
        string description,
        string executableRelativePath,
        string? arguments)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ServiceName"] = EscapePowerShellSingleQuoted(serviceName),
            ["DisplayName"] = EscapePowerShellSingleQuoted(displayName),
            ["Description"] = EscapePowerShellSingleQuoted(description),
            ["ExecutableRelativePath"] = EscapePowerShellSingleQuoted(executableRelativePath),
            ["Arguments"] = EscapePowerShellSingleQuoted(arguments ?? string.Empty)
        };

        return RenderServiceTemplate(
            "Install-Service.ps1",
            EmbeddedScripts.Load("Scripts/DotNetPublish/Install-Service.Template.ps1"),
            tokens);
    }

    private static string BuildUninstallServiceScript(string serviceName)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ServiceName"] = EscapePowerShellSingleQuoted(serviceName)
        };

        return RenderServiceTemplate(
            "Uninstall-Service.ps1",
            EmbeddedScripts.Load("Scripts/DotNetPublish/Uninstall-Service.Template.ps1"),
            tokens);
    }

    private static string BuildRunOnceServiceScript()
    {
        return RenderServiceTemplate(
            "Run-Once.ps1",
            EmbeddedScripts.Load("Scripts/DotNetPublish/Run-Once.Template.ps1"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string RenderServiceTemplate(
        string templateName,
        string template,
        IReadOnlyDictionary<string, string> tokens)
    {
        return ScriptTemplateRenderer.Render(templateName, template, tokens);
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

}
