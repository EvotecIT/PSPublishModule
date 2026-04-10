using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal static class WingetManifestWriter
{
    public static string Build(
        PowerForgeReleaseWingetOptions winget,
        PowerForgeReleaseWingetPackage package,
        string packageVersion,
        IReadOnlyList<WingetManifestInstallerEntry> installers)
    {
        if (installers is null || installers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Winget package '{package.PackageIdentifier}' does not define any installers, so a singleton manifest cannot be generated.");
        }

        var writer = new YamlTextWriter();
        var packageLocale = string.IsNullOrWhiteSpace(package.PackageLocale) ? (winget.PackageLocale ?? "en-US") : package.PackageLocale!;
        var manifestVersion = string.IsNullOrWhiteSpace(package.ManifestVersion) ? (winget.ManifestVersion ?? "1.12.0") : package.ManifestVersion!;
        writer.WriteScalar("PackageIdentifier", package.PackageIdentifier);
        writer.WriteScalar("PackageVersion", packageVersion);
        writer.WriteScalar("PackageLocale", packageLocale);
        writer.WriteScalar("Publisher", package.Publisher);
        writer.WriteOptionalScalar("PublisherUrl", package.PublisherUrl);
        writer.WriteScalar("PackageName", package.PackageName);
        writer.WriteOptionalScalar("PackageUrl", package.PackageUrl);
        writer.WriteScalar("License", package.License);
        writer.WriteOptionalScalar("LicenseUrl", package.LicenseUrl);
        writer.WriteScalar("ShortDescription", package.ShortDescription);
        writer.WriteOptionalScalar("Moniker", package.Moniker);
        writer.WriteSequence("Tags", package.Tags);
        writer.WriteSequence("Platform", package.Platform);
        writer.WriteOptionalScalar("MinimumOSVersion", package.MinimumOSVersion);

        var installerType = installers[0].InstallerType;
        var distinctInstallerTypes = installers
            .Select(entry => entry.InstallerType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctInstallerTypes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Winget package '{package.PackageIdentifier}' resolved mixed InstallerType values ({string.Join(", ", distinctInstallerTypes)}), which is not supported in singleton manifests.");
        }

        writer.WriteScalar("InstallerType", installerType);
        writer.WriteKey("Installers");
        foreach (var installer in installers)
        {
            writer.WriteSequenceItem("Architecture", installer.Architecture);
            using (writer.Indent())
            {
                writer.WriteScalar("InstallerUrl", installer.InstallerUrl);
                writer.WriteScalar("InstallerSha256", installer.InstallerSha256);
                writer.WriteOptionalScalar("NestedInstallerType", installer.NestedInstallerType);
                if (!string.IsNullOrWhiteSpace(installer.RelativeFilePath))
                {
                    writer.WriteKey("NestedInstallerFiles");
                    writer.WriteSequenceItem("RelativeFilePath", installer.RelativeFilePath!);
                }
            }
        }

        writer.WriteScalar("ManifestType", "singleton");
        writer.WriteScalar("ManifestVersion", manifestVersion);
        return writer.ToString();
    }
}

internal sealed class WingetManifestInstallerEntry
{
    public PowerForgeReleaseAssetEntry Asset { get; set; } = new();

    public string Architecture { get; set; } = string.Empty;

    public string InstallerType { get; set; } = "zip";

    public string? NestedInstallerType { get; set; }

    public string? RelativeFilePath { get; set; }

    public string InstallerUrl { get; set; } = string.Empty;

    public string InstallerSha256 { get; set; } = string.Empty;
}
