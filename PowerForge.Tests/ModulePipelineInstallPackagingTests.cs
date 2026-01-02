using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineInstallPackagingTests
{
    [Fact]
    public void PipelineInstall_UsesPackagedLayout_NotFullRepoCopy()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleName = "TestModule";

            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            var publicDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            File.WriteAllText(Path.Combine(publicDir.FullName, "Get-Test.ps1"), "function Get-Test { 'ok' }");

            // These should never end up in the installed module.
            Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".github"));
            Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Sources"));
            File.WriteAllText(Path.Combine(projectRoot.FullName, ".editorconfig"), "root=true");
            File.WriteAllText(Path.Combine(projectRoot.FullName, "README.md"), "readme");

            var destinationRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "modules"));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeFiles = Array.Empty<string>(),
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = true,
                    Strategy = InstallationStrategy.Exact,
                    KeepVersions = 1,
                    Roots = new[] { destinationRoot.FullName }
                },
                Segments = Array.Empty<IConfigurationSegment>()
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.NotNull(result.InstallResult);

            var installedPath = Path.Combine(destinationRoot.FullName, moduleName, result.InstallResult!.Version);
            Assert.True(File.Exists(Path.Combine(installedPath, moduleName + ".psd1")));
            Assert.True(File.Exists(Path.Combine(installedPath, moduleName + ".psm1")));

            Assert.False(File.Exists(Path.Combine(installedPath, ".editorconfig")));
            Assert.False(File.Exists(Path.Combine(installedPath, "README.md")));
            Assert.False(Directory.Exists(Path.Combine(installedPath, ".github")));
            Assert.False(Directory.Exists(Path.Combine(installedPath, "Sources")));
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

