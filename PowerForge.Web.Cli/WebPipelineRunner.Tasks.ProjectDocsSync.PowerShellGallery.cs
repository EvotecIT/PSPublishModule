using System;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private const string DefaultPowerShellGalleryPackageBaseUrl = "https://www.powershellgallery.com/api/v2/package";

    /// <summary>
    /// Resolves the exact-package endpoint used only as a fallback for PowerShell API help.
    /// </summary>
    private static string ResolvePowerShellGalleryPackageBaseUrl(JsonElement step)
    {
        var configured = GetString(step, "powerShellGalleryPackageBaseUrl") ??
                         GetString(step, "powershell-gallery-package-base-url");
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultPowerShellGalleryPackageBaseUrl
            : configured.Trim().TrimEnd('/');
    }

    private static bool HasPowerShellGalleryPackage(string? packageId, string? packageVersion)
    {
        return !string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(packageVersion);
    }

    /// <summary>
    /// Builds a version-pinned PSGallery download URL from catalog metadata. The caller intentionally
    /// downloads this source without the GitHub artifact bearer token.
    /// </summary>
    private static bool TryBuildPowerShellGalleryPackageUrl(
        ProjectDocsCatalogItem project,
        string packageBaseUrl,
        out string? packageUrl)
    {
        packageUrl = null;
        if (!HasPowerShellGalleryPackage(project.PowerShellGalleryPackageId, project.PowerShellGalleryPackageVersion) ||
            string.IsNullOrWhiteSpace(packageBaseUrl))
        {
            return false;
        }

        packageUrl = $"{packageBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(project.PowerShellGalleryPackageId!.Trim())}/{Uri.EscapeDataString(project.PowerShellGalleryPackageVersion!.Trim())}";
        return true;
    }
}
