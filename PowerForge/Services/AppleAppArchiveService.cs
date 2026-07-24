using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Creates and uploads Apple app archives through xcodebuild.
/// </summary>
public sealed class AppleAppArchiveService
{
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleAppArchiveService"/> class.
    /// </summary>
    /// <param name="processRunner">Process runner used to execute xcodebuild.</param>
    public AppleAppArchiveService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    /// <summary>
    /// Creates an Apple app archive.
    /// </summary>
    /// <param name="request">Archive request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Archive result.</returns>
    public async Task<AppleAppArchiveResult> CreateArchiveAsync(
        AppleAppArchiveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            throw new ArgumentException("ProjectPath is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Scheme))
            throw new ArgumentException("Scheme is required.", nameof(request));

        var projectPath = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(projectPath) && !Directory.Exists(projectPath))
            throw new FileNotFoundException("Xcode project or workspace was not found.", projectPath);

        var archivePath = ResolveArchivePath(request);
        var destination = string.IsNullOrWhiteSpace(request.Destination)
            ? GetGenericDestination(request.Platform, request.ArchiveVariant)
            : request.Destination!.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        var args = new List<string>
        {
            request.IsWorkspace ? "-workspace" : "-project",
            projectPath,
            "-scheme",
            request.Scheme.Trim(),
            "-configuration",
            string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration.Trim(),
            "-destination",
            destination,
            "-archivePath",
            archivePath
        };

        if (request.AllowProvisioningUpdates)
            args.Add("-allowProvisioningUpdates");

        AddAppStoreConnectAuthenticationArguments(
            request.AppStoreConnectApiKeyPath,
            request.AppStoreConnectApiKeyId,
            request.AppStoreConnectApiIssuerId,
            request.AllowProvisioningUpdates,
            args);
        args.Add("archive");
        args.AddRange(request.AdditionalArguments ?? Array.Empty<string>());

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.XcodeBuildExecutable),
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                args,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        return new AppleAppArchiveResult
        {
            ArchivePath = archivePath,
            Destination = destination,
            ProcessResult = result
        };
    }

    /// <summary>
    /// Uploads an Apple app archive to App Store Connect with xcodebuild exportArchive.
    /// </summary>
    /// <param name="request">Upload request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upload result.</returns>
    public async Task<AppleAppArchiveUploadResult> UploadArchiveAsync(
        AppleAppArchiveUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ArchivePath))
            throw new ArgumentException("ArchivePath is required.", nameof(request));

        var archivePath = Path.GetFullPath(request.ArchivePath);
        if (!Directory.Exists(archivePath))
            throw new DirectoryNotFoundException($"Archive path was not found: {archivePath}");

        var exportPath = ResolveExportPath(request);
        var plistPath = ResolveExportOptionsPlistPath(request);
        Directory.CreateDirectory(exportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
        File.WriteAllText(plistPath, BuildExportOptionsPlist(request), Encoding.UTF8);

        var args = new List<string>
        {
            "-exportArchive",
            "-archivePath",
            archivePath,
            "-exportPath",
            exportPath,
            "-exportOptionsPlist",
            plistPath
        };
        if (request.AllowProvisioningUpdates)
            args.Add("-allowProvisioningUpdates");

        AddAppStoreConnectAuthenticationArguments(
            request.AppStoreConnectApiKeyPath,
            request.AppStoreConnectApiKeyId,
            request.AppStoreConnectApiIssuerId,
            request.AllowProvisioningUpdates,
            args);
        args.AddRange(request.AdditionalArguments ?? Array.Empty<string>());

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.XcodeBuildExecutable),
                Path.GetDirectoryName(archivePath) ?? Directory.GetCurrentDirectory(),
                args,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        var diagnostics = ResolveUploadDiagnostics(result);

        return new AppleAppArchiveUploadResult
        {
            ArchivePath = archivePath,
            ExportPath = exportPath,
            ExportOptionsPlistPath = plistPath,
            DistributionLogPath = diagnostics.DistributionLogPath,
            BuildUploadId = diagnostics.BuildUploadId,
            ProcessResult = result
        };
    }

    private static AppleUploadDiagnostics ResolveUploadDiagnostics(ProcessRunResult result)
    {
        var output = string.Join(Environment.NewLine, result.StdOut, result.StdErr);
        var match = Regex.Match(
            output,
            "Created bundle at path \\\"(?<path>[^\\\"]+\\.xcdistributionlogs)\\\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
            return new AppleUploadDiagnostics(null, null);

        var logPath = Path.GetFullPath(match.Groups["path"].Value);
        var contentDeliveryLog = Path.Combine(logPath, "ContentDelivery.log");
        if (!File.Exists(contentDeliveryLog))
            return new AppleUploadDiagnostics(logPath, null);

        var deliveryMatches = Regex.Matches(
            File.ReadAllText(contentDeliveryLog),
            "Delivery UUID:\\s*(?<id>[0-9a-fA-F-]{36})",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var buildUploadId = deliveryMatches.Count == 0
            ? null
            : deliveryMatches[deliveryMatches.Count - 1].Groups["id"].Value;
        return new AppleUploadDiagnostics(logPath, buildUploadId);
    }

    private readonly struct AppleUploadDiagnostics
    {
        internal AppleUploadDiagnostics(string? distributionLogPath, string? buildUploadId)
        {
            DistributionLogPath = distributionLogPath;
            BuildUploadId = buildUploadId;
        }

        internal string? DistributionLogPath { get; }

        internal string? BuildUploadId { get; }
    }

    private static void AddAppStoreConnectAuthenticationArguments(
        string? appStoreConnectApiKeyPath,
        string? appStoreConnectApiKeyId,
        string? appStoreConnectApiIssuerId,
        bool allowProvisioningUpdates,
        List<string> args)
    {
        var keyPath = string.IsNullOrWhiteSpace(appStoreConnectApiKeyPath)
            ? null
            : Path.GetFullPath(appStoreConnectApiKeyPath!);
        var keyId = string.IsNullOrWhiteSpace(appStoreConnectApiKeyId) ? null : appStoreConnectApiKeyId!.Trim();
        var issuerId = string.IsNullOrWhiteSpace(appStoreConnectApiIssuerId) ? null : appStoreConnectApiIssuerId!.Trim();
        var configuredCount = (keyPath is null ? 0 : 1) + (keyId is null ? 0 : 1) + (issuerId is null ? 0 : 1);
        if (configuredCount == 0)
            return;
        if (configuredCount != 3)
            throw new ArgumentException("App Store Connect API-key authentication requires AppStoreConnectApiKeyPath, AppStoreConnectApiKeyId, and AppStoreConnectApiIssuerId.");
        if (!allowProvisioningUpdates)
            throw new ArgumentException("App Store Connect API-key authentication requires AllowProvisioningUpdates=true so xcodebuild can use the credentials.");
        if (!File.Exists(keyPath))
            throw new FileNotFoundException($"App Store Connect API key file was not found: {keyPath}", keyPath);

        args.Add("-authenticationKeyPath");
        args.Add(keyPath!);
        args.Add("-authenticationKeyID");
        args.Add(keyId!);
        args.Add("-authenticationKeyIssuerID");
        args.Add(issuerId!);
    }

    /// <summary>
    /// Resolves the generic xcodebuild destination for an Apple platform.
    /// </summary>
    /// <param name="platform">Apple platform.</param>
    /// <returns>xcodebuild destination string.</returns>
    public static string GetGenericDestination(ApplePlatform platform)
        => GetGenericDestination(platform, AppleArchiveVariant.Default);

    /// <summary>
    /// Resolves the generic xcodebuild destination for an Apple platform and archive variant.
    /// </summary>
    /// <param name="platform">Apple platform.</param>
    /// <param name="archiveVariant">Optional archive destination variant.</param>
    /// <returns>xcodebuild destination string.</returns>
    public static string GetGenericDestination(ApplePlatform platform, AppleArchiveVariant archiveVariant)
    {
        if (archiveVariant == AppleArchiveVariant.MacCatalyst)
        {
            if (platform != ApplePlatform.macOS)
                throw new ArgumentException("MacCatalyst archive targets must use Platform macOS for App Store Connect.", nameof(platform));

            return "generic/platform=macOS,variant=Mac Catalyst";
        }

        if (archiveVariant != AppleArchiveVariant.Default)
            throw new ArgumentOutOfRangeException(nameof(archiveVariant), archiveVariant, "Unsupported Apple archive variant.");

        return platform switch
        {
            ApplePlatform.iOS => "generic/platform=iOS",
            ApplePlatform.iPadOS => "generic/platform=iOS",
            ApplePlatform.macOS => "generic/platform=macOS",
            ApplePlatform.tvOS => "generic/platform=tvOS",
            ApplePlatform.watchOS => "generic/platform=watchOS",
            ApplePlatform.visionOS => "generic/platform=visionOS",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Apple platform.")
        };
    }

    private static string ResolveArchivePath(AppleAppArchiveRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ArchivePath))
            return Path.GetFullPath(request.ArchivePath!);

        var root = string.IsNullOrWhiteSpace(request.ArchiveRoot)
            ? Path.Combine(Path.GetTempPath(), "powerforge-apple-archives")
            : Path.GetFullPath(request.ArchiveRoot!);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        var safeScheme = SanitizePathPart(request.Scheme);
        return Path.Combine(root, $"{safeScheme}-{stamp}-{uniqueSuffix}.xcarchive");
    }

    private static string ResolveExportPath(AppleAppArchiveUploadRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExportPath))
            return Path.GetFullPath(request.ExportPath!);

        return Path.Combine(Path.GetTempPath(), "powerforge-apple-upload", Path.GetFileNameWithoutExtension(request.ArchivePath));
    }

    private static string ResolveExportOptionsPlistPath(AppleAppArchiveUploadRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExportOptionsPlistPath))
            return Path.GetFullPath(request.ExportOptionsPlistPath!);

        return Path.Combine(ResolveExportPath(request), "ExportOptions.plist");
    }

    private static string BuildExportOptionsPlist(AppleAppArchiveUploadRequest request)
    {
        var entries = new List<(string Key, string Type, string Value)>
        {
            ("destination", "string", string.IsNullOrWhiteSpace(request.Destination) ? "upload" : request.Destination.Trim()),
            ("method", "string", string.IsNullOrWhiteSpace(request.Method) ? "app-store-connect" : request.Method.Trim()),
            ("signingStyle", "string", string.IsNullOrWhiteSpace(request.SigningStyle) ? "automatic" : request.SigningStyle.Trim()),
            ("manageAppVersionAndBuildNumber", "bool", request.ManageAppVersionAndBuildNumber ? "true" : "false"),
            ("uploadSymbols", "bool", request.UploadSymbols ? "true" : "false"),
            ("generateAppStoreInformation", "bool", request.GenerateAppStoreInformation ? "true" : "false")
        };

        if (!string.IsNullOrWhiteSpace(request.TeamId))
            entries.Insert(2, ("teamID", "string", request.TeamId!.Trim()));

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">""");
        builder.AppendLine("""<plist version="1.0">""");
        builder.AppendLine("<dict>");
        foreach (var entry in entries)
        {
            builder.Append("  <key>").Append(SecurityElement.Escape(entry.Key)).AppendLine("</key>");
            if (entry.Type == "bool")
                builder.Append("  <").Append(entry.Value).AppendLine("/>");
            else
                builder.Append("  <string>").Append(SecurityElement.Escape(entry.Value)).AppendLine("</string>");
        }
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }

    private static string NormalizeExecutable(string? executable)
    {
        var value = executable?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "xcodebuild" : value!;
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch).ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "AppleApp" : sanitized;
    }
}
