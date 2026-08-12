using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class AppleAppArchiveService
{
    private static readonly Regex PrivacyUsageDescriptionKey = new(
        "^NS[A-Za-z0-9]+UsageDescription$",
        RegexOptions.CultureInvariant);

    private async Task ValidatePrivacyUsageDescriptionsAsync(
        AppleAppArchiveUploadRequest request,
        string archivePath,
        CancellationToken cancellationToken)
    {
        var requiredKeys = (request.RequiredPrivacyUsageDescriptionKeys ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requiredKeys.Length == 0)
            return;
        var invalidKey = requiredKeys.FirstOrDefault(key => !PrivacyUsageDescriptionKey.IsMatch(key));
        if (invalidKey is not null)
            throw new ArgumentException($"Privacy usage-description key '{invalidKey}' is invalid.", nameof(request));

        var applicationsPath = Path.Combine(archivePath, "Products", "Applications");
        var appPaths = Directory.Exists(applicationsPath)
            ? Directory.GetDirectories(applicationsPath, "*.app", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        if (appPaths.Length == 0)
            throw new InvalidOperationException($"Apple archive contains no primary app under '{applicationsPath}'.");

        var expectedBundleId = request.BundleId?.Trim();
        string? selectedInfoPlist = null;
        foreach (var appPath in appPaths)
        {
            var infoPlists = new[]
            {
                Path.Combine(appPath, "Info.plist"),
                Path.Combine(appPath, "Contents", "Info.plist")
            }.Where(File.Exists);
            foreach (var infoPlist in infoPlists)
            {
                if (string.IsNullOrWhiteSpace(expectedBundleId))
                {
                    if (appPaths.Length == 1)
                        selectedInfoPlist = infoPlist;
                    break;
                }

                var archivedBundleId = await ReadPlistStringAsync(
                    infoPlist,
                    "CFBundleIdentifier",
                    request.RequireTrustedSystemTools,
                    cancellationToken).ConfigureAwait(false);
                if (string.Equals(archivedBundleId, expectedBundleId, StringComparison.Ordinal))
                {
                    selectedInfoPlist = infoPlist;
                    break;
                }
            }
            if (selectedInfoPlist is not null)
                break;
        }

        if (selectedInfoPlist is null)
        {
            var identity = string.IsNullOrWhiteSpace(expectedBundleId) ? "the configured target" : $"bundle '{expectedBundleId}'";
            throw new InvalidOperationException($"Apple archive contains no primary app matching {identity}.");
        }

        foreach (var key in requiredKeys)
        {
            var value = await ReadPlistStringAsync(
                selectedInfoPlist,
                key,
                request.RequireTrustedSystemTools,
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Archived app '{Path.GetFileName(Path.GetDirectoryName(selectedInfoPlist))}' is missing non-empty privacy purpose string '{key}'. Upload was blocked before App Store Connect delivery.");
            }
        }
    }

    private async Task<string?> ReadPlistStringAsync(
        string infoPlist,
        string key,
        bool requireTrustedSystemTools,
        CancellationToken cancellationToken)
    {
        var executable = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX)
            ? "/usr/bin/plutil"
            : "plutil";
        var toolEnvironment = requireTrustedSystemTools
            ? AppleTrustedExecutionEnvironment.Create()
            : null;
        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                executable,
                Path.GetDirectoryName(infoPlist)!,
                new[] { "-extract", key, "raw", "-o", "-", infoPlist },
                TimeSpan.FromSeconds(30),
                toolEnvironment,
                captureOutput: true,
                captureError: true,
                inheritEnvironment: toolEnvironment is null),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.StdOut.Trim() : null;
    }
}
