using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private DotNetPublishMsiBuildResult BuildMsiPackage(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishMsiPrepareResult> prepares,
        DotNetPublishStep step)
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
        var versionResolution = ResolveMsiVersion(plan, installerConfig, step);
        var licenseResolution = ResolveInstallerClientLicense(plan, installerConfig, step);

        var installerProjectPath = ResolveOrPrepareInstallerProjectPath(
            plan,
            step,
            installerConfig,
            prepare,
            versionResolution.Version);
        if (!File.Exists(installerProjectPath))
            throw new FileNotFoundException($"Installer project path not found: {installerProjectPath}", installerProjectPath);

        var projectDir = Path.GetDirectoryName(installerProjectPath)!;
        var before = SnapshotMsiOutputs(projectDir);

        var args = new List<string>
        {
            "build",
            installerProjectPath,
            "-c",
            plan.Configuration,
            "--nologo"
        };

        if (plan.Restore)
            args.Add("--no-restore");

        var installerMsBuildProperties = BuildInstallerMsBuildProperties(
            plan.MsBuildProperties,
            installerConfig?.MsBuildProperties,
            versionResolution.PropertyName,
            versionResolution.Version,
            licenseResolution.PropertyName,
            licenseResolution.Path);
        args.AddRange(BuildMsBuildPropertyArgs(installerMsBuildProperties));
        args.Add($"/p:PowerForgeMsiInstallerId={installerId}");
        args.Add($"/p:PowerForgeMsiPayloadDir={prepare.StagingDir}");
        args.Add($"/p:PowerForgeMsiPrepareManifest={prepare.ManifestPath}");
        args.Add($"/p:PowerForgeMsiSourceTarget={prepare.Target}");
        args.Add($"/p:PowerForgeMsiSourceFramework={prepare.Framework}");
        args.Add($"/p:PowerForgeMsiSourceRuntime={prepare.Runtime}");
        args.Add($"/p:PowerForgeMsiSourceStyle={prepare.Style}");
        if (!string.IsNullOrWhiteSpace(prepare.HarvestPath))
            args.Add($"/p:PowerForgeMsiHarvestPath={prepare.HarvestPath}");
        if (!string.IsNullOrWhiteSpace(prepare.HarvestDirectoryRefId))
            args.Add($"/p:PowerForgeMsiHarvestDirectoryRefId={prepare.HarvestDirectoryRefId}");
        if (!string.IsNullOrWhiteSpace(prepare.HarvestComponentGroupId))
            args.Add($"/p:PowerForgeMsiHarvestComponentGroupId={prepare.HarvestComponentGroupId}");
        if (!string.IsNullOrWhiteSpace(versionResolution.Version) &&
            !string.IsNullOrWhiteSpace(versionResolution.PropertyName) &&
            !installerMsBuildProperties.ContainsKey(versionResolution.PropertyName!))
        {
            args.Add($"/p:PowerForgeMsiVersion={versionResolution.Version}");
            _logger.Info(
                $"MSI version for '{installerId}' resolved to {versionResolution.Version} ({versionResolution.PropertyName}).");
        }
        if (!string.IsNullOrWhiteSpace(licenseResolution.Path) &&
            !string.IsNullOrWhiteSpace(licenseResolution.PropertyName) &&
            !installerMsBuildProperties.ContainsKey(licenseResolution.PropertyName!))
        {
            args.Add($"/p:PowerForgeMsiClientLicensePath={licenseResolution.Path}");
            if (!string.IsNullOrWhiteSpace(licenseResolution.ClientId))
                args.Add($"/p:PowerForgeMsiClientId={licenseResolution.ClientId}");
            _logger.Info(
                $"MSI client license for '{installerId}' resolved to '{licenseResolution.Path}' ({licenseResolution.PropertyName}).");
        }

        _logger.Info(
            $"MSI build starting for '{installerId}' ({target}, {framework}, {runtime}, {style.Value}) -> {Path.GetFileName(installerProjectPath)}");
        RunDotnet(plan.ProjectRoot, args);

        if (versionResolution.Patch.HasValue && !string.IsNullOrWhiteSpace(versionResolution.StatePath))
            WriteMsiVersionState(versionResolution.StatePath!, versionResolution.Patch.Value, versionResolution.Version!);

        var outputs = FindChangedMsiOutputs(projectDir, before);
        if (outputs.Length == 0)
            _logger.Warn($"MSI build for '{installerId}' completed, but no changed *.msi outputs were detected under '{projectDir}'.");
        else
            _logger.Info($"MSI build produced {outputs.Length} MSI output(s) for '{installerId}'.");

        return new DotNetPublishMsiBuildResult
        {
            InstallerId = installerId,
            Target = target,
            Framework = framework,
            Runtime = runtime,
            Style = style.Value,
            ProjectPath = installerProjectPath,
            OutputFiles = outputs,
            Version = versionResolution.Version,
            VersionPropertyName = versionResolution.PropertyName,
            VersionPatch = versionResolution.Patch,
            VersionStatePath = versionResolution.StatePath,
            ClientLicensePath = licenseResolution.Path,
            ClientLicensePropertyName = licenseResolution.PropertyName,
            ClientId = licenseResolution.ClientId
        };
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
        if (!string.IsNullOrWhiteSpace(productVersion))
            definition.Product.Version = productVersion!;
        if (string.IsNullOrWhiteSpace(definition.PayloadComponentGroupId) &&
            !string.IsNullOrWhiteSpace(prepare.HarvestComponentGroupId))
        {
            definition.PayloadComponentGroupId = prepare.HarvestComponentGroupId;
        }

        var generatedDir = ResolveGeneratedInstallerProjectDirectory(plan, installerId, step);
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

    private static string ResolveGeneratedInstallerProjectDirectory(
        DotNetPublishPlan plan,
        string installerId,
        DotNetPublishStep step)
    {
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
            "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/generated",
            tokens);
        var path = ResolvePath(plan.ProjectRoot, relativePath);
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, path, $"Installer '{installerId}' generated WiX project path");
        return path;
    }

    private static void AddGeneratedInstallerDefineConstants(
        PowerForgeWixInstallerCompileRequest request,
        DotNetPublishMsiPrepareResult prepare)
    {
        request.DefineConstants["PayloadDir"] = prepare.StagingDir;
        request.DefineConstants["PowerForgeMsiPayloadDir"] = prepare.StagingDir;
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

    private static Dictionary<string, DateTime> SnapshotMsiOutputs(string root)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateMsiFiles(root))
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

    private static string[] FindChangedMsiOutputs(string root, IReadOnlyDictionary<string, DateTime> before)
    {
        var results = new List<string>();
        foreach (var file in EnumerateMsiFiles(root))
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

    private static IEnumerable<string> EnumerateMsiFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(root, "*.msi", SearchOption.AllDirectories)
                .Where(p =>
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
        DotNetPublishStep step)
    {
        if (installer?.Versioning is null || !installer.Versioning.Enabled)
            return MsiVersionResolution.None();

        var v = installer.Versioning;
        var major = Clamp(v.Major, 0, 255);
        var minor = Clamp(v.Minor, 0, 255);
        var patchCap = Clamp(v.PatchCap, 1, 65535);

        var floorDate = string.IsNullOrWhiteSpace(v.FloorDateUtc)
            ? DateTime.UtcNow.Date
            : ParseUtcDate(v.FloorDateUtc!);

        var basePatch = DaysSince20000101(floorDate);
        var patch = Clamp(basePatch, 0, patchCap);
        var propertyName = string.IsNullOrWhiteSpace(v.PropertyName) ? "ProductVersion" : v.PropertyName!.Trim();
        string? statePath = null;

        if (v.Monotonic)
        {
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["installer"] = installer.Id,
                ["target"] = step.TargetName ?? string.Empty,
                ["rid"] = step.Runtime ?? string.Empty,
                ["framework"] = step.Framework ?? string.Empty,
                ["style"] = step.Style?.ToString() ?? string.Empty,
                ["configuration"] = plan.Configuration ?? "Release"
            };

            var stateTemplate = string.IsNullOrWhiteSpace(v.StatePath)
                ? "Artifacts/DotNetPublish/Msi/{installer}/version.state.json"
                : v.StatePath!;
            statePath = ResolvePath(plan.ProjectRoot, ApplyTemplate(stateTemplate, tokens));

            if (!plan.AllowOutputOutsideProjectRoot)
                EnsurePathWithinRoot(plan.ProjectRoot, statePath, $"Installer '{installer.Id}' version state path");

            var previous = ReadMsiVersionStatePatch(statePath);
            if (previous.HasValue && previous.Value >= patch)
                patch = Math.Min(patchCap, previous.Value + 1);
        }

        if (patch >= patchCap && basePatch > patchCap)
        {
            _logger.Warn(
                $"MSI version patch exceeded cap ({patchCap}) for installer '{installer.Id}'. " +
                $"Using capped patch value {patchCap}.");
        }

        var version = $"{major}.{minor}.{patch}";
        return new MsiVersionResolution(version, propertyName, patch, statePath);
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

    private static int? ReadMsiVersionStatePatch(string statePath)
    {
        try
        {
            if (!File.Exists(statePath)) return null;
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<MsiVersionState>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            return state?.LastPatch;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteMsiVersionState(string statePath, int patch, string version)
    {
        var state = new MsiVersionState
        {
            LastPatch = patch,
            Version = version,
            UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(statePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
    }

    private sealed class MsiVersionResolution
    {
        public string? Version { get; }
        public string? PropertyName { get; }
        public int? Patch { get; }
        public string? StatePath { get; }

        public MsiVersionResolution(string? version, string? propertyName, int? patch, string? statePath)
        {
            Version = version;
            PropertyName = propertyName;
            Patch = patch;
            StatePath = statePath;
        }

        public static MsiVersionResolution None()
            => new(version: null, propertyName: null, patch: null, statePath: null);
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
