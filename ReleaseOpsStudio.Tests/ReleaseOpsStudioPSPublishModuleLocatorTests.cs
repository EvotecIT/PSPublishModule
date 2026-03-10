using ReleaseOpsStudio.Domain.PowerShell;
using ReleaseOpsStudio.Orchestrator.PowerShell;

namespace ReleaseOpsStudio.Tests;

public sealed class ReleaseOpsStudioPSPublishModuleLocatorTests
{
    [Fact]
    public void Resolve_UsableEnvironmentOverride_WinsOverOtherCandidates()
    {
        using var scope = new TemporaryDirectoryScope();
        var environmentManifest = scope.CreateModuleManifest("env", "5.1.0", prerelease: "preview1");
        var repositoryManifest = scope.CreateModuleManifest("repo", "4.9.0");
        var installedRoot = scope.CreateDirectory("installed");
        scope.CreateVersionedModule(installedRoot, "3.0.0.139", "3.0.0");

        var resolution = PSPublishModuleLocator.Resolve(environmentManifest, repositoryManifest, [installedRoot]);

        Assert.Equal(PSPublishModuleResolutionSource.EnvironmentOverride, resolution.Source);
        Assert.True(resolution.IsUsable);
        Assert.Equal("5.1.0-preview1", resolution.ModuleVersion);
        Assert.Equal("Ready", resolution.StatusDisplay);
    }

    [Fact]
    public void Resolve_UsableRepositoryManifest_IsUsedWhenNoOverrideExists()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryManifest = scope.CreateModuleManifest("repo", "4.8.2");
        var installedRoot = scope.CreateDirectory("installed");
        scope.CreateVersionedModule(installedRoot, "3.0.0.139", "3.0.0");

        var resolution = PSPublishModuleLocator.Resolve(configuredPath: null, repositoryManifest, [installedRoot]);

        Assert.Equal(PSPublishModuleResolutionSource.RepositoryManifest, resolution.Source);
        Assert.True(resolution.IsUsable);
        Assert.Equal("4.8.2", resolution.ModuleVersion);
        Assert.Equal("Ready", resolution.StatusDisplay);
    }

    [Fact]
    public void Resolve_InstalledModuleCandidate_SelectsHighestVersionAndWarns()
    {
        using var scope = new TemporaryDirectoryScope();
        var installedRoot = scope.CreateDirectory("installed");
        scope.CreateVersionedModule(installedRoot, "3.0.0.139", "3.0.0");
        scope.CreateVersionedModule(installedRoot, "3.2.0.0", "3.2.0");

        var fallbackRepositoryManifest = Path.Combine(scope.RootPath, "missing", "PSPublishModule.psd1");
        var resolution = PSPublishModuleLocator.Resolve(configuredPath: null, fallbackRepositoryManifest, [installedRoot]);

        Assert.Equal(PSPublishModuleResolutionSource.InstalledModule, resolution.Source);
        Assert.True(resolution.IsUsable);
        Assert.Equal("3.2.0", resolution.ModuleVersion);
        Assert.Equal("Watch", resolution.StatusDisplay);
        Assert.Contains("lag behind repo DSL changes", resolution.Warning);
    }

    [Fact]
    public void Resolve_NoUsableCandidate_ReturnsFallbackBlockedState()
    {
        using var scope = new TemporaryDirectoryScope();
        var fallbackRepositoryManifest = Path.Combine(scope.RootPath, "missing", "PSPublishModule.psd1");
        var resolution = PSPublishModuleLocator.Resolve(configuredPath: null, fallbackRepositoryManifest, [scope.CreateDirectory("empty")]);

        Assert.Equal(PSPublishModuleResolutionSource.FallbackPath, resolution.Source);
        Assert.False(resolution.IsUsable);
        Assert.Equal("Blocked", resolution.StatusDisplay);
        Assert.Contains("No usable PSPublishModule engine was found", resolution.Warning);
        Assert.Equal(fallbackRepositoryManifest, resolution.ManifestPath);
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "ReleaseOpsStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateModuleManifest(string relativePath, string moduleVersion, string? prerelease = null)
        {
            var moduleRoot = CreateDirectory(relativePath);
            CreateModuleLayout(moduleRoot, moduleVersion, prerelease);
            return Path.Combine(moduleRoot, "PSPublishModule.psd1");
        }

        public string CreateVersionedModule(string installedRoot, string versionFolderName, string moduleVersion, string? prerelease = null)
        {
            var moduleRoot = Path.Combine(installedRoot, versionFolderName);
            Directory.CreateDirectory(moduleRoot);
            CreateModuleLayout(moduleRoot, moduleVersion, prerelease);
            return Path.Combine(moduleRoot, "PSPublishModule.psd1");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static void CreateModuleLayout(string moduleRoot, string moduleVersion, string? prerelease)
        {
            Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Default"));
            Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core"));

            File.WriteAllText(Path.Combine(moduleRoot, "PSPublishModule.psm1"), "# test module");
            File.WriteAllText(Path.Combine(moduleRoot, "Lib", "Default", "PSPublishModule.dll"), string.Empty);
            File.WriteAllText(Path.Combine(moduleRoot, "Lib", "Core", "PSPublishModule.dll"), string.Empty);

            var manifestLines = new List<string> {
                "@{",
                $"    ModuleVersion = '{moduleVersion}'"
            };

            if (!string.IsNullOrWhiteSpace(prerelease))
            {
                manifestLines.Add("    PrivateData = @{");
                manifestLines.Add("        PSData = @{");
                manifestLines.Add($"            Prerelease = '{prerelease}'");
                manifestLines.Add("        }");
                manifestLines.Add("    }");
            }

            manifestLines.Add("}");
            File.WriteAllLines(Path.Combine(moduleRoot, "PSPublishModule.psd1"), manifestLines);
        }
    }
}

