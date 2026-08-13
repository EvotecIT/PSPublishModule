using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebLlmsPublicationCatalogTests
{
    [Fact]
    public void VerifiedCatalog_EmitsNuGetCommandForPackageOwnedByExpectedProfile()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Published.Package");
        var catalog = fixture.WriteCatalog("Evotec", ["Published.Package"]);

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            NuGetOwner = "Evotec",
            PublicationCatalogMaxAgeHours = 24
        });

        Assert.Equal(1, result.InstallCommandCount);
        Assert.Contains("dotnet add package Published.Package", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedCatalog_OmitsNuGetCommandWhenPackageIsNotPublishedByExpectedProfile()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Future.Package");
        var catalog = fixture.WriteCatalog("Evotec", ["Different.Package"]);

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            NuGetOwner = "Evotec"
        });

        Assert.Equal(0, result.InstallCommandCount);
        var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
        var llmsJson = File.ReadAllText(result.LlmsJsonPath);
        var llmsFull = File.ReadAllText(result.LlmsFullPath);
        Assert.Contains("- Package: Future.Package", llmsTxt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Install", llmsTxt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"install\"", llmsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("## Installation", llmsFull, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedCatalog_RejectsCatalogFromUnexpectedOwner()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Published.Package");
        var catalog = fixture.WriteCatalog("SomeoneElse", ["Published.Package"]);

        var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            NuGetOwner = "Evotec"
        }));

        Assert.Contains("does not match expected owner 'Evotec'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedCatalog_OmitsCommandWhenOnlyAnOlderPackageVersionIsPublished()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Future.Package");
        var catalog = fixture.WriteCatalog("Evotec", ["Future.Package"], packageVersion: "1.2.2");

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            NuGetOwner = "Evotec"
        });

        Assert.Equal(0, result.InstallCommandCount);
        Assert.DoesNotContain("## Install", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedCatalog_RejectsStalePublicationProof()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Published.Package");
        var catalog = fixture.WriteCatalog("Evotec", ["Published.Package"], DateTimeOffset.UtcNow.AddDays(-3));

        var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            NuGetOwner = "Evotec",
            PublicationCatalogMaxAgeHours = 24
        }));

        Assert.Contains("outside the accepted 24-hour age window", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedCatalog_RejectsNegativeMaximumCatalogAge()
    {
        using var fixture = new PublicationFixture();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogMaxAgeHours = -1
        }));

        Assert.Equal("PublicationCatalogMaxAgeHours", exception.ParamName);
    }

    [Fact]
    public void VerifiedCatalog_UsesPowerShellGalleryOwnerForModuleCommands()
    {
        using var fixture = new PublicationFixture();
        var module = fixture.WriteModule("PublishedModule");
        var catalog = fixture.WriteCatalog(
            "Evotec",
            [],
            powerShellGalleryOwner: "Przemyslaw.Klys",
            powerShellModules: ["PublishedModule"]);

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = module,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            PowerShellGalleryOwner = "Przemyslaw.Klys"
        });

        Assert.Equal(1, result.InstallCommandCount);
        Assert.Contains("Install-Module PublishedModule", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
    }

    [Fact]
    public void NonePolicy_OmitsDeclaredInstallationCommandsWithoutARegistryCatalog()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Internal.Package");

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.None
        });

        Assert.Equal(0, result.InstallCommandCount);
        Assert.DoesNotContain("## Install", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
    }

    private sealed class PublicationFixture : IDisposable
    {
        public PublicationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "pf-web-llms-publication-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string WriteProject(string packageId)
        {
            var path = Path.Combine(Root, packageId + ".csproj");
            File.WriteAllText(
                path,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>{packageId}</PackageId><Version>1.2.3</Version></PropertyGroup></Project>");
            return path;
        }

        public string WriteModule(string moduleName)
        {
            var path = Path.Combine(Root, moduleName + ".psd1");
            File.WriteAllText(path, $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.2.3' }}");
            return path;
        }

        public string WriteCatalog(
            string nugetOwner,
            string[] nugetPackages,
            DateTimeOffset? generatedAt = null,
            string? powerShellGalleryOwner = null,
            string[]? powerShellModules = null,
            string packageVersion = "1.2.3")
        {
            var path = Path.Combine(Root, "publication-catalog.json");
            var payload = new
            {
                generatedAtUtc = (generatedAt ?? DateTimeOffset.UtcNow).ToString("O"),
                nuget = new
                {
                    owner = nugetOwner,
                    packages = nugetPackages.Select(id => new { id, version = packageVersion }).ToArray()
                },
                powerShellGallery = powerShellGalleryOwner is null
                    ? null
                    : new
                    {
                        owner = powerShellGalleryOwner,
                        modules = (powerShellModules ?? []).Select(id => new { id, version = packageVersion }).ToArray()
                    }
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload));
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
