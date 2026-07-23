using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private DotNetPublishMsiBuildResult BuildMsiPackage(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishMsiPrepareResult> prepares,
        DotNetPublishStep step,
        string reservationOwner)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (prepares is null) throw new ArgumentNullException(nameof(prepares));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var installerId = (step.InstallerId ?? string.Empty).Trim();
        var target = (step.TargetName ?? string.Empty).Trim();
        var framework = (step.Framework ?? string.Empty).Trim();
        var runtime = (step.Runtime ?? string.Empty).Trim();
        var style = step.Style;

        if (string.IsNullOrWhiteSpace(installerId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing InstallerId.");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtime))
            throw new InvalidOperationException($"Step '{step.Key}' is missing target/framework/runtime metadata.");
        if (!style.HasValue)
            throw new InvalidOperationException($"Step '{step.Key}' is missing style metadata.");

        var prepare = prepares
            .LastOrDefault(p =>
                string.Equals(p.InstallerId, installerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Target, target, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Framework, framework, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Runtime, runtime, StringComparison.OrdinalIgnoreCase)
                && p.Style == style.Value);

        if (prepare is null)
        {
            throw new InvalidOperationException(
                $"MSI build step '{step.Key}' could not find matching msi.prepare result for " +
                $"installer='{installerId}', target='{target}', framework='{framework}', runtime='{runtime}', style='{style.Value}'.");
        }

        var installerConfig = (plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
            .FirstOrDefault(i => string.Equals(i.Id, installerId, StringComparison.OrdinalIgnoreCase));
        var versionResolution = ResolveMsiVersionForStep(plan, installerConfig, step, reservationOwner);
        var licenseResolution = ResolveInstallerClientLicense(plan, installerConfig, step);
        var isGeneratedInstallerProject = IsGeneratedInstallerProject(step, installerConfig);

        var installerProjectPath = ResolveOrPrepareInstallerProjectPath(
            plan,
            step,
            installerConfig,
            prepare,
            versionResolution.Version);
        if (!File.Exists(installerProjectPath))
            throw new FileNotFoundException($"Installer project path not found: {installerProjectPath}", installerProjectPath);

        var projectDir = Path.GetDirectoryName(installerProjectPath)!;
        var buildProjectPath = installerProjectPath;
        var configuredOutputDir = ResolveInstallerOutputDirectory(
            plan,
            installerConfig,
            installerId,
            step,
            prepare,
            versionResolution.Version,
            isGeneratedInstallerProject);
        if (!string.IsNullOrWhiteSpace(configuredOutputDir))
            Directory.CreateDirectory(configuredOutputDir);

        var outputSearchDir = configuredOutputDir ?? projectDir;
        var skipOutputBinDirectoryFilter = isGeneratedInstallerProject || !string.IsNullOrWhiteSpace(configuredOutputDir);
        var before = SnapshotMsiOutputs(outputSearchDir, skipBinDirectoryFilter: skipOutputBinDirectoryFilter);

        using var generatedBuildWorkspace = isGeneratedInstallerProject
            ? PrepareGeneratedInstallerBuildWorkspace(installerId, projectDir, installerProjectPath, prepare, plan.ProjectRoot)
            : null;
        if (generatedBuildWorkspace is not null)
        {
            _logger.Info(
                $"Generated WiX installer project for '{installerId}' will build from short workspace '{generatedBuildWorkspace.WorkingDirectory}'.");
            buildProjectPath = generatedBuildWorkspace.ProjectPath;
        }

        var configuredOutputName = ResolveInstallerOutputName(plan, installerConfig, step, versionResolution.Version);
        using var outputReservation = ReserveVersionedOutput(
            configuredOutputDir,
            configuredOutputName,
            versionResolution.Version,
            installerConfig?.Versioning?.AllowOutputOverwrite == true,
            installerId,
            reservationOwner);
        var installerMsBuildProperties = BuildInstallerMsBuildProperties(
            plan.MsBuildProperties,
            installerConfig?.MsBuildProperties,
            versionResolution.PropertyName,
            versionResolution.Version,
            licenseResolution.PropertyName,
            licenseResolution.Path);
        var args = BuildMsiBuildArguments(
            plan,
            step,
            prepare,
            installerId,
            buildProjectPath,
            configuredOutputDir,
            configuredOutputName,
            installerMsBuildProperties,
            versionResolution,
            licenseResolution,
            isGeneratedInstallerProject,
            generatedBuildWorkspace?.PayloadDirectory,
            generatedBuildWorkspace?.HarvestPath);

        if (!string.IsNullOrWhiteSpace(versionResolution.Version) &&
            !string.IsNullOrWhiteSpace(versionResolution.PropertyName) &&
            !installerMsBuildProperties.ContainsKey(versionResolution.PropertyName!))
        {
            _logger.Info(
                $"MSI version for '{installerId}' resolved to {versionResolution.Version} ({versionResolution.PropertyName}).");
        }
        if (!string.IsNullOrWhiteSpace(licenseResolution.Path) &&
            !string.IsNullOrWhiteSpace(licenseResolution.PropertyName) &&
            !installerMsBuildProperties.ContainsKey(licenseResolution.PropertyName!))
            _logger.Info(
                $"MSI client license for '{installerId}' resolved to '{licenseResolution.Path}' ({licenseResolution.PropertyName}).");

        _logger.Info(
            $"MSI build starting for '{installerId}' ({target}, {framework}, {runtime}, {style.Value}) -> {Path.GetFileName(installerProjectPath)}");
        if (!string.IsNullOrWhiteSpace(configuredOutputDir))
            _logger.Info($"MSI output directory for '{installerId}' -> {configuredOutputDir}");
        if (!string.IsNullOrWhiteSpace(configuredOutputName))
            _logger.Info($"MSI output name for '{installerId}' -> {configuredOutputName}.msi");
        RunDotnet(plan.ProjectRoot, args, plan.EnvironmentVariables);

        var outputs = FindChangedMsiOutputs(outputSearchDir, before, skipBinDirectoryFilter: skipOutputBinDirectoryFilter);
        if (outputs.Length == 0)
            _logger.Warn($"MSI build for '{installerId}' completed, but no changed *.msi outputs were detected under '{outputSearchDir}'.");
        else
        {
            _logger.Info($"MSI build produced {outputs.Length} MSI output(s) for '{installerId}':");
            foreach (var output in outputs)
                _logger.Info($"  -> {output}");
        }
        var packageMetadata = ReadMsiPackageMetadata(outputs);

        return new DotNetPublishMsiBuildResult
        {
            InstallerId = installerId,
            Target = target,
            Framework = framework,
            Runtime = runtime,
            Style = style.Value,
            ProjectPath = installerProjectPath,
            GeneratedProject = isGeneratedInstallerProject,
            OutputFiles = outputs,
            PackageMetadata = packageMetadata,
            Version = versionResolution.Version,
            VersionPropertyName = versionResolution.PropertyName,
            VersionPatch = versionResolution.Patch,
            VersionStatePath = versionResolution.StatePath,
            VersionAuthority = versionResolution.Authority,
            VersionAuthorityReference = BuildMsiVersionAuthorityReference(versionResolution),
            ClientLicensePath = licenseResolution.Path,
            ClientLicensePropertyName = licenseResolution.PropertyName,
            ClientId = licenseResolution.ClientId
        };
    }

    private DotNetPublishMsiPackageMetadata[] ReadMsiPackageMetadata(IEnumerable<string> outputs)
    {
        var reader = new MsiPackageMetadataReader();
        var metadata = new List<DotNetPublishMsiPackageMetadata>();
        foreach (var output in outputs ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(output))
                continue;

            try
            {
                metadata.Add(reader.Read(output));
            }
            catch (Exception ex) when (ex is COMException
                                           or InvalidOperationException
                                           or PlatformNotSupportedException
                                           or FileNotFoundException
                                           or UnauthorizedAccessException)
            {
                var message = ex.GetBaseException().Message;
                _logger.Warn($"MSI metadata could not be read for '{output}': {message}");
                metadata.Add(new DotNetPublishMsiPackageMetadata
                {
                    Path = Path.GetFullPath(output),
                    ReadError = message
                });
            }
        }

        return metadata.ToArray();
    }

    private string ResolveOrPrepareInstallerProjectPath(
        DotNetPublishPlan plan,
        DotNetPublishStep step,
        DotNetPublishInstallerPlan? installer,
        DotNetPublishMsiPrepareResult prepare,
        string? productVersion)
    {
        if (!string.IsNullOrWhiteSpace(step.InstallerProjectPath))
            return Path.GetFullPath(step.InstallerProjectPath!);

        var installerId = (step.InstallerId ?? installer?.Id ?? string.Empty).Trim();
        var configuredProjectPath = installer?.InstallerProjectPath;
        if (!string.IsNullOrWhiteSpace(configuredProjectPath))
            return Path.GetFullPath(configuredProjectPath!);

        if (installer?.Authoring is null)
            throw new InvalidOperationException($"Installer project path not configured for installer '{installerId}'.");

        var definition = CloneInstallerDefinition(installer.Authoring)!;
        ResolveGeneratedInstallerAuthoringPaths(plan, definition);
        if (!string.IsNullOrWhiteSpace(productVersion))
            definition.Product.Version = productVersion!;
        if (string.IsNullOrWhiteSpace(definition.PayloadComponentGroupId) &&
            !string.IsNullOrWhiteSpace(prepare.HarvestComponentGroupId))
        {
            definition.PayloadComponentGroupId = prepare.HarvestComponentGroupId;
        }
        if (!string.IsNullOrWhiteSpace(definition.PayloadComponentGroupId) &&
            string.IsNullOrWhiteSpace(prepare.HarvestPath))
        {
            throw new InvalidOperationException(
                $"Generated installer '{installerId}' references payload component group '{definition.PayloadComponentGroupId}' but no harvested WiX source is available.");
        }

        var generatedDir = ResolveGeneratedInstallerProjectDirectory(plan, installerId, step, prepare);
        var request = new PowerForgeWixInstallerCompileRequest
        {
            WorkingDirectory = generatedDir,
            SourceFileName = "Product.wxs",
            ProjectFileName = SanitizeWixIdentifier(installerId, "Installer") + ".wixproj",
            Configuration = plan.Configuration,
            NoRestore = plan.Restore
        };
        AddGeneratedInstallerDefineConstants(request, prepare);
        if (!string.IsNullOrWhiteSpace(prepare.HarvestPath))
            request.AdditionalSourceFiles.Add(prepare.HarvestPath!);

        var workspace = new PowerForgeWixInstallerCompiler()
            .PrepareWorkspace(definition, request);
        _logger.Info($"Generated WiX installer project for '{installerId}' -> {workspace.ProjectPath}");
        return workspace.ProjectPath;
    }

    private static void ResolveGeneratedInstallerAuthoringPaths(
        DotNetPublishPlan plan,
        PowerForgeInstallerDefinition definition)
    {
        if (definition.LicenseAgreement is not { Enabled: true } licenseAgreement ||
            string.IsNullOrWhiteSpace(licenseAgreement.Path) ||
            Path.IsPathRooted(licenseAgreement.Path))
        {
            return;
        }

        licenseAgreement.Path = ResolvePath(plan.ProjectRoot, licenseAgreement.Path);
    }

    private static bool IsGeneratedInstallerProject(
        DotNetPublishStep step,
        DotNetPublishInstallerPlan? installer)
    {
        return string.IsNullOrWhiteSpace(step.InstallerProjectPath)
            && string.IsNullOrWhiteSpace(installer?.InstallerProjectPath)
            && installer?.Authoring is not null;
    }

    private static string ResolveGeneratedInstallerProjectDirectory(
        DotNetPublishPlan plan,
        string installerId,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare)
    {
        var artifactDir = ResolveGeneratedInstallerArtifactDirectory(plan, installerId, step, prepare);
        var path = Path.Combine(artifactDir, "generated");
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, path, $"Installer '{installerId}' generated WiX project path");
        return path;
    }

    private static string ResolveGeneratedInstallerOutputDirectory(
        DotNetPublishPlan plan,
        string installerId,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare)
    {
        var artifactDir = ResolveGeneratedInstallerArtifactDirectory(plan, installerId, step, prepare);
        var path = Path.Combine(artifactDir, "output");
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, path, $"Installer '{installerId}' generated MSI output path");
        return path;
    }

    internal static string? ResolveInstallerOutputDirectory(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan? installer,
        string installerId,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare,
        string? version,
        bool isGeneratedInstallerProject)
    {
        if (installer is not null && !string.IsNullOrWhiteSpace(installer.OutputPath))
        {
            var tokens = BuildInstallerOutputTokens(plan, installerId, step, version);
            var outputPath = installer.OutputPath!;
            var path = ResolvePath(plan.ProjectRoot, ApplyTemplate(outputPath, tokens));
            if (!plan.AllowOutputOutsideProjectRoot)
                EnsurePathWithinRoot(plan.ProjectRoot, path, $"Installer '{installerId}' MSI output path");
            return path;
        }

        return isGeneratedInstallerProject
            ? ResolveGeneratedInstallerOutputDirectory(plan, installerId, step, prepare)
            : null;
    }

    internal static string? ResolveInstallerOutputName(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan? installer,
        DotNetPublishStep step,
        string? version)
    {
        if (installer is null)
            return null;

        var hasVersion = !string.IsNullOrWhiteSpace(version);
        string? configuredName = installer.OutputName;
        if (string.IsNullOrWhiteSpace(configuredName) && hasVersion)
        {
            configuredName = string.Join(
                "-",
                new[]
                {
                    step.TargetName,
                    installer.Id,
                    step.Runtime,
                    step.Framework,
                    step.Style?.ToString(),
                    version
                }
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));
        }

        if (string.IsNullOrWhiteSpace(configuredName))
            return null;

        var tokens = BuildInstallerOutputTokens(plan, installer.Id, step, version);
        var name = ApplyTemplate(configuredName!, tokens).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = ToSafeFileName(name, "installer");
        name = name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;

        if (hasVersion && !ContainsCompleteVersionToken(name, version!))
            name = ToSafeFileName($"{name}-{version}", "installer");

        return name;
    }

    private static bool ContainsCompleteVersionToken(string name, string version)
    {
        var searchIndex = 0;
        while (searchIndex <= name.Length - version.Length)
        {
            var matchIndex = name.IndexOf(version, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                return false;

            var beforeIsBoundary = matchIndex == 0
                || IsVersionTokenDelimiter(name[matchIndex - 1])
                || (IsVersionPrefix(name[matchIndex - 1])
                    && (matchIndex == 1 || IsVersionTokenDelimiter(name[matchIndex - 2])));
            var afterIndex = matchIndex + version.Length;
            var afterIsBoundary = afterIndex == name.Length
                || IsVersionTokenDelimiter(name[afterIndex]);
            if (beforeIsBoundary && afterIsBoundary)
                return true;

            searchIndex = matchIndex + 1;
        }

        return false;
    }

    private static bool IsVersionPrefix(char value) => value is 'v' or 'V';

    private static bool IsVersionTokenDelimiter(char value) =>
        !char.IsLetterOrDigit(value) && value != '.';

    private static Dictionary<string, string> BuildInstallerOutputTokens(
        DotNetPublishPlan plan,
        string installerId,
        DotNetPublishStep step,
        string? version)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["installer"] = installerId,
            ["target"] = step.TargetName ?? string.Empty,
            ["rid"] = step.Runtime ?? string.Empty,
            ["framework"] = step.Framework ?? string.Empty,
            ["style"] = step.Style?.ToString() ?? string.Empty,
            ["configuration"] = plan.Configuration ?? "Release",
            ["version"] = version ?? string.Empty
        };
    }

    private static string ResolveGeneratedInstallerArtifactDirectory(
        DotNetPublishPlan plan,
        string installerId,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare)
    {
        if (!string.IsNullOrWhiteSpace(prepare.ManifestPath))
        {
            var manifestPath = Path.GetFullPath(prepare.ManifestPath);
            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(manifestDirectory))
            {
                var manifestName = ToSafeFileName(
                    Path.GetFileNameWithoutExtension(manifestPath),
                    "manifest");
                return Path.Combine(manifestDirectory, manifestName);
            }
        }

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["installer"] = installerId,
            ["target"] = step.TargetName ?? string.Empty,
            ["rid"] = step.Runtime ?? string.Empty,
            ["framework"] = step.Framework ?? string.Empty,
            ["style"] = step.Style?.ToString() ?? string.Empty,
            ["configuration"] = plan.Configuration ?? "Release"
        };
        var relativePath = ApplyTemplate(
            "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}",
            tokens);
        var path = ResolvePath(plan.ProjectRoot, relativePath);
        return path;
    }

    private static void AddGeneratedInstallerDefineConstants(
        PowerForgeWixInstallerCompileRequest request,
        DotNetPublishMsiPrepareResult prepare)
    {
        request.DefineConstants["PayloadDir"] = prepare.StagingDir;
        request.DefineConstants["PowerForgeMsiPayloadDir"] = prepare.StagingDir;
        if (!string.IsNullOrWhiteSpace(prepare.ManifestPath))
            request.DefineConstants["PowerForgeMsiPrepareManifest"] = prepare.ManifestPath;
        request.DefineConstants["PowerForgeMsiSourceTarget"] = prepare.Target;
        request.DefineConstants["PowerForgeMsiSourceFramework"] = prepare.Framework;
        request.DefineConstants["PowerForgeMsiSourceRuntime"] = prepare.Runtime;
        request.DefineConstants["PowerForgeMsiSourceStyle"] = prepare.Style.ToString();
        if (!string.IsNullOrWhiteSpace(prepare.HarvestPath))
            request.DefineConstants["PowerForgeMsiHarvestPath"] = prepare.HarvestPath!;
        if (!string.IsNullOrWhiteSpace(prepare.HarvestDirectoryRefId))
            request.DefineConstants["PowerForgeMsiHarvestDirectoryRefId"] = prepare.HarvestDirectoryRefId!;
        if (!string.IsNullOrWhiteSpace(prepare.HarvestComponentGroupId))
            request.DefineConstants["PowerForgeMsiHarvestComponentGroupId"] = prepare.HarvestComponentGroupId!;
    }

    internal static Dictionary<string, string> BuildInstallerMsBuildProperties(
        IReadOnlyDictionary<string, string>? globalProperties,
        IReadOnlyDictionary<string, string>? installerProperties,
        string? versionPropertyName,
        string? versionValue,
        string? licensePropertyName,
        string? licensePath)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (globalProperties is not null)
        {
            foreach (var entry in globalProperties)
                merged[entry.Key] = entry.Value;
        }

        if (installerProperties is not null)
        {
            foreach (var entry in installerProperties)
                merged[entry.Key] = entry.Value;
        }

        if (!string.IsNullOrWhiteSpace(versionValue) &&
            !string.IsNullOrWhiteSpace(versionPropertyName) &&
            !merged.ContainsKey(versionPropertyName!))
        {
            merged[versionPropertyName!] = versionValue!;
        }

        if (!string.IsNullOrWhiteSpace(licensePath) &&
            !string.IsNullOrWhiteSpace(licensePropertyName) &&
            !merged.ContainsKey(licensePropertyName!))
        {
            merged[licensePropertyName!] = licensePath!;
        }

        return merged;
    }

    private static Dictionary<string, DateTime> SnapshotMsiOutputs(string root, bool skipBinDirectoryFilter = false)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateMsiFiles(root, skipBinDirectoryFilter))
        {
            try
            {
                map[file] = File.GetLastWriteTimeUtc(file);
            }
            catch
            {
                // best effort
            }
        }

        return map;
    }

    private static string[] FindChangedMsiOutputs(
        string root,
        IReadOnlyDictionary<string, DateTime> before,
        bool skipBinDirectoryFilter = false)
    {
        var results = new List<string>();
        foreach (var file in EnumerateMsiFiles(root, skipBinDirectoryFilter))
        {
            try
            {
                var now = File.GetLastWriteTimeUtc(file);
                if (!before.TryGetValue(file, out var previous) || now > previous)
                    results.Add(Path.GetFullPath(file));
            }
            catch
            {
                // best effort
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateMsiFiles(string root, bool skipBinDirectoryFilter = false)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<string>();

        try
        {
            var files = Directory.EnumerateFiles(root, "*.msi", SearchOption.AllDirectories);
            if (skipBinDirectoryFilter)
                return files;

            return files.Where(p =>
                p.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf($"{Path.AltDirectorySeparatorChar}bin{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private MsiVersionResolution ResolveMsiVersion(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan? installer,
        DotNetPublishStep step,
        IReadOnlyDictionary<string, MsiVersionState>? stateOverrides = null)
    {
        if (installer?.Versioning is null || !installer.Versioning.Enabled)
            return MsiVersionResolution.None();

        var v = installer.Versioning;
        var major = Clamp(v.Major, 0, 255);
        var minor = Clamp(v.Minor, 0, 255);
        var patchCap = Clamp(v.PatchCap, 1, 65535);

        var basePatch = ResolveMsiVersionSegments(v, ref major, ref minor);
        var patch = Clamp(basePatch, 0, patchCap);
        var propertyName = string.IsNullOrWhiteSpace(v.PropertyName) ? "ProductVersion" : v.PropertyName!.Trim();
        string? statePath = null;
        string? coordinationKey = null;
        string? authorityKey = null;
        string? gitRemote = null;
        string? gitTagPrefix = null;

        if (v.Monotonic)
        {
            var tokens = BuildMsiVersionTemplateTokens(plan, installer, step);
            statePath = ResolveMsiVersionStatePath(plan, installer, tokens);

            MsiVersionState? authorityState = null;
            if (v.Authority == DotNetPublishMsiVersionAuthorityKind.GitTags)
            {
                if (v.AllowOutputOverwrite)
                {
                    throw new InvalidOperationException(
                        $"Installer '{installer.Id}' cannot combine Versioning.Authority=GitTags with " +
                        "Versioning.AllowOutputOverwrite. Shared release identities are immutable.");
                }

                var authorityTemplate = string.IsNullOrWhiteSpace(v.AuthorityKey)
                    ? installer.Id
                    : v.AuthorityKey!;
                authorityKey = NormalizeMsiGitRefPath(
                    ApplyTemplate(authorityTemplate, tokens),
                    "MSI version authority key");
                gitRemote = NormalizeMsiGitRemote(v.GitRemote);
                gitTagPrefix = NormalizeMsiGitRefPath(v.GitTagPrefix, "MSI version Git tag prefix", "powerforge-msi");
                coordinationKey = $"git:{gitRemote}:{gitTagPrefix}:{authorityKey}";
                authorityState = ReadMsiGitTagVersionState(
                    plan.ProjectRoot,
                    gitRemote,
                    gitTagPrefix,
                    authorityKey);
            }
            else
            {
                coordinationKey = statePath;
            }

            MsiVersionState? plannedState = null;
            var hasPlannedState = stateOverrides is not null
                                  && stateOverrides.TryGetValue(coordinationKey, out plannedState);
            var previous = SelectLatestMsiVersionState(
                plannedState,
                ReadMsiVersionState(statePath),
                authorityState);
            ThrowIfMsiVersionLineRegresses(previous, major, minor, installer.Id);
            if (!hasPlannedState
                && v.AllowOutputOverwrite
                && TryResolveReusableMsiPatch(previous, major, minor, patch, patchCap, out var reusablePatch))
            {
                patch = reusablePatch;
            }
            else if (ShouldBumpMsiPatch(previous, major, minor, patch))
            {
                if (previous!.LastPatch >= patchCap)
                {
                    throw new InvalidOperationException(
                        $"Installer '{installer.Id}' cannot advance beyond MSI patch '{previous.LastPatch}' " +
                        $"because Versioning.PatchCap is '{patchCap}'. Increase the version line or patch cap.");
                }

                patch = previous.LastPatch + 1;
            }

            ThrowIfMsiVersionRegresses(
                previous,
                major,
                minor,
                patch,
                allowEqual: v.AllowOutputOverwrite,
                installer.Id);
        }

        if (patch >= patchCap && basePatch > patchCap)
        {
            _logger.Warn(
                $"MSI version patch exceeded cap ({patchCap}) for installer '{installer.Id}'. " +
                $"Using capped patch value {patchCap}.");
        }

        var version = $"{major}.{minor}.{patch}";
        return new MsiVersionResolution(
            version,
            propertyName,
            patch,
            statePath,
            coordinationKey,
            v.Authority,
            authorityKey,
            gitRemote,
            gitTagPrefix);
    }

    private static Dictionary<string, string> BuildMsiVersionTemplateTokens(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan installer,
        DotNetPublishStep step)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["installer"] = installer.Id,
            ["target"] = step.TargetName ?? string.Empty,
            ["rid"] = step.Runtime ?? string.Empty,
            ["framework"] = step.Framework ?? string.Empty,
            ["style"] = step.Style?.ToString() ?? string.Empty,
            ["configuration"] = plan.Configuration ?? "Release"
        };

    private static string ResolveMsiVersionStatePath(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan installer,
        IReadOnlyDictionary<string, string> tokens)
    {
        var stateTemplate = string.IsNullOrWhiteSpace(installer.Versioning?.StatePath)
            ? "Artifacts/DotNetPublish/Msi/{installer}/version.state.json"
            : installer.Versioning!.StatePath!;
        var statePath = ResolvePath(plan.ProjectRoot, ApplyTemplate(stateTemplate, tokens));

        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, statePath, $"Installer '{installer.Id}' version state path");

        return statePath;
    }

    private MsiVersionResolution ResolveMsiVersionForStep(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan? installer,
        DotNetPublishStep step,
        string reservationOwner)
    {
        if (installer is not null)
        {
            var cached = FindResolvedMsiVersion(plan, installer.Id, step.TargetName, step.Framework, step.Runtime, step.Style);
            if (cached is not null)
            {
                ReserveMsiVersionState(
                    cached,
                    $"MSI build for installer '{installer.Id}'",
                    reservationOwner,
                    cached.AllowOutputOverwrite);
                return new MsiVersionResolution(
                    cached.Version,
                    cached.VersionPropertyName,
                    cached.Patch,
                    cached.StatePath,
                    BuildMsiVersionCoordinationKey(cached),
                    cached.Authority,
                    cached.AuthorityKey,
                    cached.GitRemote,
                    cached.GitTagPrefix);
            }
        }

        var resolved = ResolveMsiVersion(plan, installer, step);
        var versionPlan = new DotNetPublishMsiVersionPlan
        {
            Version = resolved.Version ?? string.Empty,
            VersionPropertyName = resolved.PropertyName,
            AssemblyVersion = BuildFourPartVersion(resolved.Version ?? string.Empty),
            Patch = resolved.Patch,
            StatePath = resolved.StatePath,
            Authority = resolved.Authority,
            AuthorityKey = resolved.AuthorityKey,
            GitRemote = resolved.GitRemote,
            GitTagPrefix = resolved.GitTagPrefix,
            AuthorityWorkingDirectory = plan.ProjectRoot,
            AllowOutputOverwrite = installer?.Versioning?.AllowOutputOverwrite == true
        };
        ReserveMsiVersionState(
            versionPlan,
            $"MSI build for installer '{installer?.Id ?? step.InstallerId}'",
            reservationOwner,
            versionPlan.AllowOutputOverwrite);
        if (installer is not null && step.Style.HasValue)
        {
            var key = BuildMsiVersionKey(
                installer.Id,
                step.TargetName,
                step.Framework,
                step.Runtime,
                step.Style.Value);
            plan.MsiVersions[key] = versionPlan;
        }
        return resolved;
    }

    private static DotNetPublishMsiVersionPlan? FindResolvedMsiVersion(
        DotNetPublishPlan plan,
        string? installerId,
        string? targetName,
        string? framework,
        string? runtime,
        DotNetPublishStyle? style)
    {
        if (plan.MsiVersions is null || plan.MsiVersions.Count == 0 || !style.HasValue)
            return null;

        var key = BuildMsiVersionKey(installerId, targetName, framework, runtime, style.Value);
        return plan.MsiVersions.TryGetValue(key, out var version) ? version : null;
    }

    private static string BuildMsiVersionKey(
        string? installerId,
        string? targetName,
        string? framework,
        string? runtime,
        DotNetPublishStyle style)
    {
        return string.Join(
            "|",
            installerId?.Trim() ?? string.Empty,
            targetName?.Trim() ?? string.Empty,
            framework?.Trim() ?? string.Empty,
            runtime?.Trim() ?? string.Empty,
            style.ToString());
    }

    private static string BuildFourPartVersion(string version)
    {
        var parts = (version ?? string.Empty)
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim())
            .Where(static part => part.Length > 0)
            .Take(4)
            .ToList();
        while (parts.Count < 4)
            parts.Add("0");

        return string.Join(".", parts);
    }

    internal static void ReserveMsiVersionState(
        DotNetPublishMsiVersionPlan version,
        string context,
        string reservationOwner,
        bool allowOutputOverwrite = false)
    {
        if (version is null || string.IsNullOrWhiteSpace(version.StatePath) || !version.Patch.HasValue)
            return;

        if (!TryParseMsiVersion(version.Version, out var major, out var minor, out var patch))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(version.StatePath!)!);
        FileStream stateStream;
        try
        {
            stateStream = new FileStream(
                version.StatePath!,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"MSI version state '{version.StatePath}' is being reserved by another publish.",
                ex);
        }

        using (stateStream)
        {
            var previous = ReadMsiVersionState(stateStream);
            if (previous is not null
                && previous.LastPatch == patch
                && string.Equals(previous.Version, version.Version, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(previous.ReservationOwner, reservationOwner, StringComparison.Ordinal))
                    return;

                if (allowOutputOverwrite && string.IsNullOrWhiteSpace(previous.ReservationOwner))
                {
                    WriteMsiVersionState(stateStream, patch, version.Version, reservationOwner);
                    return;
                }

                throw new InvalidOperationException(
                    $"MSI version '{version.Version}' in state '{version.StatePath}' is already reserved " +
                    "by another publish. Re-plan or rerun to allocate the next version.");
            }

            if (ShouldBumpMsiPatch(previous, major, minor, patch))
            {
                var previousVersion = string.IsNullOrWhiteSpace(previous?.Version)
                    ? previous?.LastPatch.ToString(CultureInfo.InvariantCulture)
                    : previous!.Version;
                throw new InvalidOperationException(
                    $"MSI version state '{version.StatePath}' advanced to '{previousVersion}' before {context} could reserve '{version.Version}'. Re-plan or rerun the publish to avoid duplicate MSI versions.");
            }

            ReserveMsiGitTagVersion(version, context, reservationOwner);

            WriteMsiVersionState(stateStream, patch, version.Version, reservationOwner);
        }
    }

    internal static bool ReleaseMsiVersionStateReservation(
        DotNetPublishMsiVersionPlan version,
        string reservationOwner)
    {
        if (version is null || string.IsNullOrWhiteSpace(version.StatePath) || !version.Patch.HasValue)
            return true;

        try
        {
            using var stateStream = new FileStream(
                version.StatePath!,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            var current = ReadMsiVersionState(stateStream);
            if (current is null
                || current.LastPatch != version.Patch.Value
                || !string.Equals(current.Version, version.Version, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.ReservationOwner, reservationOwner, StringComparison.Ordinal))
            {
                return true;
            }

            WriteMsiVersionState(stateStream, version.Patch.Value, version.Version, reservationOwner: null);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int ResolveMsiVersionSegments(DotNetPublishMsiVersionOptions options, ref int major, ref int minor)
    {
        switch (options.Pattern)
        {
            case DotNetPublishMsiVersionPattern.UtcShortYearMonthDayMinute:
                var stamp = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(options.FloorDateUtc))
                {
                    var floor = ParseUtcDate(options.FloorDateUtc!);
                    if (floor > stamp)
                        stamp = floor;
                }

                major = Clamp(stamp.Year - 2000, 0, 255);
                minor = Clamp(stamp.Month, 0, 255);
                return (stamp.Day * 1440) + (stamp.Hour * 60) + stamp.Minute;
            case DotNetPublishMsiVersionPattern.FixedMajorMinorDatePatch:
            default:
                var floorDate = string.IsNullOrWhiteSpace(options.FloorDateUtc)
                    ? DateTime.UtcNow.Date
                    : ParseUtcDate(options.FloorDateUtc!);
                return DaysSince20000101(floorDate);
        }
    }

    private MsiClientLicenseResolution ResolveInstallerClientLicense(
        DotNetPublishPlan plan,
        DotNetPublishInstallerPlan? installer,
        DotNetPublishStep step)
    {
        if (installer is null)
            return MsiClientLicenseResolution.None();

        var rawOptions = installer.ClientLicense;
        if (rawOptions is null || !rawOptions.Enabled)
            return MsiClientLicenseResolution.None();
        var options = rawOptions!;

        var clientId = (options.ClientId ?? string.Empty).Trim();
        if (clientId.Length == 0)
            clientId = null;

        var propertyName = (options.PropertyName ?? string.Empty).Trim();
        if (propertyName.Length == 0)
            propertyName = "ClientLicensePath";

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["installer"] = installer.Id,
            ["target"] = step.TargetName ?? string.Empty,
            ["rid"] = step.Runtime ?? string.Empty,
            ["framework"] = step.Framework ?? string.Empty,
            ["style"] = step.Style?.ToString() ?? string.Empty,
            ["configuration"] = plan.Configuration ?? "Release",
            ["clientId"] = clientId ?? string.Empty
        };

        var pathTemplate = !string.IsNullOrWhiteSpace(options.Path)
            ? options.Path!
            : (string.IsNullOrWhiteSpace(options.PathTemplate)
                ? "Installer/Clients/{clientId}/{target}.txlic"
                : options.PathTemplate!);
        var resolvedPath = ResolvePath(plan.ProjectRoot, ApplyTemplate(pathTemplate, tokens));
        if (File.Exists(resolvedPath))
            return new MsiClientLicenseResolution(resolvedPath, propertyName, clientId);

        HandlePolicy(
            options.OnMissingFile,
            $"Installer '{installer.Id}' client license file was not found: {resolvedPath}.");
        return MsiClientLicenseResolution.None();
    }

    private static MsiVersionState? ReadMsiVersionState(string statePath)
    {
        try
        {
            if (!File.Exists(statePath)) return null;
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<MsiVersionState>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch
        {
            return null;
        }
    }

    private static MsiVersionState? ReadMsiVersionState(Stream stream)
    {
        if (stream.Length == 0)
            return null;

        stream.Position = 0;
        return JsonSerializer.Deserialize<MsiVersionState>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static bool ShouldBumpMsiPatch(MsiVersionState? previous, int major, int minor, int patch)
    {
        if (previous is null || previous.LastPatch < patch)
            return false;

        if (TryParseMsiVersion(previous.Version, out var previousMajor, out var previousMinor, out _)
            && (previousMajor < major || (previousMajor == major && previousMinor < minor)))
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveReusableMsiPatch(
        MsiVersionState? previous,
        int major,
        int minor,
        int candidatePatch,
        int patchCap,
        out int patch)
    {
        patch = candidatePatch;
        if (previous is null
            || !TryParseMsiVersion(previous.Version, out var previousMajor, out var previousMinor, out var previousPatch)
            || previousMajor != major
            || previousMinor != minor
            || previous.LastPatch != previousPatch
            || previousPatch < candidatePatch
            || previousPatch > patchCap)
        {
            return false;
        }

        patch = previousPatch;
        return true;
    }

    private static bool TryParseMsiVersion(string? version, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var parts = version!.Split('.');
        return parts.Length >= 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out patch);
    }

    private static void WriteMsiVersionState(
        Stream stream,
        int patch,
        string version,
        string? reservationOwner)
    {
        var state = new MsiVersionState
        {
            LastPatch = patch,
            Version = version,
            UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ReservationOwner = reservationOwner
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        stream.SetLength(0);
        stream.Position = 0;
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);
        writer.Write(json);
        writer.Flush();
        if (stream is FileStream fileStream)
            fileStream.Flush(flushToDisk: true);
    }

    private static DateTime ParseUtcDate(string value)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd" };
        if (DateTime.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed.Date;
        }

        throw new InvalidOperationException($"Invalid MSI floor date '{value}'. Expected yyyy-MM-dd or yyyyMMdd.");
    }

    private static int DaysSince20000101(DateTime utcDate)
    {
        var floor = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candidate = utcDate.Kind == DateTimeKind.Utc ? utcDate.Date : utcDate.ToUniversalTime().Date;
        return (int)(candidate - floor).TotalDays;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private sealed class MsiVersionState
    {
        public int LastPatch { get; set; }
        public string? Version { get; set; }
        public string? UpdatedUtc { get; set; }
        public string? ReservationOwner { get; set; }
    }

    private sealed class MsiVersionResolution
    {
        public string? Version { get; }
        public string? PropertyName { get; }
        public int? Patch { get; }
        public string? StatePath { get; }
        public string? CoordinationKey { get; }
        public DotNetPublishMsiVersionAuthorityKind Authority { get; }
        public string? AuthorityKey { get; }
        public string? GitRemote { get; }
        public string? GitTagPrefix { get; }

        public MsiVersionResolution(
            string? version,
            string? propertyName,
            int? patch,
            string? statePath,
            string? coordinationKey,
            DotNetPublishMsiVersionAuthorityKind authority,
            string? authorityKey,
            string? gitRemote,
            string? gitTagPrefix)
        {
            Version = version;
            PropertyName = propertyName;
            Patch = patch;
            StatePath = statePath;
            CoordinationKey = coordinationKey;
            Authority = authority;
            AuthorityKey = authorityKey;
            GitRemote = gitRemote;
            GitTagPrefix = gitTagPrefix;
        }

        public static MsiVersionResolution None()
            => new(
                version: null,
                propertyName: null,
                patch: null,
                statePath: null,
                coordinationKey: null,
                authority: DotNetPublishMsiVersionAuthorityKind.LocalFile,
                authorityKey: null,
                gitRemote: null,
                gitTagPrefix: null);
    }

    private sealed class MsiClientLicenseResolution
    {
        public string? Path { get; }
        public string? PropertyName { get; }
        public string? ClientId { get; }

        public MsiClientLicenseResolution(string? path, string? propertyName, string? clientId)
        {
            Path = path;
            PropertyName = propertyName;
            ClientId = clientId;
        }

        public static MsiClientLicenseResolution None()
            => new(path: null, propertyName: null, clientId: null);
    }
}
