namespace PowerForge.Tests;

internal static class AppleDeploymentTestFixture
{
    internal static void MaterializeConfiguredBuildProduct(ProcessRunRequest request)
    {
        var outputSetting = request.Arguments.FirstOrDefault(argument =>
            argument.StartsWith("CONFIGURATION_BUILD_DIR=", StringComparison.Ordinal));
        if (outputSetting is null)
            return;

        var outputRoot = outputSetting.Substring("CONFIGURATION_BUILD_DIR=".Length);
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
