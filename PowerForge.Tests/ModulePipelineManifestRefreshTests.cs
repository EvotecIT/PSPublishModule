using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineManifestRefreshTests
{
    [Fact]
    public void Run_RefreshesManifestMetadataAndClearsStaleValues()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteModuleWithStaleManifest(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.0",
                            CompatiblePSEditions = new[] { "Desktop", "Core" },
                            Guid = "22222222-2222-2222-2222-222222222222",
                            Author = "New Author",
                            CompanyName = null,
                            Copyright = null,
                            Description = "Fresh description",
                            PowerShellVersion = "5.1",
                            Tags = null,
                            IconUri = null,
                            ProjectUri = "https://new.example/project",
                            DotNetFrameworkVersion = null,
                            LicenseUri = null,
                            RequireLicenseAcceptance = false,
                            Prerelease = null,
                            FormatsToProcess = null
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);
            var manifestPath = result.BuildResult.ManifestPath;

            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "GUID", out var guid));
            Assert.Equal("22222222-2222-2222-2222-222222222222", guid);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "ModuleVersion", out var version));
            Assert.Equal("3.0.0", version);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "Author", out var author));
            Assert.Equal("New Author", author);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "Description", out var description));
            Assert.Equal("Fresh description", description);
            Assert.True(ManifestEditor.TryGetTopLevelString(manifestPath, "PowerShellVersion", out var psVersion));
            Assert.Equal("5.1", psVersion);

            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "CompanyName", out _));
            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "Copyright", out _));
            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "DotNetFrameworkVersion", out _));
            Assert.False(ManifestEditor.TryGetTopLevelString(manifestPath, "Prerelease", out _));
            Assert.False(ManifestEditor.TryGetTopLevelStringArray(manifestPath, "FormatsToProcess", out _));
            Assert.False(ManifestEditor.TryGetPsDataStringArray(manifestPath, "Tags", out _));

            Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out var requiredModules));
            Assert.NotNull(requiredModules);
            Assert.Empty(requiredModules!);

            var content = File.ReadAllText(manifestPath);
            Assert.DoesNotContain("CommandModuleDependencies", content, StringComparison.Ordinal);
            Assert.Contains("ProjectUri = 'https://new.example/project'", content, StringComparison.Ordinal);
            Assert.DoesNotContain("IconUri =", content, StringComparison.Ordinal);
            Assert.DoesNotContain("LicenseUri =", content, StringComparison.Ordinal);
            Assert.Contains("RequireLicenseAcceptance = $false", content, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteModuleWithStaleManifest(string rootPath, string moduleName, string version)
    {
        File.WriteAllText(Path.Combine(rootPath, $"{moduleName}.psm1"), "function Test-Example { 'ok' }");
        File.WriteAllText(Path.Combine(rootPath, $"{moduleName}.psd1"),
            "@{" + Environment.NewLine +
            $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
            $"    ModuleVersion = '{version}'" + Environment.NewLine +
            "    GUID = '11111111-1111-1111-1111-111111111111'" + Environment.NewLine +
            "    Author = 'Old Author'" + Environment.NewLine +
            "    CompanyName = 'Old Company'" + Environment.NewLine +
            "    Copyright = 'Old Copyright'" + Environment.NewLine +
            "    Description = 'Old description'" + Environment.NewLine +
            "    PowerShellVersion = '2.0'" + Environment.NewLine +
            "    DotNetFrameworkVersion = '4.0'" + Environment.NewLine +
            "    Prerelease = 'preview1'" + Environment.NewLine +
            "    CommandModuleDependencies = @{ 'Old.Module' = @('Get-Old') }" + Environment.NewLine +
            "    FormatsToProcess = @('Old.format.ps1xml')" + Environment.NewLine +
            "    RequiredModules = @('LegacyOnly')" + Environment.NewLine +
            "    FunctionsToExport = @('Test-Example')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    AliasesToExport = @()" + Environment.NewLine +
            "    PrivateData = @{" + Environment.NewLine +
            "        PSData = @{" + Environment.NewLine +
            "            Tags = @('OldTag')" + Environment.NewLine +
            "            IconUri = 'https://old.example/icon.png'" + Environment.NewLine +
            "            ProjectUri = 'https://old.example/project'" + Environment.NewLine +
            "            LicenseUri = 'https://old.example/license'" + Environment.NewLine +
            "            RequireLicenseAcceptance = $true" + Environment.NewLine +
            "            ExternalModuleDependencies = @('Old.External')" + Environment.NewLine +
            "        }" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "}" + Environment.NewLine);
    }
}
