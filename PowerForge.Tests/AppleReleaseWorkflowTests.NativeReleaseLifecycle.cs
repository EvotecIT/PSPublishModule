using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void PublicMetadataOperationPreservesStructuredFailedNativeReceipt()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string repository = Path.Combine(root.FullName, "source");
            string build = Path.Combine(repository, "Build");
            string helpers = Path.Combine(build, "Private");
            Directory.CreateDirectory(helpers);
            string sourceBuild = Path.Combine(FindRepoRoot(), "Build");
            string wrapper = Path.Combine(build, "Invoke-PowerForgePublicRelease.ps1");
            File.Copy(Path.Combine(sourceBuild, "Invoke-PowerForgePublicRelease.ps1"), wrapper);
            foreach (string helper in Directory.GetFiles(Path.Combine(sourceBuild, "Private"), "*.ps1"))
                File.Copy(helper, Path.Combine(helpers, Path.GetFileName(helper)));
            File.WriteAllText(Path.Combine(repository, "powerforge.json"), "{}");
            File.WriteAllText(Path.Combine(build, "release.json"), """
                {
                  "Module": { "ModuleVersion": "3.0.X" },
                  "Packages": { "VersionTracks": { "Main": { "ExpectedVersion": "3.0.X" } } },
                  "GitHub": {}
                }
                """);
            File.WriteAllText(Path.Combine(build, "Build-Project.ps1"), """
                param([switch] $PrepareMetadata)
                if (-not $PrepareMetadata) { throw 'PrepareMetadata was not forwarded' }
                [pscustomobject]@{
                    Success = $false
                    ErrorMessage = 'native metadata failure'
                    ReleaseAssetEntries = @(@{ Path = 'original-signed.nupkg' })
                    Packages = @{ Result = @{ Release = @{ PublishedPackages = @('original-signed.nupkg') } } }
                } | ConvertTo-Json -Depth 20
                exit 1
                """);
            Run("git", repository, "init").EnsureSuccess();
            Run("git", repository, "config", "user.email", "powerforge-tests@example.invalid").EnsureSuccess();
            Run("git", repository, "config", "user.name", "PowerForge Tests").EnsureSuccess();
            Run("git", repository, "add", ".").EnsureSuccess();
            Run("git", repository, "commit", "-m", "fixture").EnsureSuccess();
            string revision = Run("git", repository, "rev-parse", "HEAD").EnsureSuccess().StandardOutput.Trim();
            string receiptPath = Path.Combine(root.FullName, "failed-native.json");
            string command = $$"""
                function dotnet { $global:LASTEXITCODE = 0 }
                & '{{wrapper.Replace("'", "''", StringComparison.Ordinal)}}' -Operation PrepareMetadata `
                    -Version 3.0.189 -ExpectedCommit {{revision}} -ReceiptPath '{{receiptPath.Replace("'", "''", StringComparison.Ordinal)}}'
                """;

            var result = Run("pwsh", repository, "-NoProfile", "-Command", command);

            Assert.NotEqual(0, result.ExitCode);
            using var receipt = JsonDocument.Parse(File.ReadAllText(receiptPath));
            Assert.False(receipt.RootElement.GetProperty("Success").GetBoolean());
            Assert.Equal("native metadata failure", receipt.RootElement.GetProperty("ErrorMessage").GetString());
            Assert.Equal("original-signed.nupkg", receipt.RootElement.GetProperty("ReleaseAssetEntries")[0].GetProperty("Path").GetString());
            Assert.True(string.IsNullOrWhiteSpace(Run("git", repository, "status", "--porcelain=v1").EnsureSuccess().StandardOutput));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public void BuildProjectJsonRetainsFailedReleaseAssetsAndExitCode(string shell)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string scriptPath = Path.Combine(FindRepoRoot(), "Build", "Build-Project.ps1");
            string command = $$"""
                function dotnet { $global:LASTEXITCODE = 0 }
                function Import-Module { }
                function Invoke-PowerForgeRelease {
                    [pscustomobject]@{
                        Success = $false
                        ErrorMessage = 'registry indexing failed'
                        ReleaseAssetEntries = @(@{ Path = 'original-signed.nupkg'; Category = 'Package'; SourceCommit = 'release-source' })
                        Packages = @{ Result = @{ Release = @{ PublishedPackages = @('original-signed.nupkg') } } }
                        UnifiedGitHubRelease = @{ Success = $false; ErrorMessage = 'HTTP 404' }
                    }
                    throw 'registry indexing failed'
                }
                & '{{scriptPath.Replace("'", "''", StringComparison.Ordinal)}}' -Json
                """;

            var result = Run(shell, root.FullName, "-NoProfile", "-Command", command);

            Assert.Equal(1, result.ExitCode);
            using var receipt = JsonDocument.Parse(result.StandardOutput);
            var release = receipt.RootElement;
            Assert.False(release.GetProperty("Success").GetBoolean());
            Assert.Equal("registry indexing failed", release.GetProperty("ErrorMessage").GetString());
            Assert.Equal("original-signed.nupkg", release.GetProperty("ReleaseAssetEntries")[0].GetProperty("Path").GetString());
            Assert.Equal("original-signed.nupkg", release.GetProperty("Packages").GetProperty("Result")
                .GetProperty("Release").GetProperty("PublishedPackages")[0].GetString());
            Assert.False(release.GetProperty("UnifiedGitHubRelease").GetProperty("Success").GetBoolean());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void PrepareMetadataUpdatesOnlyTwelveProjectsManifestAndHelpVersion()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string modulePath = Path.Combine(root.FullName, "Module");
            Directory.CreateDirectory(Path.Combine(modulePath, "Docs"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "Build"));
            File.WriteAllText(Path.Combine(modulePath, "Fixture.psm1"), "function Get-Fixture { 'fixture' }\n");
            File.WriteAllText(Path.Combine(modulePath, "Fixture.psd1"), """
                @{
                    RootModule = 'Fixture.psm1'
                    ModuleVersion = '3.0.188'
                    GUID = '10000000-0000-0000-0000-000000000001'
                    FunctionsToExport = @('Get-Fixture')
                }
                """);
            const string readme = "---\r\nModule Name: Fixture\r\nHelp Version: 3.0.188\r\n---\r\n# Fixture\r\nRetained generated command index.\r\n";
            string readmePath = Path.Combine(modulePath, "Docs", "Readme.md");
            File.WriteAllText(readmePath, readme);
            string signedPath = Path.Combine(root.FullName, "signed-staging", "original.nupkg");
            Directory.CreateDirectory(Path.GetDirectoryName(signedPath)!);
            File.WriteAllText(signedPath, "original-signed-bytes");

            var projectNames = Enumerable.Range(1, 12).Select(index => $"Project{index}").ToArray();
            foreach (string name in projectNames)
            {
                Directory.CreateDirectory(Path.Combine(root.FullName, name));
                File.WriteAllText(Path.Combine(root.FullName, name, name + ".csproj"), $$"""
                    <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework><VersionPrefix>3.0.188</VersionPrefix>
                    <PackageVersion>3.0.188</PackageVersion><PackageId>{{name}}</PackageId>
                    </PropertyGroup></Project>
                    """);
            }
            File.WriteAllText(Path.Combine(root.FullName, "powerforge.json"), """
                {
                  "SchemaVersion": 1,
                  "Build": { "Name": "Fixture", "SourcePath": "Module", "Version": "3.0.X" },
                  "Install": { "Enabled": true },
                  "Segments": [
                    { "Type": "Manifest", "Configuration": { "ModuleVersion": "3.0.X" } },
                    { "Type": "Documentation", "Configuration": { "Path": "Docs", "PathReadme": "Docs/Readme.md" } }
                  ]
                }
                """);
            string configPath = Path.Combine(root.FullName, "Build", "release.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(new
            {
                Module = new { RepositoryRoot = "..", ConfigPath = "powerforge.json", ModuleVersion = "3.0.X" },
                Packages = new
                {
                    RootPath = root.FullName,
                    StagingPath = "signed-staging",
                    CleanStaging = true,
                    VersionTracks = new Dictionary<string, object>
                    {
                        ["Fixture"] = new { ExpectedVersion = "3.0.X", AnchorProject = projectNames[0], Projects = projectNames.Skip(1).ToArray() }
                    },
                    ExpectedVersionMapAsInclude = true,
                    UpdateVersions = true,
                    Build = true,
                    PublishNuget = true,
                    PublishGitHub = true
                }
            }));
            var before = Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllText);
            string scriptPath = Path.Combine(FindRepoRoot(), "Build", "Build-Project.ps1");
            string binaryPath = typeof(PSPublishModule.InvokeModuleBuildCommand).Assembly.Location;
            string command = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module '{{binaryPath.Replace("'", "''", StringComparison.Ordinal)}}'
                function dotnet { $global:LASTEXITCODE = 0 }
                function Import-Module { Microsoft.PowerShell.Core\Import-Module '{{binaryPath.Replace("'", "''", StringComparison.Ordinal)}}' -Force }
                & '{{scriptPath.Replace("'", "''", StringComparison.Ordinal)}}' -PrepareMetadata `
                    -ConfigPath '{{configPath.Replace("'", "''", StringComparison.Ordinal)}}' -ModuleVersion 3.0.189 -Json
                """;
            Run("pwsh", root.FullName, "-NoProfile", "-Command", command).EnsureSuccess();

            var changed = before.Keys.Where(path => before[path] != File.ReadAllText(path)).ToArray();
            Assert.Equal(14, changed.Length);
            foreach (string name in projectNames)
            {
                string project = File.ReadAllText(Path.Combine(root.FullName, name, name + ".csproj"));
                Assert.Contains("<VersionPrefix>3.0.189</VersionPrefix>", project);
                Assert.Contains("<PackageVersion>3.0.189</PackageVersion>", project);
            }
            Assert.Contains("3.0.189", File.ReadAllText(Path.Combine(modulePath, "Fixture.psd1")));
            Assert.Equal(readme.Replace("3.0.188", "3.0.189", StringComparison.Ordinal), File.ReadAllText(readmePath));
            Assert.Equal("original-signed-bytes", File.ReadAllText(signedPath));
            Assert.Equal(before[Path.Combine(modulePath, "Fixture.psm1")], File.ReadAllText(Path.Combine(modulePath, "Fixture.psm1")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
