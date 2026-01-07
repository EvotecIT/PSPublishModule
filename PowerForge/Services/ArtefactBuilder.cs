using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Creates packed/unpacked artefacts for a built module using typed configuration segments.
/// </summary>
public sealed class ArtefactBuilder
{
    private static readonly string[] DefaultExcludeFromPackage = { ".*", "Ignore", "Examples", "package.json", "Publish", "Docs" };
    private static readonly string[] DefaultIncludeRoot = { "*.psm1", "*.psd1", "*.Libraries.ps1", "License*" };
    private static readonly string[] DefaultIncludePS1 = { "Private", "Public", "Enums", "Classes" };
    private static readonly string[] DefaultIncludeAll = { "Images", "Resources", "Templates", "Bin", "Lib", "Data", "en-US" };

    private const string PSGalleryName = "PSGallery";
    private const string PSGalleryUriV2 = "https://www.powershellgallery.com/api/v2";

    private readonly ILogger _logger;
    private bool _skipPsResourceGetSave;
    private bool _ensuredPowerShellGetRepository;
    private bool _ensuredPsResourceGetRepository;

    /// <summary>
    /// Creates a new builder that logs progress via <paramref name="logger"/>.
    /// </summary>
    public ArtefactBuilder(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Builds a single artefact described by <paramref name="segment"/> using the built module from <paramref name="stagingPath"/>.
    /// </summary>
    /// <param name="segment">Artefact configuration segment.</param>
    /// <param name="projectRoot">Project root used for resolving relative paths.</param>
    /// <param name="stagingPath">Path to the built module staging folder.</param>
    /// <param name="moduleName">Module name.</param>
    /// <param name="moduleVersion">Resolved module version (without prerelease).</param>
    /// <param name="preRelease">Optional prerelease tag.</param>
    /// <param name="requiredModules">Required modules from configuration (used when AddRequiredModules is enabled).</param>
    /// <param name="information">Optional include/exclude configuration for packaging.</param>
    public ArtefactBuildResult Build(
        ConfigurationArtefactSegment segment,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information = null)
    {
        if (segment is null) throw new ArgumentNullException(nameof(segment));
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("ProjectRoot is required.", nameof(projectRoot));
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(moduleVersion)) throw new ArgumentException("ModuleVersion is required.", nameof(moduleVersion));

        var cfg = segment.Configuration ?? new ArtefactConfiguration();
        if (cfg.Enabled != true)
            throw new InvalidOperationException($"Artefact '{segment.ArtefactType}' is not enabled.");

        var root = ResolveOutputRoot(cfg.Path, projectRoot, moduleName, moduleVersion, preRelease, segment.ArtefactType);

        return segment.ArtefactType switch
        {
            ArtefactType.Unpacked => BuildUnpacked(cfg, root, projectRoot, stagingPath, moduleName, moduleVersion, preRelease, requiredModules, information),
            ArtefactType.Packed => BuildPacked(cfg, root, projectRoot, stagingPath, moduleName, moduleVersion, preRelease, requiredModules, information),
            _ => throw new NotSupportedException($"Artefact type '{segment.ArtefactType}' is not supported yet.")
        };
    }

    internal static void CopyModulePackageForInstall(
        string stagingRoot,
        string destinationModuleRoot,
        InformationConfiguration? information)
    {
        var include = ResolvePackagingInformation(information);
        CopyModulePackage(stagingRoot, destinationModuleRoot, include);
    }

    private ArtefactBuildResult BuildUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information)
    {
        if (cfg.DoNotClear != true)
            ClearDirectorySafe(outputRoot);
        Directory.CreateDirectory(outputRoot);

        var include = ResolvePackagingInformation(information);

        var requiredRoot = ResolveRequiredModulesRootForUnpacked(cfg, outputRoot, projectRoot, moduleName, moduleVersion, preRelease);
        var modulesRoot = ResolveModulesRootForUnpacked(cfg, outputRoot, requiredRoot, projectRoot, moduleName, moduleVersion, preRelease);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();

        var mainModuleDest = Path.Combine(modulesRoot, moduleName);
        _logger.Info($"Creating unpacked artefact at '{outputRoot}'");
        CopyModulePackage(stagingPath, mainModuleDest, include);
        modules.Add(new ArtefactModuleEntry(moduleName, isMainModule: true, version: moduleVersion, path: mainModuleDest));

        if (cfg.RequiredModules.Enabled == true)
        {
            var tool = cfg.RequiredModules.Tool ?? ModuleSaveTool.Auto;
            var source = cfg.RequiredModules.Source ?? RequiredModulesSource.Installed;
            foreach (var rm in (requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>()).Where(m => !string.IsNullOrWhiteSpace(m.ModuleName)))
            {
                var depEntry = SaveRequiredModuleToFolder(
                    rm,
                    requiredRoot,
                    cfg.RequiredModules.Repository,
                    cfg.RequiredModules.Credential,
                    tool,
                    source);
                modules.Add(depEntry);
            }
        }

        CopyExtraMappings(
            cfg,
            projectRoot,
            outputRoot,
            moduleName,
            moduleVersion,
            preRelease,
            copied);

        return new ArtefactBuildResult(ArtefactType.Unpacked, cfg.ID, outputRoot, modules.ToArray(), copied.ToArray());
    }

    private ArtefactBuildResult BuildPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information)
    {
        Directory.CreateDirectory(outputRoot);
        if (cfg.DoNotClear != true)
            ClearDirectoryContentsSafe(outputRoot, excludePatterns: new[] { "*.zip" });

        var include = ResolvePackagingInformation(information);

        var artefactName = ResolveArtefactFileName(cfg, moduleName, moduleVersion, preRelease);
        var zipPath = Path.Combine(outputRoot, artefactName);

        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", $"{moduleName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();

        try
        {
            var mainModuleDest = Path.Combine(tempRoot, moduleName);
            _logger.Info($"Staging packed artefact '{zipPath}'");
            CopyModulePackage(stagingPath, mainModuleDest, include);
            modules.Add(new ArtefactModuleEntry(moduleName, isMainModule: true, version: moduleVersion, path: mainModuleDest));

            if (cfg.RequiredModules.Enabled == true)
            {
                var tool = cfg.RequiredModules.Tool ?? ModuleSaveTool.Auto;
                var source = cfg.RequiredModules.Source ?? RequiredModulesSource.Installed;
                foreach (var rm in (requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>()).Where(m => !string.IsNullOrWhiteSpace(m.ModuleName)))
                {
                    var depEntry = SaveRequiredModuleToFolder(
                        rm,
                        tempRoot,
                        cfg.RequiredModules.Repository,
                        cfg.RequiredModules.Credential,
                        tool,
                        source);
                    modules.Add(depEntry);
                }
            }

            CopyExtraMappings(
                cfg,
                projectRoot,
                tempRoot,
                moduleName,
                moduleVersion,
                preRelease,
                copied,
                enforceRelativeDestination: true);

            CreateZipFromDirectoryContents(tempRoot, zipPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }

        return new ArtefactBuildResult(ArtefactType.Packed, cfg.ID, zipPath, modules.ToArray(), copied.ToArray());
    }

    private ArtefactModuleEntry SaveRequiredModuleToFolder(
        ManifestEditor.RequiredModule requiredModule,
        string destinationRoot,
        string? repository,
        RepositoryCredential? credential,
        ModuleSaveTool tool,
        RequiredModulesSource source)
    {
        if (requiredModule is null) throw new ArgumentNullException(nameof(requiredModule));
        if (string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            throw new ArgumentException("Required module name is empty.", nameof(requiredModule));
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new ArgumentException("DestinationRoot is required.", nameof(destinationRoot));

        var name = requiredModule.ModuleName.Trim();
        var minimumVersionArgument = NormalizeVersionArgument(requiredModule.ModuleVersion);
        var requiredVersionArgument = NormalizeVersionArgument(requiredModule.RequiredVersion);
        var maximumVersionArgument = NormalizeVersionArgument(requiredModule.MaximumVersion);
        var versionArgument = requiredVersionArgument ?? minimumVersionArgument;

        Directory.CreateDirectory(destinationRoot);

        var runner = new PowerShellRunner();
        if (source != RequiredModulesSource.Download)
        {
            try
            {
                var installed = TryGetInstalledModule(
                    runner,
                    name,
                    requiredVersionArgument,
                    minimumVersionArgument,
                    maximumVersionArgument);
                if (installed is not null)
                {
                    var dest = Path.Combine(destinationRoot, name);
                    CopyDirectory(installed.ModuleBasePath, dest);
                    return new ArtefactModuleEntry(name, isMainModule: false, version: installed.Version, path: dest);
                }

                if (source == RequiredModulesSource.Installed)
                {
                    var reqText = requiredVersionArgument ?? minimumVersionArgument ?? string.Empty;
                    var versionText = string.IsNullOrWhiteSpace(reqText) ? string.Empty : $" (requested: {reqText})";
                    throw new InvalidOperationException($"Required module '{name}' was not found locally{versionText}. Install it (Install-Module {name}) or set RequiredModules.Source to 'Auto'/'Download'.");
                }
            }
            catch (Exception ex) when (source == RequiredModulesSource.Auto)
            {
                _logger.Warn($"Failed to resolve required module '{name}' from local module paths; attempting download. {ex.Message}");
            }
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", "saved", $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            IReadOnlyList<PSResourceInfo> saved;

            var effectiveTool = tool;
            if (effectiveTool == ModuleSaveTool.Auto && _skipPsResourceGetSave)
                effectiveTool = ModuleSaveTool.PowerShellGet;

            if (effectiveTool == ModuleSaveTool.PowerShellGet)
            {
                var psg = new PowerShellGetClient(runner, _logger);
                var psgOpts = new PowerShellGetSaveOptions(
                    name: name,
                    destinationPath: tempRoot,
                    minimumVersion: minimumVersionArgument,
                    requiredVersion: requiredVersionArgument,
                    repository: repository,
                    prerelease: false,
                    acceptLicense: true,
                    credential: credential);
                TryEnsurePowerShellGetDefaultRepository(psg, repository);
                saved = SaveRequiredModuleWithPowerShellGet(psg, psgOpts, name);
            }
            else
            {
                var psrg = new PSResourceGetClient(runner, _logger);
                var psrgOpts = new PSResourceSaveOptions(
                    name: name,
                    destinationPath: tempRoot,
                    version: versionArgument,
                    repository: repository,
                    prerelease: false,
                    trustRepository: true,
                    skipDependencyCheck: true,
                    acceptLicense: true,
                    credential: credential);

                try
                {
                    TryEnsurePsResourceGetDefaultRepository(psrg, repository);
                    saved = psrg.Save(psrgOpts, timeout: TimeSpan.FromMinutes(10));
                }
                catch (Exception ex)
                {
                    if (tool == ModuleSaveTool.PSResourceGet)
                    {
                        var raw = ex.GetBaseException().Message ?? ex.Message ?? string.Empty;
                        var reason = SimplifyPsResourceGetFailureMessage(raw);
                        var msg = string.IsNullOrWhiteSpace(reason)
                            ? $"Save-PSResource failed while downloading required module '{name}'."
                            : $"Save-PSResource failed while downloading required module '{name}'. {reason}";
                        throw new InvalidOperationException(msg, ex);
                    }

                    _skipPsResourceGetSave = true;

                    var rawAuto = ex.Message ?? string.Empty;
                    var reasonAuto = SimplifyPsResourceGetFailureMessage(rawAuto);

                    if (ex is PowerShellToolNotAvailableException)
                    {
                        _logger.Warn("PSResourceGet is not available; using Save-Module for required modules.");
                    }
                    else if (IsSecretStoreLockedMessage(rawAuto))
                    {
                        _logger.Warn("PSResourceGet cannot access SecretStore (locked); using Save-Module for required modules.");
                    }
                    else if (!string.IsNullOrWhiteSpace(reasonAuto))
                    {
                        _logger.Warn($"Save-PSResource failed; using Save-Module for required modules. {reasonAuto}");
                    }
                    else
                    {
                        _logger.Warn("Save-PSResource failed; using Save-Module for required modules.");
                    }

                    if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(rawAuto))
                        _logger.Verbose(rawAuto.Trim());

                    var psg = new PowerShellGetClient(runner, _logger);
                    var psgOpts = new PowerShellGetSaveOptions(
                        name: name,
                        destinationPath: tempRoot,
                        minimumVersion: minimumVersionArgument,
                        requiredVersion: requiredVersionArgument,
                        repository: repository,
                        prerelease: false,
                        acceptLicense: true,
                        credential: credential);
                    TryEnsurePowerShellGetDefaultRepository(psg, repository);
                    saved = SaveRequiredModuleWithPowerShellGet(psg, psgOpts, name);
                }
            }
            var resolved = saved.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            var resolvedVersion = resolved?.Version;

            var moduleRoot = Path.Combine(tempRoot, name);
            if (!Directory.Exists(moduleRoot))
                throw new InvalidOperationException($"Save tool did not create expected folder '{moduleRoot}'.");

            string? versionFolder = null;
            if (!string.IsNullOrWhiteSpace(resolvedVersion))
            {
                var candidate = Path.Combine(moduleRoot, resolvedVersion!);
                if (Directory.Exists(candidate)) versionFolder = candidate;
            }

            versionFolder ??= Directory.EnumerateDirectories(moduleRoot).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(versionFolder) || !Directory.Exists(versionFolder))
                throw new InvalidOperationException($"Unable to locate saved version folder under '{moduleRoot}'.");

            var dest = Path.Combine(destinationRoot, name);
            CopyDirectory(versionFolder, dest);
            return new ArtefactModuleEntry(name, isMainModule: false, version: resolvedVersion, path: dest);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class InstalledModule
    {
        public string ModuleBasePath { get; }
        public string? Version { get; }

        public InstalledModule(string moduleBasePath, string? version)
        {
            ModuleBasePath = moduleBasePath;
            Version = version;
        }
    }

    private InstalledModule? TryGetInstalledModule(
        IPowerShellRunner runner,
        string moduleName,
        string? requiredVersion,
        string? minimumVersion,
        string? maximumVersion)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (string.IsNullOrWhiteSpace(moduleName)) return null;

        var script = BuildFindInstalledModuleScript();
        var args = new List<string>(4)
        {
            moduleName.Trim(),
            requiredVersion ?? string.Empty,
            minimumVersion ?? string.Empty,
            maximumVersion ?? string.Empty
        };

        var result = RunScript(runner, script, args, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            var msg = TryExtractModuleLocatorError(result.StdOut) ?? result.StdErr;
            throw new InvalidOperationException($"Get-Module -ListAvailable failed (exit {result.ExitCode}). {msg}".Trim());
        }

        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMODLOC::FOUND::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 4) continue;

            var version = Decode(parts[2]);
            var path = Decode(parts[3]);
            if (string.IsNullOrWhiteSpace(path)) continue;

            var full = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(full)) continue;

            var verText = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            return new InstalledModule(full, verText);
        }

        return null;
    }

    private static PowerShellRunResult RunScript(
        IPowerShellRunner runner,
        string scriptText,
        IReadOnlyList<string> args,
        TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "modulelocator");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"modulelocator_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string? TryExtractModuleLocatorError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFMODLOC::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFMODLOC::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
    }

    private static string BuildFindInstalledModuleScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleLocator/Find-InstalledModule.ps1");
}

    private static bool IsSecretStoreLockedMessage(string message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.IndexOf("Microsoft.PowerShell.SecretStore", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("Unlock-SecretStore", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("A valid password is required", StringComparison.OrdinalIgnoreCase) >= 0);

    private static string SimplifyPsResourceGetFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var trimmed = message.Trim();

        // "Save-PSResource failed (exit X). <reason>" -> "<reason>"
        if (trimmed.StartsWith("Save-PSResource failed", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "). ";
            var idx = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && idx + marker.Length < trimmed.Length)
                return trimmed.Substring(idx + marker.Length).Trim();
        }

        return trimmed;
    }

    private IReadOnlyList<PSResourceInfo> SaveRequiredModuleWithPowerShellGet(
        PowerShellGetClient client,
        PowerShellGetSaveOptions options,
        string moduleName)
    {
        try
        {
            return client.Save(options, timeout: TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            var raw = ex.GetBaseException().Message ?? ex.Message ?? string.Empty;
            if (ShouldTryEnsurePowerShellGetDefaultRepository(raw, options.Repository))
            {
                try { client.EnsureRepositoryRegistered(PSGalleryName, PSGalleryUriV2, PSGalleryUriV2, trusted: true, timeout: TimeSpan.FromMinutes(2)); } catch { }
                try { return client.Save(options, timeout: TimeSpan.FromMinutes(10)); } catch (Exception retryEx) { ex = retryEx; raw = retryEx.GetBaseException().Message ?? retryEx.Message ?? string.Empty; }
            }
            var reason = SimplifyPowerShellGetFailureMessage(raw);
            var hint = BuildPowerShellGetRepositoryHint(raw);

            var msg = string.IsNullOrWhiteSpace(reason)
                ? $"Save-Module failed while downloading required module '{moduleName}'."
                : $"Save-Module failed while downloading required module '{moduleName}'. {reason}";

            if (!string.IsNullOrWhiteSpace(hint))
                msg = $"{msg} {hint}";

            throw new InvalidOperationException(msg, ex);
        }
    }

    private void TryEnsurePsResourceGetDefaultRepository(PSResourceGetClient client, string? repositoryName)
    {
        if (client is null) return;

        var repo = string.IsNullOrWhiteSpace(repositoryName) ? PSGalleryName : repositoryName!.Trim();
        if (!repo.Equals(PSGalleryName, StringComparison.OrdinalIgnoreCase)) return;
        if (_ensuredPsResourceGetRepository) return;

        try
        {
            client.EnsureRepositoryRegistered(
                name: PSGalleryName,
                uri: PSGalleryUriV2,
                trusted: true,
                priority: null,
                apiVersion: RepositoryApiVersion.Auto,
                timeout: TimeSpan.FromMinutes(2));
            _ensuredPsResourceGetRepository = true;
        }
        catch (Exception ex)
        {
            if (_logger.IsVerbose)
                _logger.Verbose($"Failed to ensure PSResourceGet repository '{PSGalleryName}' is registered: {ex.Message}");
        }
    }

    private void TryEnsurePowerShellGetDefaultRepository(PowerShellGetClient client, string? repositoryName)
    {
        if (client is null) return;

        var repo = string.IsNullOrWhiteSpace(repositoryName) ? PSGalleryName : repositoryName!.Trim();
        if (!repo.Equals(PSGalleryName, StringComparison.OrdinalIgnoreCase)) return;
        if (_ensuredPowerShellGetRepository) return;

        try
        {
            client.EnsureRepositoryRegistered(
                name: PSGalleryName,
                sourceUri: PSGalleryUriV2,
                publishUri: PSGalleryUriV2,
                trusted: true,
                timeout: TimeSpan.FromMinutes(2));
            _ensuredPowerShellGetRepository = true;
        }
        catch (Exception ex)
        {
            if (_logger.IsVerbose)
                _logger.Verbose($"Failed to ensure PowerShellGet repository '{PSGalleryName}' is registered: {ex.Message}");
        }
    }

    private static bool ShouldTryEnsurePowerShellGetDefaultRepository(string message, string? repository)
    {
        if (!string.IsNullOrWhiteSpace(repository) && !repository!.Trim().Equals(PSGalleryName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(message)) return false;

        return message.IndexOf("Try Get-PSRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("No match was found for the specified search criteria", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("Unable to find module repositories", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("Unable to find repository", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SimplifyPowerShellGetFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var trimmed = message.Trim();

        // "Save-Module failed (exit X). <reason>" -> "<reason>"
        if (trimmed.StartsWith("Save-Module failed", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "). ";
            var idx = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && idx + marker.Length < trimmed.Length)
                return trimmed.Substring(idx + marker.Length).Trim();
        }

        return trimmed;
    }

    private static string BuildPowerShellGetRepositoryHint(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        if (message.IndexOf("Get-PSRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("No match was found for the specified search criteria", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Verify a repository is registered and reachable (Get-PSRepository). If PSGallery is missing, run Register-PSRepository -Default.";
        }

        return string.Empty;
    }

    private static string? NormalizeVersionArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Latest", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

    private sealed class PackagingInformation
    {
        public string[] ExcludeFromPackage { get; set; } = Array.Empty<string>();
        public string[] IncludeRoot { get; set; } = Array.Empty<string>();
        public string[] IncludePS1 { get; set; } = Array.Empty<string>();
        public string[] IncludeAll { get; set; } = Array.Empty<string>();
    }

    private static PackagingInformation ResolvePackagingInformation(InformationConfiguration? information)
    {
        var info = information ?? new InformationConfiguration();

        var includeRoot = (info.IncludeRoot is { Length: > 0 } ? info.IncludeRoot : DefaultIncludeRoot).ToArray();
        var includePS1 = (info.IncludePS1 is { Length: > 0 } ? info.IncludePS1 : DefaultIncludePS1).ToArray();
        var includeAll = (info.IncludeAll is { Length: > 0 } ? info.IncludeAll : DefaultIncludeAll).ToArray();
        var exclude = (info.ExcludeFromPackage is { Length: > 0 } ? info.ExcludeFromPackage : DefaultExcludeFromPackage).ToArray();

        if (info.IncludeToArray is { Length: > 0 })
        {
            foreach (var entry in info.IncludeToArray.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Key)))
            {
                if (entry.Values is not { Length: > 0 }) continue;
                if (entry.Key.Equals("IncludeRoot", StringComparison.OrdinalIgnoreCase)) includeRoot = entry.Values;
                if (entry.Key.Equals("IncludePS1", StringComparison.OrdinalIgnoreCase)) includePS1 = entry.Values;
                if (entry.Key.Equals("IncludeAll", StringComparison.OrdinalIgnoreCase)) includeAll = entry.Values;
                if (entry.Key.Equals("ExcludeFromPackage", StringComparison.OrdinalIgnoreCase)) exclude = entry.Values;
            }
        }

        static string[] Normalize(string[] values)
            => (values ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToArray();

        return new PackagingInformation
        {
            ExcludeFromPackage = Normalize(exclude),
            IncludeRoot = Normalize(includeRoot),
            IncludePS1 = Normalize(includePS1),
            IncludeAll = Normalize(includeAll),
        };
    }

    private static string ResolveOutputRoot(string? configuredPath, string projectRoot, string moduleName, string moduleVersion, string? preRelease, ArtefactType type)
    {
        var raw = BuildServices.ReplacePathTokens(configuredPath ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Default: <ProjectRoot>\Artefacts\<Type>
            return Path.GetFullPath(Path.Combine(projectRoot, "Artefacts", type.ToString()));
        }

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
    }

    private static string ResolveArtefactFileName(ArtefactConfiguration cfg, string moduleName, string moduleVersion, string? preRelease)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ArtefactName))
            return BuildServices.ReplacePathTokens(cfg.ArtefactName!.Trim(), moduleName, moduleVersion, preRelease);

        var tagWithPre = BuildServices.ReplacePathTokens("<TagModuleVersionWithPreRelease>", moduleName, moduleVersion, preRelease);
        return cfg.IncludeTagName == true
            ? $"{moduleName}.{tagWithPre}.zip"
            : $"{moduleName}.zip";
    }

    private static void CopyModulePackage(string stagingRoot, string destinationModuleRoot, PackagingInformation include)
    {
        var src = Path.GetFullPath(stagingRoot);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException($"Staging directory not found: {src}");

        if (Directory.Exists(destinationModuleRoot))
            Directory.Delete(destinationModuleRoot, recursive: true);
        Directory.CreateDirectory(destinationModuleRoot);

        var excludes = include.ExcludeFromPackage ?? Array.Empty<string>();

        bool IsExcludedName(string name)
            => WildcardAnyMatch(name, excludes);

        // 1) Root files
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(name) || IsExcludedName(name)) continue;
            if (!WildcardAnyMatch(name, include.IncludeRoot)) continue;
            File.Copy(file, Path.Combine(destinationModuleRoot, name), overwrite: true);
        }

        // 2) IncludeAll directories
        foreach (var dirName in include.IncludeAll)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            CopyDirectoryFiltered(dir, Path.Combine(destinationModuleRoot, dirName), include.ExcludeFromPackage ?? Array.Empty<string>(), includeOnlyPs1: false);
        }

        // 3) IncludePS1 directories
        foreach (var dirName in include.IncludePS1)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            CopyDirectoryFiltered(dir, Path.Combine(destinationModuleRoot, dirName), include.ExcludeFromPackage ?? Array.Empty<string>(), includeOnlyPs1: true);
        }
    }

    private static void CopyDirectoryFiltered(string sourceDir, string destDir, string[] excludeNamePatterns, bool includeOnlyPs1)
    {
        var sourceFull = Path.GetFullPath(sourceDir);
        Directory.CreateDirectory(destDir);

        var stack = new Stack<string>();
        stack.Push(sourceFull);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var rel = ComputeRelativePath(sourceFull, current);
            var targetDir = string.IsNullOrEmpty(rel) || rel == "." ? destDir : Path.Combine(destDir, rel);
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name) || WildcardAnyMatch(name, excludeNamePatterns)) continue;
                if (includeOnlyPs1 && !name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) continue;

                var destFile = Path.Combine(targetDir, name);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var dir in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (WildcardAnyMatch(name, excludeNamePatterns)) continue;
                stack.Push(dir);
            }
        }
    }

    private static void CopyExtraMappings(
        ArtefactConfiguration cfg,
        string projectRoot,
        string destinationRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        List<ArtefactCopyEntry> copied,
        bool enforceRelativeDestination = false)
    {
        foreach (var mapping in cfg.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null) continue;
            var src = ResolveInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease);
            var dest = ResolveOutputPath(mapping.Destination, destinationRoot, cfg.DestinationDirectoriesRelative == true, enforceRelativeDestination, moduleName, moduleVersion, preRelease);
            CopyDirectory(src, dest);
            copied.Add(new ArtefactCopyEntry(src, dest, isDirectory: true));
        }

        foreach (var mapping in cfg.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null) continue;
            var src = ResolveInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease);
            var dest = ResolveOutputPath(mapping.Destination, destinationRoot, cfg.DestinationFilesRelative == true, enforceRelativeDestination, moduleName, moduleVersion, preRelease);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
            copied.Add(new ArtefactCopyEntry(src, dest, isDirectory: false));
        }
    }

    private static string ResolveInputPath(string value, string projectRoot, string moduleName, string moduleVersion, string? preRelease)
    {
        var raw = BuildServices.ReplacePathTokens(value ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Copy mapping source path is empty.", nameof(value));
        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
    }

    private static string ResolveOutputPath(
        string value,
        string destinationRoot,
        bool relativeToRoot,
        bool enforceRelativeDestination,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var raw = BuildServices.ReplacePathTokens(value ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Copy mapping destination path is empty.", nameof(value));

        if (enforceRelativeDestination && Path.IsPathRooted(raw))
            throw new InvalidOperationException($"Packed artefact copy destinations must be relative, but got rooted path '{raw}'.");

        if (relativeToRoot || !Path.IsPathRooted(raw))
            return Path.GetFullPath(Path.Combine(destinationRoot, raw));

        return Path.GetFullPath(raw);
    }

    private static string ResolveRequiredModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var path = cfg.RequiredModules.Path;
        if (string.IsNullOrWhiteSpace(path))
            return outputRoot;

        var replaced = BuildServices.ReplacePathTokens(path ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(replaced)) return outputRoot;

        var full = Path.IsPathRooted(replaced) ? replaced : Path.Combine(outputRoot, replaced);
        return Path.GetFullPath(full);
    }

    private static string ResolveModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string requiredModulesRoot,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var path = cfg.RequiredModules.ModulesPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            // If RequiredModulesPath is set, default to the same location to keep a self-contained Modules folder.
            return requiredModulesRoot;
        }

        var replaced = BuildServices.ReplacePathTokens(path ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(replaced)) return requiredModulesRoot;

        var full = Path.IsPathRooted(replaced) ? replaced : Path.Combine(outputRoot, replaced);
        return Path.GetFullPath(full);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Directory not found: {sourceDir}");

        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);

        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file);
            var outPath = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.Copy(file, outPath, overwrite: true);
        }
    }

    private static void CreateZipFromDirectoryContents(string sourceDir, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file).Replace('\\', '/');
            var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(file);
            fileStream.CopyTo(entryStream);
        }
    }

    private static void ClearDirectorySafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clear root directory: {full}");

        if (Directory.Exists(full))
            Directory.Delete(full, recursive: true);

        Directory.CreateDirectory(full);
    }

    private static void ClearDirectoryContentsSafe(string path, IEnumerable<string>? excludePatterns = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clear root directory contents: {full}");

        if (!Directory.Exists(full)) return;

        var excludes = (excludePatterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray();

        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            try
            {
                var name = Path.GetFileName(entry);
                if (!string.IsNullOrWhiteSpace(name) && WildcardAnyMatch(name, excludes))
                    continue;

                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch { /* best effort */ }
        }
    }

    private static bool WildcardAnyMatch(string text, IEnumerable<string> patterns)
        => (patterns ?? Array.Empty<string>()).Any(p => WildcardMatch(text, p));

    private static bool WildcardMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (pattern == "*") return true;

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(text ?? string.Empty, regex, RegexOptions.IgnoreCase);
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch { return Path.GetFileName(fullPath) ?? fullPath; }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}

