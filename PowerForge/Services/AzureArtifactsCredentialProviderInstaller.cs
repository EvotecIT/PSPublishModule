using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;

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

    private const string PublicNetCorePackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.Net8.NuGet.CredentialProvider.zip";
    private const string PublicNetFxPackageUri = "https://github.com/microsoft/artifacts-credprovider/releases/latest/download/Microsoft.NetFx48.NuGet.CredentialProvider.zip";

    private readonly ILogger _logger;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string> _getUserProfilePath;
    private readonly Func<Uri, string, TimeSpan, string> _downloadPackage;

    /// <summary>
    /// Creates a new installer.
    /// </summary>
    public AzureArtifactsCredentialProviderInstaller(IPowerShellRunner runner, ILogger logger)
        : this(logger, Environment.GetEnvironmentVariable, GetDefaultUserProfilePath, DownloadPackage)
    {
        _ = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    internal AzureArtifactsCredentialProviderInstaller(
        ILogger logger,
        Func<string, string?> getEnvironmentVariable,
        Func<string> getUserProfilePath,
        Func<Uri, string, TimeSpan, string> downloadPackage)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _getEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        _getUserProfilePath = getUserProfilePath ?? throw new ArgumentNullException(nameof(getUserProfilePath));
        _downloadPackage = downloadPackage ?? throw new ArgumentNullException(nameof(downloadPackage));
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
        if (Path.DirectorySeparatorChar != '\\')
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
        var source = ResolvePackageSource(packageKind);
        var packagePath = MaterializePackage(source, packageKind, tempRoot, timeout);
        ValidateSha256(packagePath, ResolveExpectedSha256(packageKind));

        var extractRoot = Path.Combine(tempRoot, packageKind.ToString());
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(packagePath, extractRoot);

        var sourceDirectory = FindCredentialProviderDirectory(extractRoot, packageKind);
        var targetDirectory = GetTargetCredentialProviderDirectory(packageKind);
        if (Directory.Exists(targetDirectory))
        {
            if (!force)
            {
                messages.Add($"Azure Artifacts Credential Provider {packageKind} package is already installed at '{targetDirectory}'. Use the force prerequisite mode to overwrite it.");
                return;
            }

            Directory.Delete(targetDirectory, recursive: true);
        }

        CopyDirectory(sourceDirectory, targetDirectory);
        messages.Add($"Azure Artifacts Credential Provider {packageKind} package installed from {source.Description}.");
    }

    private CredentialProviderPackageSource ResolvePackageSource(CredentialProviderPackageKind packageKind)
    {
        var specific = packageKind == CredentialProviderPackageKind.NetFx
            ? _getEnvironmentVariable(NetFxPackageEnvironmentVariable)
            : _getEnvironmentVariable(NetCorePackageEnvironmentVariable);
        var configured = FirstNonEmpty(specific, _getEnvironmentVariable(PackageEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(configured))
            return CredentialProviderPackageSource.FromConfiguredValue(configured!);

        var publicUri = packageKind == CredentialProviderPackageKind.NetFx
            ? PublicNetFxPackageUri
            : PublicNetCorePackageUri;
        return CredentialProviderPackageSource.FromPublicFallback(publicUri);
    }

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

    private sealed class CredentialProviderPackageSource
    {
        private CredentialProviderPackageSource(string value, bool isLocalPath, string description)
        {
            Value = value;
            IsLocalPath = isLocalPath;
            Description = description;
        }

        internal string Value { get; }

        internal bool IsLocalPath { get; }

        internal string Description { get; }

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

        internal static CredentialProviderPackageSource FromPublicFallback(string uri)
            => new(uri, isLocalPath: false, description: $"public fallback '{uri}'");
    }
}
