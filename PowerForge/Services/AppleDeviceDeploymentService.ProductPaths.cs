namespace PowerForge;

public sealed partial class AppleDeviceDeploymentService
{
    private static string ResolveDerivedDataPath(AppleAppBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DerivedDataPath))
            return Path.GetFullPath(request.DerivedDataPath!);

        var safeScheme = SanitizePathPart(request.Scheme);
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        return Path.Combine(
            Path.GetTempPath(),
            "powerforge-apple-derived-data",
            $"{safeScheme}-{uniqueSuffix}");
    }

    private static string ResolveAppPath(
        AppleAppBuildRequest request,
        string derivedDataPath)
    {
        if (!string.IsNullOrWhiteSpace(request.AppPath))
            return Path.GetFullPath(request.AppPath!);

        return Path.Combine(
            derivedDataPath,
            "Build",
            "Products",
            GetProductDirectory(request),
            ResolveProductName(request) + ".app");
    }

    private static string ResolveBuiltAppPath(
        AppleAppBuildRequest request,
        string productDirectory,
        string expectedAppPath)
    {
        if (!string.IsNullOrWhiteSpace(request.AppPath) ||
            !string.IsNullOrWhiteSpace(request.ProductName) ||
            Directory.Exists(expectedAppPath))
        {
            return expectedAppPath;
        }

        if (!Directory.Exists(productDirectory))
            return expectedAppPath;

        var candidates = Directory.EnumerateDirectories(
                productDirectory,
                "*.app",
                SearchOption.TopDirectoryOnly)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : expectedAppPath;
    }

    private static string ResolveProductName(AppleAppBuildRequest request)
        => string.IsNullOrWhiteSpace(request.ProductName)
            ? request.Scheme.Trim()
            : request.ProductName!.Trim();

    private static string GetProductDirectory(AppleAppBuildRequest request)
    {
        var configuration = string.IsNullOrWhiteSpace(request.Configuration)
            ? "Debug"
            : request.Configuration.Trim();
        return request.Platform == ApplePlatform.macOS
            ? request.ArchiveVariant == AppleArchiveVariant.MacCatalyst
                ? $"{configuration}-maccatalyst"
                : configuration
            : $"{configuration}-{GetSdkProductSuffix(request.Platform)}";
    }

    private static string GetSdkProductSuffix(ApplePlatform platform)
        => platform switch
        {
            ApplePlatform.iOS => "iphoneos",
            ApplePlatform.iPadOS => "iphoneos",
            ApplePlatform.tvOS => "appletvos",
            ApplePlatform.watchOS => "watchos",
            ApplePlatform.visionOS => "xros",
            ApplePlatform.macOS => "macosx",
            _ => "iphoneos"
        };
}
