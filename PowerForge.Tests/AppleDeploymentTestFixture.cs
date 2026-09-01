namespace PowerForge.Tests;

internal static class AppleDeploymentTestFixture
{
    internal static void MaterializeConfiguredBuildProduct(ProcessRunRequest request)
    {
        var outputRoot = TryResolveConfiguredBuildProductDirectory(request);
        if (outputRoot is null)
            return;

        Directory.CreateDirectory(outputRoot);
        if (Directory.EnumerateDirectories(outputRoot, "*.app", SearchOption.TopDirectoryOnly).Any())
            return;

        var arguments = request.Arguments.ToArray();
        var derivedDataIndex = Array.IndexOf(arguments, "-derivedDataPath");
        if (derivedDataIndex >= 0 && derivedDataIndex + 1 < request.Arguments.Count)
        {
            var productsRoot = Path.Combine(request.Arguments[derivedDataIndex + 1], "Build", "Products");
            if (Directory.Exists(productsRoot))
            {
                var candidates = Directory.EnumerateDirectories(
                        productsRoot,
                        "*.app",
                        SearchOption.AllDirectories)
                    .ToArray();
                if (candidates.Length == 1)
                {
                    AppleArtifactCopy.CopyDirectory(
                        candidates[0],
                        Path.Combine(outputRoot, Path.GetFileName(candidates[0])));
                    return;
                }
            }
        }

        var schemeIndex = Array.IndexOf(arguments, "-scheme");
        var scheme = schemeIndex >= 0 && schemeIndex + 1 < request.Arguments.Count
            ? request.Arguments[schemeIndex + 1]
            : "AppleApp";
        var app = Directory.CreateDirectory(Path.Combine(outputRoot, scheme + ".app"));
        File.WriteAllText(Path.Combine(app.FullName, "payload"), "test product");
    }

    internal static string? TryResolvePrivateProductRoot(ProcessRunRequest request)
    {
        var outputSetting = request.Arguments.FirstOrDefault(argument =>
            argument.StartsWith("SYMROOT=", StringComparison.Ordinal));
        return outputSetting?.Substring("SYMROOT=".Length);
    }

    internal static string? TryResolveConfiguredBuildProductDirectory(
        ProcessRunRequest request)
    {
        var productRoot = TryResolvePrivateProductRoot(request);
        if (productRoot is null)
            return null;

        var arguments = request.Arguments.ToArray();
        var configurationIndex = Array.IndexOf(arguments, "-configuration");
        var configuration = configurationIndex >= 0 &&
                            configurationIndex + 1 < arguments.Length
            ? arguments[configurationIndex + 1]
            : "Debug";
        var destinationIndex = Array.IndexOf(arguments, "-destination");
        var destination = destinationIndex >= 0 &&
                          destinationIndex + 1 < arguments.Length
            ? arguments[destinationIndex + 1]
            : string.Empty;
        var productDirectory = destination.Contains(
                "platform=macOS",
                StringComparison.OrdinalIgnoreCase)
            ? destination.Contains(
                    "variant=Mac Catalyst",
                    StringComparison.OrdinalIgnoreCase)
                ? configuration + "-maccatalyst"
                : configuration
            : destination.Contains("watchOS", StringComparison.OrdinalIgnoreCase)
                ? configuration + "-watchos"
                : destination.Contains("tvOS", StringComparison.OrdinalIgnoreCase)
                    ? configuration + "-appletvos"
                    : destination.Contains("visionOS", StringComparison.OrdinalIgnoreCase)
                        ? configuration + "-xros"
                        : configuration + "-iphoneos";
        return Path.Combine(productRoot, productDirectory);
    }

    internal static void WriteSharedSchemes(
        string workingDirectory,
        params string[] schemeNames)
    {
        foreach (var project in Directory.EnumerateDirectories(
                     workingDirectory,
                     "*.xcodeproj",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(project) & FileAttributes.ReparsePoint) != 0)
                continue;

            var schemeRoot = Directory.CreateDirectory(Path.Combine(
                project,
                "xcshareddata",
                "xcschemes"));
            foreach (var scheme in schemeNames
                         .Append(Path.GetFileNameWithoutExtension(project))
                         .Distinct(StringComparer.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(schemeRoot.FullName, scheme + ".xcscheme"),
                    "<Scheme />");
            }
        }
    }
}
