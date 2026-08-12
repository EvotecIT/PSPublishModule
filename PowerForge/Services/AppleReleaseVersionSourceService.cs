using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads and compare-and-write updates the shared version values in an XcodeGen project specification.
/// </summary>
internal sealed class AppleReleaseVersionSourceService
{
    private static readonly Regex MarketingVersionPattern = CreateSettingPattern("MARKETING_VERSION");
    private static readonly Regex BuildNumberPattern = CreateSettingPattern("CURRENT_PROJECT_VERSION");
    private static readonly Regex MarketingVersionValuePattern = new(
        "^\\d+\\.\\d+(?:\\.\\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly Action<string>? _onComparedVersionSource;
    private readonly Action<string> _deleteFile;

    internal AppleReleaseVersionSourceService(
        Action<string>? onComparedVersionSource = null,
        Action<string>? deleteFile = null)
    {
        _onComparedVersionSource = onComparedVersionSource;
        _deleteFile = deleteFile ?? File.Delete;
    }

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
            WriteIfUnchanged(fullPath, approvedContent, updated);
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

    private void WriteIfUnchanged(string path, string expectedContent, string content)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.previous");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       options: FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(path));
#endif

            if (!string.Equals(File.ReadAllText(path), expectedContent, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Apple version source changed after plan approval: {path}");
            }

            _onComparedVersionSource?.Invoke(path);

            File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            var replacedContent = File.ReadAllText(backupPath);
            if (!string.Equals(replacedContent, expectedContent, StringComparison.Ordinal))
            {
                RestoreConcurrentVersionSource(path, backupPath, content);
                throw new InvalidOperationException(
                    $"Apple version source changed while applying the approved update and was restored instead of being overwritten: {path}");
            }
            TryDeleteCommittedBackup(backupPath);

            if (!string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                throw new InvalidOperationException($"Apple version source changed while the approved update was being published: {path}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (File.Exists(backupPath) && string.Equals(File.ReadAllText(backupPath), expectedContent, StringComparison.Ordinal))
                TryDeleteCommittedBackup(backupPath);
        }
    }

    private void TryDeleteCommittedBackup(string backupPath)
    {
        try { _deleteFile(backupPath); }
        catch { /* approved replacement is already committed; retain the old bytes */ }
    }

    private static void RestoreConcurrentVersionSource(string path, string candidatePath, string expectedNamedContent)
    {
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        while (true)
        {
            var installedCandidateContent = File.ReadAllText(candidatePath);
            var displacedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.displaced");
            File.Replace(candidatePath, path, displacedPath, ignoreMetadataErrors: true);
            var displacedContent = File.ReadAllText(displacedPath);
            if (string.Equals(displacedContent, expectedNamedContent, StringComparison.Ordinal))
            {
                File.Delete(displacedPath);
                return;
            }

            // A newer pathname replacement won while the prior concurrent bytes
            // were being restored. Promote those newer bytes on the next atomic
            // exchange instead of silently overwriting or deleting them.
            candidatePath = displacedPath;
            expectedNamedContent = installedCandidateContent;
        }
    }
}
