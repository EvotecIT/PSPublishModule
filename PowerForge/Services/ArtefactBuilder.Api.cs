using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
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
    /// <param name="includeScriptFolders">When false, skips packaging script-only folders (Public/Private/Classes/Enums).</param>
    public ArtefactBuildResult Build(
        ConfigurationArtefactSegment segment,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information = null,
        bool includeScriptFolders = true)
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
            ArtefactType.Unpacked => BuildUnpacked(cfg, root, projectRoot, stagingPath, moduleName, moduleVersion, preRelease, requiredModules, information, includeScriptFolders),
            ArtefactType.Packed => BuildPacked(cfg, root, projectRoot, stagingPath, moduleName, moduleVersion, preRelease, requiredModules, information, includeScriptFolders),
            _ => throw new NotSupportedException($"Artefact type '{segment.ArtefactType}' is not supported yet.")
        };
    }

    internal static void CopyModulePackageForInstall(
        string stagingRoot,
        string destinationModuleRoot,
        InformationConfiguration? information,
        bool includeScriptFolders = true)
    {
        var include = ResolvePackagingInformation(information, includeScriptFolders);
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
        InformationConfiguration? information,
        bool includeScriptFolders)
    {
        if (cfg.DoNotClear != true)
            ClearDirectorySafe(outputRoot);
        Directory.CreateDirectory(outputRoot);

        var include = ResolvePackagingInformation(information, includeScriptFolders);

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
        InformationConfiguration? information,
        bool includeScriptFolders)
    {
        Directory.CreateDirectory(outputRoot);
        if (cfg.DoNotClear != true)
            ClearDirectoryContentsSafe(outputRoot, excludePatterns: new[] { "*.zip" }, includeDirectories: false);

        var include = ResolvePackagingInformation(information, includeScriptFolders);

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

}
