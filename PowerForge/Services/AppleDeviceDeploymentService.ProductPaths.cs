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
        expectedAppPath = EnsurePathWithinProductDirectory(
            expectedAppPath,
            productDirectory);
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
        return candidates.Length == 1
            ? EnsurePathWithinProductDirectory(candidates[0], productDirectory)
            : expectedAppPath;
    }

    private static string ResolveProductName(AppleAppBuildRequest request)
    {
        var productName = string.IsNullOrWhiteSpace(request.ProductName)
            ? request.Scheme.Trim()
            : request.ProductName!.Trim();
        if (productName is "." or ".." ||
            Path.IsPathRooted(productName) ||
            productName.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
        {
            throw new InvalidOperationException(
                $"ProductName must be a simple app bundle name without a path: '{productName}'.");
        }
        return productName;
    }

    private static string EnsurePathWithinProductDirectory(
        string appPath,
        string productDirectory)
    {
        var fullProductDirectory = Path.GetFullPath(productDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullAppPath = Path.GetFullPath(appPath);
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(
            fullProductDirectory);
        if (!fullAppPath.StartsWith(
                EnsureTrailingDirectorySeparator(fullProductDirectory),
                comparison))
        {
            throw new InvalidOperationException(
                $"Built app path '{fullAppPath}' must remain inside the private product directory '{fullProductDirectory}'.");
        }
        return fullAppPath;
    }

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
