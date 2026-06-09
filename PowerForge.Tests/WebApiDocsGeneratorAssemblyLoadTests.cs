using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorAssemblyLoadTests
{
    [Fact]
    public void GetApiDocsHostProbeDirectories_IncludesAssemblyDirectoryAndAppBase()
    {
        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;

        var directories = WebApiDocsGenerator.GetApiDocsHostProbeDirectories(assemblyPath);

        Assert.Contains(Path.GetDirectoryName(assemblyPath)!, directories, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(AppContext.BaseDirectory), directories, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetApiDocsHostProbeDirectories_IncludesPsHomeWhenConfigured()
    {
        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var temporaryPsHome = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-pshome-" + Guid.NewGuid().ToString("N"))).FullName;
        var originalPsHome = Environment.GetEnvironmentVariable("PSHOME");

        try
        {
            Environment.SetEnvironmentVariable("PSHOME", temporaryPsHome);

            var directories = WebApiDocsGenerator.GetApiDocsHostProbeDirectories(assemblyPath);

            Assert.Contains(Path.GetFullPath(temporaryPsHome), directories, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSHOME", originalPsHome);
            TryDeleteDirectory(temporaryPsHome);
        }
    }

    [Theory]
    [InlineData("System.Net.Requests", false)]
    [InlineData("System.Runtime", false)]
    [InlineData("netstandard", false)]
    [InlineData("System.Management.Automation", true)]
    [InlineData("Microsoft.PowerShell.Commands.Utility", true)]
    [InlineData("Scriban", true)]
    public void GetApiDocsShouldProbeHostAssemblyPaths_AppliesFrameworkProbePolicy(string assemblyName, bool expected)
    {
        var actual = WebApiDocsGenerator.GetApiDocsShouldProbeHostAssemblyPaths(assemblyName);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetNuGetPackageRootCandidates_PrefersConfiguredAndEnvironmentHomes()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-nuget-candidates-" + Guid.NewGuid().ToString("N"))).FullName;
        var configuredPackages = Path.Combine(tempRoot, "configured-packages");
        var homeRoot = Path.Combine(tempRoot, "home-root");
        var userProfileRoot = Path.Combine(tempRoot, "user-profile-root");
        var driveRoot = Path.Combine(tempRoot, "homedrive-root");
        Directory.CreateDirectory(configuredPackages);
        Directory.CreateDirectory(Path.Combine(homeRoot, ".nuget", "packages"));
        Directory.CreateDirectory(Path.Combine(userProfileRoot, ".nuget", "packages"));
        Directory.CreateDirectory(Path.Combine(driveRoot, "profile", ".nuget", "packages"));

        var originalNugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHomeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
        var originalHomePath = Environment.GetEnvironmentVariable("HOMEPATH");

        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", configuredPackages);
            Environment.SetEnvironmentVariable("HOME", homeRoot);
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileRoot);
            Environment.SetEnvironmentVariable("HOMEDRIVE", driveRoot);
            Environment.SetEnvironmentVariable("HOMEPATH", "\\profile");

            var candidates = WebApiDocsGenerator.GetApiDocsNuGetPackageRootCandidates();

            Assert.NotEmpty(candidates);
            Assert.Equal(Path.GetFullPath(configuredPackages), candidates[0]);
            Assert.Contains(Path.GetFullPath(Path.Combine(homeRoot, ".nuget", "packages")), candidates, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(Path.Combine(userProfileRoot, ".nuget", "packages")), candidates, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(Path.Combine(driveRoot, "profile", ".nuget", "packages")), candidates, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", originalNugetPackages);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOMEDRIVE", originalHomeDrive);
            Environment.SetEnvironmentVariable("HOMEPATH", originalHomePath);
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNuGetPackageRootCandidates_DeduplicatesEquivalentEntries()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-nuget-dedupe-" + Guid.NewGuid().ToString("N"))).FullName;
        var packageRoot = Path.Combine(tempRoot, ".nuget", "packages");
        Directory.CreateDirectory(packageRoot);

        var originalNugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", packageRoot);
            Environment.SetEnvironmentVariable("HOME", tempRoot);
            Environment.SetEnvironmentVariable("USERPROFILE", tempRoot);

            var candidates = WebApiDocsGenerator.GetApiDocsNuGetPackageRootCandidates();

            Assert.Equal(1, candidates.Count(path => string.Equals(path, Path.GetFullPath(packageRoot), StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", originalNugetPackages);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNuGetPackageRootCandidates_IncludesRepoLocalPackagesNearAssembly()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-local-packages-" + Guid.NewGuid().ToString("N"))).FullName;
        var assemblyDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "src", "TestimoX", "bin", "Release", "net8.0-windows")).FullName;
        var repoLocalPackages = Directory.CreateDirectory(Path.Combine(tempRoot, ".nuget", "packages")).FullName;
        var assemblyPath = Path.Combine(assemblyDirectory, "TestimoX.dll");
        File.WriteAllText(assemblyPath, string.Empty);

        try
        {
            var candidates = WebApiDocsGenerator.GetApiDocsNuGetPackageRootCandidates(assemblyPath);

            Assert.Contains(Path.GetFullPath(repoLocalPackages), candidates, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetNuGetPackageRootCandidates_UsesNuGetConfigRelativeGlobalPackagesFolder()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-nuget-config-" + Guid.NewGuid().ToString("N"))).FullName;
        var assemblyDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "Website", "..", "TestimoX", "bin", "Release", "net8.0-windows")).FullName;
        var configuredPackages = Directory.CreateDirectory(Path.Combine(tempRoot, ".nuget", "packages")).FullName;
        var assemblyPath = Path.Combine(assemblyDirectory, "TestimoX.dll");
        var configPath = Path.Combine(tempRoot, "NuGet.config");
        File.WriteAllText(assemblyPath, string.Empty);
        File.WriteAllText(configPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value=".nuget/packages" />
              </config>
            </configuration>
            """);

        try
        {
            var candidates = WebApiDocsGenerator.GetApiDocsNuGetPackageRootCandidates(assemblyPath);

            Assert.Contains(Path.GetFullPath(configuredPackages), candidates, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetApiDocsResolvedHeadHtml_IncludesSiteHeadLinksFromNavConfig()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-head-links-" + Guid.NewGuid().ToString("N"))).FullName;
        var siteJsonPath = Path.Combine(tempRoot, "site.json");

        try
        {
            File.WriteAllText(siteJsonPath,
                """
                {
                  "Name": "HtmlForgeX",
                  "Head": {
                    "Links": [
                      { "Rel": "preconnect", "Href": "https://cdn.jsdelivr.net", "Crossorigin": "anonymous" },
                      { "Rel": "dns-prefetch", "Href": "https://cdn.jsdelivr.net" }
                    ]
                  }
                }
                """);

            var options = new WebApiDocsOptions
            {
                NavJsonPath = siteJsonPath
            };

            var headHtml = WebApiDocsGenerator.GetApiDocsResolvedHeadHtml(options);

            Assert.Contains("rel=\"preconnect\"", headHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://cdn.jsdelivr.net\"", headHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("crossorigin=\"anonymous\"", headHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rel=\"dns-prefetch\"", headHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetApiDocsResolvedHeadHtml_AppendsSiteHeadLinksAfterExplicitHeadHtml()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-head-links-merge-" + Guid.NewGuid().ToString("N"))).FullName;
        var siteJsonPath = Path.Combine(tempRoot, "site.json");
        var headHtmlPath = Path.Combine(tempRoot, "head.html");

        try
        {
            File.WriteAllText(siteJsonPath,
                """
                {
                  "Head": {
                    "Links": [
                      { "Rel": "preconnect", "Href": "https://cdn.jsdelivr.net", "Crossorigin": "anonymous" }
                    ]
                  }
                }
                """);
            File.WriteAllText(headHtmlPath, "<meta name=\"robots\" content=\"index,follow\" />");

            var options = new WebApiDocsOptions
            {
                NavJsonPath = siteJsonPath,
                HeadHtmlPath = headHtmlPath
            };

            var headHtml = WebApiDocsGenerator.GetApiDocsResolvedHeadHtml(options);

            Assert.Contains("<meta name=\"robots\" content=\"index,follow\" />", headHtml, StringComparison.Ordinal);
            Assert.Contains("href=\"https://cdn.jsdelivr.net\"", headHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
