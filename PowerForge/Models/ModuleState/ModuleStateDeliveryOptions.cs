using System;

namespace PowerForge;

internal sealed class ModuleStateDeliveryOptions
{
    internal ModuleStateDeliveryOptions(
        string? profileName = null,
        string? repository = null,
        bool installPrerequisites = false,
        bool prerelease = false,
        bool force = false,
        bool allowErrorFindings = false)
    {
        ProfileName = NormalizeOptional(profileName);
        Repository = NormalizeOptional(repository);
        InstallPrerequisites = installPrerequisites;
        Prerelease = prerelease;
        Force = force;
        AllowErrorFindings = allowErrorFindings;
    }

    internal string? ProfileName { get; }

    internal string? Repository { get; }

    internal bool InstallPrerequisites { get; }

    internal bool Prerelease { get; }

    internal bool Force { get; }

    internal bool AllowErrorFindings { get; }

    internal bool HasDeliveryTarget => !string.IsNullOrWhiteSpace(ProfileName) || !string.IsNullOrWhiteSpace(Repository);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
