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
                $"Winget package '{package.PackageIdentifier}' does not define any installers.");
        }

        var writer = new YamlTextWriter();
        var manifestVersion = ResolveManifestVersion(winget, package);
        writer.WriteScalar("PackageIdentifier", package.PackageIdentifier);
        writer.WriteScalar("PackageVersion", packageVersion);

        var firstInstaller = installers.FirstOrDefault();
        if (firstInstaller is null)
        {
            throw new InvalidOperationException(
                $"Winget package '{package.PackageIdentifier}' does not define any installers.");
        }

        var installerType = NormalizeInstallerType(firstInstaller.InstallerType);
        var distinctInstallerTypes = installers
            .Select(static entry => NormalizeInstallerType(entry?.InstallerType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctInstallerTypes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Winget package '{package.PackageIdentifier}' resolved mixed InstallerType values ({string.Join(", ", distinctInstallerTypes)}).");
        }

        writer.WriteScalar("InstallerType", installerType);
        writer.WriteSequence("Platform", package.Platform);
        writer.WriteOptionalScalar("MinimumOSVersion", package.MinimumOSVersion);
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

        writer.WriteScalar("ManifestType", "installer");
        writer.WriteScalar("ManifestVersion", manifestVersion);
        return AddSchemaHeader(writer.ToString(), "installer", manifestVersion);
    }

    public static string BuildVersion(PowerForgeReleaseWingetOptions winget, PowerForgeReleaseWingetPackage package, string packageVersion)
    {
        var writer = new YamlTextWriter();
        var manifestVersion = ResolveManifestVersion(winget, package);
        writer.WriteScalar("PackageIdentifier", package.PackageIdentifier);
        writer.WriteScalar("PackageVersion", packageVersion);
        writer.WriteScalar("DefaultLocale", string.IsNullOrWhiteSpace(package.PackageLocale) ? (winget.PackageLocale ?? "en-US") : package.PackageLocale!);
        writer.WriteScalar("ManifestType", "version");
        writer.WriteScalar("ManifestVersion", manifestVersion);
        return AddSchemaHeader(writer.ToString(), "version", manifestVersion);
    }

    public static string BuildDefaultLocale(PowerForgeReleaseWingetOptions winget, PowerForgeReleaseWingetPackage package, string packageVersion)
    {
        var writer = new YamlTextWriter();
        var manifestVersion = ResolveManifestVersion(winget, package);
        writer.WriteScalar("PackageIdentifier", package.PackageIdentifier);
        writer.WriteScalar("PackageVersion", packageVersion);
        writer.WriteScalar("PackageLocale", string.IsNullOrWhiteSpace(package.PackageLocale) ? (winget.PackageLocale ?? "en-US") : package.PackageLocale!);
        writer.WriteScalar("Publisher", package.Publisher);
        writer.WriteOptionalScalar("PublisherUrl", package.PublisherUrl);
        writer.WriteScalar("PackageName", package.PackageName);
        writer.WriteOptionalScalar("PackageUrl", package.PackageUrl);
        writer.WriteScalar("License", package.License);
        writer.WriteOptionalScalar("LicenseUrl", package.LicenseUrl);
        writer.WriteScalar("ShortDescription", package.ShortDescription);
        writer.WriteOptionalScalar("Moniker", package.Moniker);
        writer.WriteSequence("Tags", package.Tags);
        writer.WriteScalar("ManifestType", "defaultLocale");
        writer.WriteScalar("ManifestVersion", manifestVersion);
        return AddSchemaHeader(writer.ToString(), "defaultLocale", manifestVersion);
    }

    private static string ResolveManifestVersion(PowerForgeReleaseWingetOptions winget, PowerForgeReleaseWingetPackage package)
        => !string.IsNullOrWhiteSpace(package.ManifestVersion) ? package.ManifestVersion!.Trim()
            : !string.IsNullOrWhiteSpace(winget.ManifestVersion) ? winget.ManifestVersion!.Trim()
            : "1.12.0";

    private static string AddSchemaHeader(string yaml, string manifestType, string manifestVersion)
        => $"# yaml-language-server: $schema=https://aka.ms/winget-manifest.{manifestType}.{manifestVersion}.schema.json{Environment.NewLine}{yaml}";

    private static string NormalizeInstallerType(string? installerType)
        => string.IsNullOrWhiteSpace(installerType) ? "zip" : installerType!.Trim();
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
