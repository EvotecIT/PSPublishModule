using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

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
    public void VerifiedCatalog_RejectsPreservedNuGetRegistryData()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteProject("Published.Package");
        var catalog = fixture.WriteCatalog(
            "Evotec",
            ["Published.Package"],
            warnings: ["Preserved existing NuGet stats after upstream fetch warnings returned empty data."]);

        var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            NuGetOwner = "Evotec"
        }));

        Assert.Contains("preserved stale NuGet data", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedCatalog_RejectsPreservedPowerShellGalleryRegistryData()
    {
        using var fixture = new PublicationFixture();
        var module = fixture.WriteModule("PublishedModule");
        var catalog = fixture.WriteCatalog(
            "Evotec",
            [],
            powerShellGalleryOwner: "Przemyslaw.Klys",
            powerShellModules: ["PublishedModule"],
            warnings: ["Preserved existing PowerShell Gallery stats after upstream fetch warnings returned empty data."]);

        var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = module,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.VerifiedCatalog,
            PublicationCatalogPath = catalog,
            PowerShellGalleryOwner = "Przemyslaw.Klys"
        }));

        Assert.Contains("preserved stale PowerShell Gallery data", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void NonePolicy_DoesNotRequireConditionalToolMetadata()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteFile(
            "ConditionalTool.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Conditional.Tool</PackageId><Version>1.2.3</Version><PackAsTool Condition=\"'$(Configuration)' == 'Release'\">true</PackAsTool></PropertyGroup></Project>");
        var quickstart = fixture.WriteFile("quickstart.txt", "Conditional.Tool --help");

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project,
            QuickstartPath = quickstart,
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.None
        });

        Assert.Equal(0, result.InstallCommandCount);
        Assert.DoesNotContain("## Install", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
    }

    [Fact]
    public void NonePolicy_DoesNotRequireConditionalToolMetadataFromPackageFiles()
    {
        using var fixture = new PublicationFixture();
        var project = fixture.WriteFile(
            "ConditionalTool.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Conditional.Tool</PackageId><Version>1.2.3</Version><PackAsTool Condition=\"'$(Configuration)' == 'Release'\">true</PackAsTool></PropertyGroup></Project>");

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            PackageFiles = [project],
            InstallCommandPolicy = WebLlmsInstallCommandPolicy.None
        });

        Assert.Equal(0, result.InstallCommandCount);
        Assert.DoesNotContain("## Install", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
    }

    [Fact]
    public void PackageMetadata_ResolvesImportsUsingStandardProjectDirectoryProperty()
    {
        using var fixture = new PublicationFixture();
        fixture.WriteFile(
            "package.props",
            "<Project><PropertyGroup><PackageId>Imported.Package</PackageId><Version>1.2.3</Version></PropertyGroup></Project>");
        var project = fixture.WriteFile(
            "Imported.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"$(MSBuildProjectDirectory)/package.props\" /></Project>");

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project
        });

        Assert.Equal("Imported.Package", result.PackageId);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public void PackageMetadata_ResolvesDirectoryBuildTargetsPathUsingStandardProjectDirectoryProperty()
    {
        using var fixture = new PublicationFixture();
        fixture.WriteFile(
            "package.targets",
            "<Project><PropertyGroup><PackageId>Targeted.Package</PackageId><Version>1.2.3</Version></PropertyGroup></Project>");
        var project = fixture.WriteFile(
            "Targeted.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><DirectoryBuildTargetsPath>$(MSBuildProjectDirectory)/package.targets</DirectoryBuildTargetsPath></PropertyGroup></Project>");

        var result = WebLlmsGenerator.Generate(new WebLlmsOptions
        {
            SiteRoot = fixture.Root,
            ProjectFile = project
        });

        Assert.Equal("Targeted.Package", result.PackageId);
        Assert.Equal("1.2.3", result.Version);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("999999999999999999999999")]
    [InlineData("-1")]
    public void Cli_RejectsMalformedPublicationCatalogAge(string value)
    {
        using var fixture = new PublicationFixture();

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "llms",
            ["--site-root", fixture.Root, "--publication-catalog-max-age-hours", value],
            outputJson: false,
            new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
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
            string packageVersion = "1.2.3",
            string[]? warnings = null)
        {
            var path = Path.Combine(Root, "publication-catalog.json");
            var payload = new
            {
                generatedAtUtc = (generatedAt ?? DateTimeOffset.UtcNow).ToString("O"),
                warnings = warnings ?? [],
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

        public string WriteFile(string fileName, string content)
        {
            var path = Path.Combine(Root, fileName);
            File.WriteAllText(path, content);
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
