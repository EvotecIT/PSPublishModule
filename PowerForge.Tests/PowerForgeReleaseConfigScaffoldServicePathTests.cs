using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseConfigScaffoldServicePathTests
{
    [Theory]
    [InlineData("project.build.json", "..", "../plans/release.json")]
    [InlineData(".powerforge/project.build.json", "..", "../.powerforge/plans/release.json")]
    public void Generate_rebases_embedded_package_paths_to_the_release_config(
        string packageConfigRelativePath,
        string expectedRootPath,
        string expectedPlanPath)
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "pf-release-scaffold-rebase-" + Guid.NewGuid().ToString("N")));
        try
        {
            var packageConfig = Path.Combine(
                root.FullName,
                packageConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(packageConfig)!);
            File.WriteAllText(
                packageConfig,
                """
                {
                  "RootPath": "..",
                  "PlanOutputPath": "plans/release.json",
                  "PublishApiKeyFilePath": "secrets/nuget.txt",
                  "Build": true
                }
                """.Replace(
                    "\"RootPath\": \"..\"",
                    packageConfigRelativePath.StartsWith(".powerforge/", StringComparison.Ordinal)
                        ? "\"RootPath\": \"..\""
                        : "\"RootPath\": \".\""));

            var result = new PowerForgeReleaseConfigScaffoldService().Generate(
                new PowerForgeReleaseConfigScaffoldRequest
                {
                    ProjectRoot = root.FullName,
                    WorkingDirectory = root.FullName,
                    PackagesConfigPath = packageConfigRelativePath,
                    SkipTools = true
                });

            using var release = JsonDocument.Parse(File.ReadAllText(result.ConfigPath));
            var packages = release.RootElement.GetProperty("Packages");
            Assert.Equal(expectedRootPath, packages.GetProperty("RootPath").GetString());
            Assert.Equal(expectedPlanPath, packages.GetProperty("PlanOutputPath").GetString());
            Assert.Equal(
                packageConfigRelativePath.StartsWith(".powerforge/", StringComparison.Ordinal)
                    ? "../.powerforge/secrets/nuget.txt"
                    : "../secrets/nuget.txt",
                packages.GetProperty("PublishApiKeyFilePath").GetString());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
