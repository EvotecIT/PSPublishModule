using System;
using System.Linq;

namespace PowerForge;

internal static class PrivateGalleryVersionPolicy
{
    internal static PrivateGalleryBootstrapMode GetRecommendedBootstrapMode(BootstrapPrerequisiteStatus status)
        => IsExistingSessionBootstrapReady(status)
            ? PrivateGalleryBootstrapMode.ExistingSession
            : IsCredentialPromptBootstrapReady(status)
                ? PrivateGalleryBootstrapMode.CredentialPrompt
                : PrivateGalleryBootstrapMode.Auto;

    internal static string BuildBootstrapUnavailableMessage(string repositoryName, BootstrapPrerequisiteStatus status)
    {
        var message = $"No supported private-gallery bootstrap path is ready for repository '{repositoryName}'.";
        var reasons = status.ReadinessMessages
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (reasons.Length > 0)
            message += " " + string.Join(" ", reasons);

        message += " Install prerequisites with -InstallPrerequisites or ensure PowerShellGet/PSResourceGet availability before retrying.";
        return message;
    }

    internal static bool IsExistingSessionBootstrapReady(BootstrapPrerequisiteStatus status)
        => status.PSResourceGetSupportsExistingSessionBootstrap && status.CredentialProviderDetection.IsDetected;

    internal static bool IsCredentialPromptBootstrapReady(BootstrapPrerequisiteStatus status)
        => (status.PSResourceGetAvailable && status.PSResourceGetMeetsMinimumVersion) || status.PowerShellGetAvailable;

    internal static string SelectAccessProbeTool(ModuleRepositoryRegistrationResult registration, RepositoryCredential? credential)
    {
        if (credential is null)
        {
            if (registration.InstallPSResourceReady)
                return "PSResourceGet";
            if (registration.InstallModuleReady)
                return "PowerShellGet";

            throw new InvalidOperationException(
                $"Repository '{registration.RepositoryName}' does not currently have a native authenticated access path. {registration.RecommendedBootstrapCommand}".Trim());
        }

        if (registration.PSResourceGetRegistered)
            return "PSResourceGet";
        if (registration.PowerShellGetRegistered)
            return "PowerShellGet";

        throw new InvalidOperationException(
            $"Repository '{registration.RepositoryName}' is not registered for PSResourceGet or PowerShellGet.");
    }

    internal static bool VersionMeetsMinimum(string? versionText, string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(versionText) || string.IsNullOrWhiteSpace(minimumVersion))
            return false;

        return TryParseVersionStamp(versionText, out var version) &&
               TryParseVersionStamp(minimumVersion, out var minimum) &&
               CompareVersionStamps(version, minimum) >= 0;
    }

    private static bool TryParseVersionStamp(string? versionText, out (Version Version, string[] PreRelease) version)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            version = (new Version(0, 0), Array.Empty<string>());
            return false;
        }

        var raw = versionText!.Trim();
        var plusIndex = raw.IndexOf('+');
        if (plusIndex >= 0)
            raw = raw.Substring(0, plusIndex);

        string[] preRelease = Array.Empty<string>();
        var dashIndex = raw.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = raw.Substring(dashIndex + 1)
                .Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            raw = raw.Substring(0, dashIndex);
        }

        if (Version.TryParse(raw, out var parsed) && parsed is not null)
        {
            version = (parsed, preRelease);
            return true;
        }

        version = (new Version(0, 0), Array.Empty<string>());
        return false;
    }

    private static int CompareVersionStamps((Version Version, string[] PreRelease) left, (Version Version, string[] PreRelease) right)
    {
        var versionCompare = left.Version.CompareTo(right.Version);
        if (versionCompare != 0)
            return versionCompare;

        var leftHasPreRelease = left.PreRelease.Length > 0;
        var rightHasPreRelease = right.PreRelease.Length > 0;
        if (!leftHasPreRelease && !rightHasPreRelease)
            return 0;
        if (!leftHasPreRelease)
            return 1;
        if (!rightHasPreRelease)
            return -1;

        var count = Math.Max(left.PreRelease.Length, right.PreRelease.Length);
        for (var index = 0; index < count; index++)
        {
            if (index >= left.PreRelease.Length)
                return -1;
            if (index >= right.PreRelease.Length)
                return 1;

            var segmentCompare = ComparePreReleaseSegment(left.PreRelease[index], right.PreRelease[index]);
            if (segmentCompare != 0)
                return segmentCompare;
        }

        return 0;
    }

    private static int ComparePreReleaseSegment(string left, string right)
    {
        if (TrySplitAlphaNumeric(left, out var leftPrefix, out var leftNumber) &&
            TrySplitAlphaNumeric(right, out var rightPrefix, out var rightNumber))
        {
            var prefixCompare = string.Compare(leftPrefix, rightPrefix, StringComparison.OrdinalIgnoreCase);
            if (prefixCompare != 0)
                return prefixCompare;

            return leftNumber.CompareTo(rightNumber);
        }

        var leftIsNumeric = int.TryParse(left, out var leftNumeric);
        var rightIsNumeric = int.TryParse(right, out var rightNumeric);
        if (leftIsNumeric && rightIsNumeric)
            return leftNumeric.CompareTo(rightNumeric);
        if (leftIsNumeric)
            return -1;
        if (rightIsNumeric)
            return 1;

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySplitAlphaNumeric(string value, out string prefix, out int number)
    {
        prefix = string.Empty;
        number = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var index = value.Length;
        while (index > 0 && char.IsDigit(value[index - 1]))
            index--;

        if (index <= 0 || index >= value.Length)
            return false;

        prefix = value.Substring(0, index);
        return int.TryParse(value.Substring(index), out number);
    }
}
