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
        bool acceptLicense = false,
        bool allowErrorFindings = false,
        bool allowClobber = false,
        string? moduleRoot = null,
        ModuleStateDeliveryTransport transport = ModuleStateDeliveryTransport.PrivateModule)
    {
        ProfileName = NormalizeOptional(profileName);
        Repository = NormalizeOptional(repository);
        InstallPrerequisites = installPrerequisites;
        Prerelease = prerelease;
        Force = force;
        AcceptLicense = acceptLicense;
        AllowErrorFindings = allowErrorFindings;
        AllowClobber = allowClobber;
        ModuleRoot = NormalizeOptional(moduleRoot);
        Transport = transport;
    }

    internal string? ProfileName { get; }

    internal string? Repository { get; }

    internal bool InstallPrerequisites { get; }

    internal bool Prerelease { get; }

    internal bool Force { get; }

    internal bool AcceptLicense { get; }

    internal bool AllowErrorFindings { get; }

    internal bool AllowClobber { get; }

    internal string? ModuleRoot { get; }

    internal ModuleStateDeliveryTransport Transport { get; }

    internal bool HasDeliveryTarget => !string.IsNullOrWhiteSpace(ProfileName) || !string.IsNullOrWhiteSpace(Repository);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
