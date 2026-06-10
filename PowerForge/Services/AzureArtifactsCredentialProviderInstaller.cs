using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Result of attempting to install the Azure Artifacts credential provider.
/// </summary>
public sealed class AzureArtifactsCredentialProviderInstallResult
{
    /// <summary>Whether the installation command completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Whether the installation changed the local machine state.</summary>
    public bool Changed { get; set; }

    /// <summary>Detected credential-provider file paths after installation.</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();

    /// <summary>Detected credential-provider version after installation.</summary>
    public string? Version { get; set; }

    /// <summary>Messages emitted by the installer.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Installs the Azure Artifacts credential provider for the current user.
/// </summary>
public sealed class AzureArtifactsCredentialProviderInstaller
{
    private const string PackageEnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_PACKAGE";
    private const string NetCorePackageEnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_PACKAGE";
    private const string NetFxPackageEnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETFX_PACKAGE";
    private const string Sha256EnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_SHA256";
    private const string NetCoreSha256EnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETCORE_SHA256";
    private const string NetFxSha256EnvironmentVariable = "POWERFORGE_AZURE_ARTIFACTS_CREDENTIAL_PROVIDER_NETFX_SHA256";
    private const string ArtefactsModuleName = "PSPublishModule.Artefacts";
    private const string ArtefactsManifestRelativePath = "Artefacts/AzureArtifactsCredentialProvider/manifest.json";

    private const string PublicNetCorePackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.Net8.NuGet.CredentialProvider.zip";
    private const string PublicWinX64PackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.win-x64.NuGet.CredentialProvider.zip";
    private const string PublicWinArm64PackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.win-arm64.NuGet.CredentialProvider.zip";
    private const string PublicWinX86PackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.win-x86.NuGet.CredentialProvider.zip";
    private const string PublicNetFxPackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.NetFx48.NuGet.CredentialProvider.zip";

    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string> _getUserProfilePath;
    private readonly Func<Uri, string, TimeSpan, string> _downloadPackage;
    private readonly Func<bool> _isWindows;

    /// <summary>
    /// Creates a new installer.
    /// </summary>
    public AzureArtifactsCredentialProviderInstaller(IPowerShellRunner runner, ILogger logger)
        : this(runner, logger, Environment.GetEnvironmentVariable, GetDefaultUserProfilePath, DownloadPackage, IsCurrentPlatformWindows)
    {
    }

    internal AzureArtifactsCredentialProviderInstaller(
        IPowerShellRunner runner,
        ILogger logger,
        Func<string, string?> getEnvironmentVariable,
        Func<string> getUserProfilePath,
        Func<Uri, string, TimeSpan, string> downloadPackage,
        Func<bool>? isWindows = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _getEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        _getUserProfilePath = getUserProfilePath ?? throw new ArgumentNullException(nameof(getUserProfilePath));
        _downloadPackage = downloadPackage ?? throw new ArgumentNullException(nameof(downloadPackage));
        _isWindows = isWindows ?? IsCurrentPlatformWindows;
    }

    /// <summary>
    /// Installs the Azure Artifacts credential provider from a local package, internal mirror, or public fallback package.
    /// </summary>
    public AzureArtifactsCredentialProviderInstallResult InstallForCurrentUser(
        bool includeNetFx = true,
        bool installNet8 = true,
        bool force = false,
        TimeSpan? timeout = null)
    {
        if (!_isWindows())
            throw new InvalidOperationException("Automatic Azure Artifacts Credential Provider installation is currently supported on Windows only.");

        if (!includeNetFx && !installNet8)
            throw new ArgumentException("At least one credential-provider runtime must be requested.", nameof(includeNetFx));

        var messages = new List<string>();
        var before = DetectCredentialProvider().Paths;
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "azartifacts", Guid.NewGuid().ToString("N"));
        var packageTimeout = timeout ?? TimeSpan.FromMinutes(5);

        try
        {
            Directory.CreateDirectory(tempRoot);
            if (installNet8)
                InstallPackage(CredentialProviderPackageKind.NetCore, tempRoot, force, packageTimeout, messages);
            if (includeNetFx)
                InstallPackage(CredentialProviderPackageKind.NetFx, tempRoot, force, packageTimeout, messages);

            var afterDetection = DetectCredentialProvider();
            var changed = !PathsEqual(before, afterDetection.Paths);
            messages.Add(BuildDetectionMessage(afterDetection));

            return new AzureArtifactsCredentialProviderInstallResult
            {
                Succeeded = afterDetection.IsDetected,
                Changed = changed,
                Paths = afterDetection.Paths,
                Version = afterDetection.Version,
                Messages = messages.Where(static message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal).ToArray()
            };
        }
        catch (Exception ex)
        {
            var message = $"Azure Artifacts Credential Provider install failed. {ex.Message}".Trim();
            if (_logger.IsVerbose) _logger.Verbose(message);
            throw new InvalidOperationException(message, ex);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    private void InstallPackage(
        CredentialProviderPackageKind packageKind,
        string tempRoot,
        bool force,
        TimeSpan timeout,
        List<string> messages)
    {
        var source = ResolvePackageSource(packageKind, timeout, messages);
        var packagePath = MaterializePackage(source, packageKind, tempRoot, timeout);
        ValidateSha256(packagePath, FirstNonEmpty(ResolveExpectedSha256(packageKind), source.ExpectedSha256));

        var extractRoot = Path.Combine(tempRoot, packageKind.ToString());
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(packagePath, extractRoot);

        var sourceDirectory = FindCredentialProviderDirectory(extractRoot, packageKind);
        var targetDirectory = GetTargetCredentialProviderDirectory(packageKind);
        if (Directory.Exists(targetDirectory))
        {
            if (!force && IsCredentialProviderRuntimeComplete(targetDirectory, packageKind))
            {
                messages.Add($"Azure Artifacts Credential Provider {packageKind} package is already installed at '{targetDirectory}'. Use the force prerequisite mode to overwrite it.");
                return;
            }

            if (!force)
                messages.Add($"Azure Artifacts Credential Provider {packageKind} target '{targetDirectory}' is incomplete and will be repaired.");

            Directory.Delete(targetDirectory, recursive: true);
        }

        CopyDirectory(sourceDirectory, targetDirectory);
        messages.Add($"Azure Artifacts Credential Provider {packageKind} package installed from {source.Description}.");
    }

    private CredentialProviderPackageSource ResolvePackageSource(
        CredentialProviderPackageKind packageKind,
        TimeSpan timeout,
        List<string> messages)
    {
        var specific = packageKind == CredentialProviderPackageKind.NetFx
            ? _getEnvironmentVariable(NetFxPackageEnvironmentVariable)
            : _getEnvironmentVariable(NetCorePackageEnvironmentVariable);
        var configured = FirstNonEmpty(specific, _getEnvironmentVariable(PackageEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(configured))
            return CredentialProviderPackageSource.FromConfiguredValue(configured!);

        var artefactsSource = TryResolveArtefactsModulePackageSource(packageKind, messages);
        if (artefactsSource is not null)
            return artefactsSource;

        var artefactsInstallResult = TryInstallArtefactsModule(timeout, messages);
        if (artefactsInstallResult.Succeeded)
        {
            artefactsSource = TryResolveArtefactsModulePackageSource(packageKind, messages, artefactsInstallResult.ModulePaths);
            if (artefactsSource is not null)
                return artefactsSource;
        }

        var publicUri = packageKind == CredentialProviderPackageKind.NetFx
            ? PublicNetFxPackageUri
            : GetPublicNetCorePackageUri();
        return CredentialProviderPackageSource.FromPublicFallback(publicUri);
    }

    private static string GetPublicNetCorePackageUri()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => PublicWinArm64PackageUri,
            Architecture.X86 => PublicWinX86PackageUri,
            Architecture.X64 => PublicWinX64PackageUri,
            _ => PublicNetCorePackageUri
        };

    private CredentialProviderPackageSource? TryResolveArtefactsModulePackageSource(
        CredentialProviderPackageKind packageKind,
        List<string> messages,
        IEnumerable<string>? additionalModulePaths = null)
    {
        foreach (var manifestPath in EnumerateArtefactsManifests(additionalModulePaths))
        {
            var source = TryReadArtefactsPackageSource(manifestPath, packageKind);
            if (source is null)
                continue;

            messages.Add($"{ArtefactsModuleName} supplied Azure Artifacts Credential Provider {packageKind} package '{source.Value}'.");
            return source;
        }

        return null;
    }

    private CredentialProviderPackageSource? TryReadArtefactsPackageSource(
        string manifestPath,
        CredentialProviderPackageKind packageKind)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("files", out var files) ||
                files.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var runtime = packageKind == CredentialProviderPackageKind.NetFx ? "netfx" : "netcore";
            foreach (var file in files.EnumerateArray())
            {
                if (!file.TryGetProperty("runtime", out var runtimeProperty) ||
                    !string.Equals(runtimeProperty.GetString(), runtime, StringComparison.OrdinalIgnoreCase) ||
                    !file.TryGetProperty("path", out var pathProperty))
                {
                    continue;
                }

                var relativePath = pathProperty.GetString();
                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                var packagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, relativePath!));
                var sha256 = file.TryGetProperty("sha256", out var shaProperty) ? shaProperty.GetString() : null;
                if (File.Exists(packagePath))
                    return CredentialProviderPackageSource.FromArtefactsModule(packagePath, sha256);
            }
        }
        catch (IOException)
        {
            // Best effort discovery only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort discovery only.
        }
        catch (JsonException)
        {
            // Ignore malformed artefact manifests and continue to other sources.
        }

        return null;
    }

    private IEnumerable<string> EnumerateArtefactsManifests(IEnumerable<string>? additionalModulePaths = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateModulePathRoots(additionalModulePaths))
        {
            if (!seen.Add(root))
                continue;

            var moduleRoot = Path.Combine(root, ArtefactsModuleName);
            if (!Directory.Exists(moduleRoot))
                continue;

            var direct = Path.Combine(moduleRoot, ArtefactsManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(direct))
                yield return direct;

            IEnumerable<string> versionDirectories;
            try
            {
                versionDirectories = Directory.EnumerateDirectories(moduleRoot).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var versionDirectory in OrderArtefactsModuleVersionDirectories(versionDirectories))
            {
                var manifestPath = Path.Combine(versionDirectory, ArtefactsManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(manifestPath))
                    yield return manifestPath;
            }
        }
    }

    private IEnumerable<string> EnumerateModulePathRoots(IEnumerable<string>? additionalModulePaths)
    {
        foreach (var root in SplitModulePath(_getEnvironmentVariable("PSModulePath")))
            yield return root;

        if (additionalModulePaths is null)
            yield break;

        foreach (var modulePath in additionalModulePaths)
        {
            foreach (var root in SplitModulePath(modulePath))
                yield return root;
        }
    }

    private static IEnumerable<string> SplitModulePath(string? modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
            yield break;

        foreach (var root in modulePath!.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = root.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;
        }
    }

    private ArtefactsModuleInstallResult TryInstallArtefactsModule(TimeSpan timeout, List<string> messages)
    {
        var command = @"
$ErrorActionPreference = 'Stop'
function Write-PowerForgeModulePath {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes([string] $env:PSModulePath)
    [Console]::Out.WriteLine('PFARTEFACTS::PSMODULEPATH::' + [Convert]::ToBase64String($bytes))
}
$installPSResource = Get-Command -Name Install-PSResource -ErrorAction SilentlyContinue
if ($installPSResource) {
    try {
        $parameters = @{
            Name = 'PSPublishModule.Artefacts'
            Scope = 'CurrentUser'
            TrustRepository = $true
            ErrorAction = 'Stop'
        }
        if ($installPSResource.Parameters.ContainsKey('Reinstall')) {
            $parameters['Reinstall'] = $true
        }
        Install-PSResource @parameters
        Write-PowerForgeModulePath
        return
    } catch {
        $psResourceError = $_
        if (-not (Get-Command -Name Install-Module -ErrorAction SilentlyContinue)) {
            throw
        }
        Write-Verbose 'Install-PSResource failed for PSPublishModule.Artefacts. Falling back to Install-Module.'
    }
}
if (Get-Command -Name Install-Module -ErrorAction SilentlyContinue) {
    Install-Module -Name 'PSPublishModule.Artefacts' -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
    Write-PowerForgeModulePath
    return
}
throw 'Install-PSResource and Install-Module are not available.'
";

        var result = _runner.Run(PowerShellRunRequest.ForCommand(command, timeout, preferPwsh: true));
        if (result.ExitCode == 0)
        {
            messages.Add($"{ArtefactsModuleName} was installed or refreshed from the configured PowerShell gallery.");
            return new ArtefactsModuleInstallResult(true, ParsePowerShellModulePaths(result.StdOut));
        }

        var failure = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        messages.Add($"{ArtefactsModuleName} could not be installed from the configured PowerShell gallery. {failure}".Trim());
        return new ArtefactsModuleInstallResult(false, Array.Empty<string>());
    }

    private static string[] ParsePowerShellModulePaths(string? stdOut)
    {
        const string marker = "PFARTEFACTS::PSMODULEPATH::";
        if (string.IsNullOrWhiteSpace(stdOut))
            return Array.Empty<string>();

        var paths = new List<string>();
        using var reader = new StringReader(stdOut);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith(marker, StringComparison.Ordinal))
                continue;

            var encoded = line.Substring(marker.Length).Trim();
            if (string.IsNullOrWhiteSpace(encoded))
                continue;

            try
            {
                var modulePath = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                if (!string.IsNullOrWhiteSpace(modulePath))
                    paths.Add(modulePath);
            }
            catch (FormatException)
            {
                // Ignore unrelated or malformed host output.
            }
        }

        return paths.ToArray();
    }

    private static IEnumerable<string> OrderArtefactsModuleVersionDirectories(IEnumerable<string> versionDirectories)
        => versionDirectories
            .OrderByDescending(
                static directory => ArtefactsModuleVersionKey.Parse(Path.GetFileName(directory) ?? string.Empty),
                ArtefactsModuleVersionKeyComparer.Instance)
            .ThenByDescending(static directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase);

    private string? ResolveExpectedSha256(CredentialProviderPackageKind packageKind)
    {
        var specific = packageKind == CredentialProviderPackageKind.NetFx
            ? _getEnvironmentVariable(NetFxSha256EnvironmentVariable)
            : _getEnvironmentVariable(NetCoreSha256EnvironmentVariable);
        return FirstNonEmpty(specific, _getEnvironmentVariable(Sha256EnvironmentVariable));
    }

    private string MaterializePackage(
        CredentialProviderPackageSource source,
        CredentialProviderPackageKind packageKind,
        string tempRoot,
        TimeSpan timeout)
    {
        if (source.IsLocalPath)
        {
            if (!File.Exists(source.Value))
                throw new FileNotFoundException($"Configured credential-provider package was not found: {source.Value}", source.Value);

            return source.Value;
        }

        if (!Uri.TryCreate(source.Value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Configured credential-provider package source is not a valid local path or absolute URI: {source.Value}");

        var packagePath = Path.Combine(tempRoot, $"{packageKind}.zip");
        return _downloadPackage(uri, packagePath, timeout);
    }

    private void ValidateSha256(string packagePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return;

        var expected = NormalizeSha256(expectedSha256!);
        using var stream = File.OpenRead(packagePath);
        using var sha256 = SHA256.Create();
        var actual = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Credential-provider package SHA256 mismatch for '{packagePath}'. Expected {expected}, actual {actual}.");
    }

    private string FindCredentialProviderDirectory(string extractRoot, CredentialProviderPackageKind packageKind)
    {
        var expectedSegment = packageKind == CredentialProviderPackageKind.NetFx
            ? Path.Combine("plugins", "netfx", "CredentialProvider.Microsoft")
            : Path.Combine("plugins", "netcore", "CredentialProvider.Microsoft");
        var direct = Path.Combine(extractRoot, expectedSegment);
        if (Directory.Exists(direct))
            return direct;

        var providerFile = Directory.EnumerateFiles(extractRoot, "CredentialProvider.Microsoft.*", SearchOption.AllDirectories)
            .FirstOrDefault(path => IsExpectedProviderFile(path, packageKind));
        if (providerFile is null)
            throw new InvalidOperationException($"Credential-provider package did not contain the expected {packageKind} provider files.");

        return Directory.GetParent(providerFile)!.FullName;
    }

    private string GetTargetCredentialProviderDirectory(CredentialProviderPackageKind packageKind)
    {
        var userProfile = _getUserProfilePath();
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("Unable to resolve the current user's profile path for NuGet plugin installation.");

        return Path.Combine(
            userProfile,
            ".nuget",
            "plugins",
            packageKind == CredentialProviderPackageKind.NetFx ? "netfx" : "netcore",
            "CredentialProvider.Microsoft");
    }

    private static bool IsExpectedProviderFile(string path, CredentialProviderPackageKind packageKind)
    {
        var fileName = Path.GetFileName(path);
        return packageKind == CredentialProviderPackageKind.NetFx
            ? string.Equals(fileName, "CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase)
            : string.Equals(fileName, "CredentialProvider.Microsoft.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCredentialProviderRuntimeComplete(string targetDirectory, CredentialProviderPackageKind packageKind)
    {
        var providerFile = packageKind == CredentialProviderPackageKind.NetFx
            ? "CredentialProvider.Microsoft.exe"
            : "CredentialProvider.Microsoft.dll";
        return File.Exists(Path.Combine(targetDirectory, providerFile));
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = file.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var target = Path.Combine(targetDirectory, relative);
            var targetParent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetParent))
                Directory.CreateDirectory(targetParent);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string DownloadPackage(Uri uri, string packagePath, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = timeout };
        using var response = http.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var output = File.Create(packagePath);
        input.CopyTo(output);
        return packagePath;
    }

    private static string GetDefaultUserProfilePath()
    {
        var specialFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(specialFolder))
            return specialFolder;

        return Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;
    }

    private static bool IsCurrentPlatformWindows()
        => Path.DirectorySeparatorChar == '\\';

    private AzureArtifactsCredentialProviderDetectionResult DetectCredentialProvider()
        => AzureArtifactsCredentialProviderLocator.Detect(
            _getEnvironmentVariable,
            folder => folder == Environment.SpecialFolder.UserProfile
                ? _getUserProfilePath()
                : Environment.GetFolderPath(folder));

    private static bool PathsEqual(string[] left, string[] right)
        => string.Equals(
            string.Join(";", left.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)),
            string.Join(";", right.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)),
            StringComparison.OrdinalIgnoreCase);

    private static string BuildDetectionMessage(AzureArtifactsCredentialProviderDetectionResult detection)
    {
        if (!detection.IsDetected)
            return "Azure Artifacts Credential Provider installation completed, but no provider files were detected afterwards.";

        return string.IsNullOrWhiteSpace(detection.Version)
            ? $"Azure Artifacts Credential Provider detected at {detection.Paths.Length} path(s)."
            : $"Azure Artifacts Credential Provider detected at {detection.Paths.Length} path(s), version {detection.Version}.";
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static string NormalizeSha256(string value)
        => value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value.Substring("sha256:".Length).Trim()
            : value.Trim();

    private enum CredentialProviderPackageKind
    {
        NetCore,
        NetFx
    }

    private sealed class ArtefactsModuleInstallResult
    {
        public ArtefactsModuleInstallResult(bool succeeded, string[] modulePaths)
        {
            Succeeded = succeeded;
            ModulePaths = modulePaths ?? Array.Empty<string>();
        }

        public bool Succeeded { get; }

        public string[] ModulePaths { get; }
    }

    private sealed class CredentialProviderPackageSource
    {
        private CredentialProviderPackageSource(string value, bool isLocalPath, string description, string? expectedSha256 = null)
        {
            Value = value;
            IsLocalPath = isLocalPath;
            Description = description;
            ExpectedSha256 = expectedSha256;
        }

        internal string Value { get; }

        internal bool IsLocalPath { get; }

        internal string Description { get; }

        internal string? ExpectedSha256 { get; }

        internal static CredentialProviderPackageSource FromConfiguredValue(string value)
        {
            var trimmed = value.Trim();
            var isUri = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            return new CredentialProviderPackageSource(
                trimmed,
                !isUri,
                isUri ? $"configured URI '{trimmed}'" : $"configured local package '{trimmed}'");
        }

        internal static CredentialProviderPackageSource FromArtefactsModule(string packagePath, string? expectedSha256)
            => new(packagePath, isLocalPath: true, description: $"{ArtefactsModuleName} package '{packagePath}'", expectedSha256);

        internal static CredentialProviderPackageSource FromPublicFallback(string uri)
            => new(uri, isLocalPath: false, description: $"public fallback '{uri}'");
    }

    private readonly struct ArtefactsModuleVersionKey
    {
        private ArtefactsModuleVersionKey(string original, bool parsed, int[] parts, string[] preRelease)
        {
            Original = original;
            Parsed = parsed;
            Parts = parts;
            PreRelease = preRelease;
        }

        internal string Original { get; }

        internal bool Parsed { get; }

        internal int[] Parts { get; }

        internal string[] PreRelease { get; }

        internal static ArtefactsModuleVersionKey Parse(string value)
        {
            var original = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(original))
                return new ArtefactsModuleVersionKey(original, parsed: false, Array.Empty<int>(), Array.Empty<string>());

            var versionParts = original.Split(new[] { '-' }, 2);
            var mainParts = versionParts[0].Split('.');
            if (mainParts.Length is < 2 or > 4)
                return new ArtefactsModuleVersionKey(original, parsed: false, Array.Empty<int>(), Array.Empty<string>());

            var parsedParts = new int[4];
            for (var i = 0; i < mainParts.Length; i++)
            {
                if (!int.TryParse(mainParts[i], out var part) || part < 0)
                    return new ArtefactsModuleVersionKey(original, parsed: false, Array.Empty<int>(), Array.Empty<string>());

                parsedParts[i] = part;
            }

            var preRelease = versionParts.Length == 2 && !string.IsNullOrWhiteSpace(versionParts[1])
                ? versionParts[1].Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();

            return new ArtefactsModuleVersionKey(original, parsed: true, parsedParts, preRelease);
        }
    }

    private sealed class ArtefactsModuleVersionKeyComparer : IComparer<ArtefactsModuleVersionKey>
    {
        internal static readonly ArtefactsModuleVersionKeyComparer Instance = new();

        public int Compare(ArtefactsModuleVersionKey left, ArtefactsModuleVersionKey right)
        {
            if (left.Parsed && !right.Parsed)
                return 1;
            if (!left.Parsed && right.Parsed)
                return -1;
            if (!left.Parsed && !right.Parsed)
                return StringComparer.OrdinalIgnoreCase.Compare(left.Original, right.Original);

            for (var i = 0; i < 4; i++)
            {
                var partCompare = left.Parts[i].CompareTo(right.Parts[i]);
                if (partCompare != 0)
                    return partCompare;
            }

            return ComparePreRelease(left.PreRelease, right.PreRelease);
        }

        private static int ComparePreRelease(string[] left, string[] right)
        {
            if (left.Length == 0 && right.Length == 0)
                return 0;
            if (left.Length == 0)
                return 1;
            if (right.Length == 0)
                return -1;

            var count = Math.Max(left.Length, right.Length);
            for (var i = 0; i < count; i++)
            {
                if (i >= left.Length)
                    return -1;
                if (i >= right.Length)
                    return 1;

                var leftIsNumber = int.TryParse(left[i], out var leftNumber);
                var rightIsNumber = int.TryParse(right[i], out var rightNumber);
                if (leftIsNumber && rightIsNumber)
                {
                    var numberCompare = leftNumber.CompareTo(rightNumber);
                    if (numberCompare != 0)
                        return numberCompare;
                    continue;
                }

                if (leftIsNumber)
                    return -1;
                if (rightIsNumber)
                    return 1;

                var textCompare = StringComparer.OrdinalIgnoreCase.Compare(left[i], right[i]);
                if (textCompare != 0)
                    return textCompare;
            }

            return 0;
        }
    }
}
