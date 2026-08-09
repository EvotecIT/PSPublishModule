using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads and atomically updates the shared version values in an XcodeGen project specification.
/// </summary>
internal sealed class AppleReleaseVersionSourceService
{
    private static readonly Regex MarketingVersionPattern = CreateSettingPattern("MARKETING_VERSION");
    private static readonly Regex BuildNumberPattern = CreateSettingPattern("CURRENT_PROJECT_VERSION");
    private static readonly Regex MarketingVersionValuePattern = new(
        "^\\d+\\.\\d+(?:\\.\\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal PowerForgeAppleVersionReceipt Read(string sourcePath)
    {
        var fullPath = ResolveSourcePath(sourcePath);
        return Read(fullPath, File.ReadAllText(fullPath));
    }

    internal PowerForgeAppleVersionReceipt Read(string sourcePath, string content)
    {
        var fullPath = ResolveSourcePath(sourcePath);
        return new PowerForgeAppleVersionReceipt
        {
            SourcePath = fullPath,
            MarketingVersion = ReadSingleValue(content, MarketingVersionPattern, "MARKETING_VERSION", fullPath),
            BuildNumber = ReadSingleValue(content, BuildNumberPattern, "CURRENT_PROJECT_VERSION", fullPath)
        };
    }

    internal PowerForgeAppleVersionReceipt Update(
        string sourcePath,
        string approvedContent,
        string marketingVersion,
        string buildNumber,
        long highestRemoteBuildNumber,
        bool whatIf)
    {
        if (string.IsNullOrWhiteSpace(marketingVersion) ||
            !MarketingVersionValuePattern.IsMatch(marketingVersion.Trim()))
        {
            throw new ArgumentException("Apple marketing version must use x.y or x.y.z.", nameof(marketingVersion));
        }
        if (!long.TryParse(buildNumber, out var parsedBuildNumber) || parsedBuildNumber <= 0)
            throw new ArgumentException("Apple build number must be a positive integer.", nameof(buildNumber));

        var fullPath = ResolveSourcePath(sourcePath);
        var content = approvedContent ?? throw new ArgumentNullException(nameof(approvedContent));
        var previousMarketingVersion = ReadSingleValue(content, MarketingVersionPattern, "MARKETING_VERSION", fullPath);
        var previousBuildNumber = ReadSingleValue(content, BuildNumberPattern, "CURRENT_PROJECT_VERSION", fullPath);
        var updated = ReplaceSingleValue(content, MarketingVersionPattern, marketingVersion.Trim());
        updated = ReplaceSingleValue(updated, BuildNumberPattern, buildNumber.Trim());
        var changed = !string.Equals(content, updated, StringComparison.Ordinal);

        if (changed && !whatIf)
        {
            var currentContent = File.ReadAllText(fullPath);
            if (!string.Equals(currentContent, approvedContent, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Apple version source changed after plan approval: {fullPath}");
            }
            WriteAtomic(fullPath, updated);
        }

        return new PowerForgeAppleVersionReceipt
        {
            SourcePath = fullPath,
            MarketingVersion = marketingVersion.Trim(),
            BuildNumber = buildNumber.Trim(),
            PreviousMarketingVersion = previousMarketingVersion,
            PreviousBuildNumber = previousBuildNumber,
            HighestRemoteBuildNumber = highestRemoteBuildNumber,
            Changed = changed
        };
    }

    private static Regex CreateSettingPattern(string settingName)
        => new(
            "(?m)^(?<prefix>\\s*" + Regex.Escape(settingName) + "\\s*:\\s*)(?<quote>[\\\"']?)(?<value>[^\\\"'\\r\\n#]+?)(?:\\k<quote>)(?<suffix>\\s*(?:#.*)?)$",
            RegexOptions.Compiled);

    private static string ReadSingleValue(string content, Regex pattern, string settingName, string sourcePath)
    {
        var matches = pattern.Matches(content);
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected exactly one {settingName} setting in '{sourcePath}', found {matches.Count}.");
        return matches[0].Groups["value"].Value.Trim();
    }

    private static string ReplaceSingleValue(string content, Regex pattern, string value)
    {
        var matches = pattern.Matches(content);
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected exactly one match while updating Apple version source, found {matches.Count}.");

        return pattern.Replace(
            content,
            match => match.Groups["prefix"].Value + "\"" + value + "\"" + match.Groups["suffix"].Value,
            count: 1);
    }

    private static string ResolveSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Apple version source path is required.", nameof(sourcePath));
        var fullPath = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Apple version source was not found: {fullPath}", fullPath);
        return fullPath;
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
