using System;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleReadyToRunTests
{
    [Fact]
    public void Plan_ReadyToRunIsOffUnlessConfigured_AndNeedsTheBinaryProject()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            ModulePipelineSpec CreateSpec(params IConfigurationSegment[] segments) => new()
            {
                Build = new ModuleBuildSpec { Name = "SampleModule", SourcePath = projectRoot.FullName, Version = "1.0.0" },
                Segments = segments,
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var unchanged = runner.Plan(CreateSpec());
            Assert.Null(unchanged.BuildSpec.ReadyToRun);
            Assert.DoesNotContain("NETReadyToRun", unchanged.BuildSpec.CsprojRequiredReasons);

            var plan = runner.Plan(CreateSpec(
                new ConfigurationBuildLibrariesSegment
                {
                    BuildLibraries = new BuildLibrariesConfiguration { ReadyToRun = true }
                },
                new ConfigurationBuildLibrariesSegment
                {
                    BuildLibraries = new BuildLibrariesConfiguration
                    {
                        ReadyToRunRuntimeIdentifiers = new[] { "win-x64", "osx-arm64" },
                        ReadyToRunScope = ModuleReadyToRunScope.All
                    }
                }));

            var readyToRun = Assert.IsType<ModuleReadyToRunOptions>(plan.BuildSpec.ReadyToRun);
            Assert.True(readyToRun.Enabled);
            Assert.Equal("net10.0", readyToRun.Framework);
            Assert.Equal(new[] { "win-x64", "osx-arm64" }, readyToRun.RuntimeIdentifiers);
            Assert.Equal(ModuleReadyToRunScope.All, readyToRun.Scope);
            // Without a project the setting cannot take effect, so the build must stop instead of skipping it.
            Assert.Contains("NETReadyToRun", plan.BuildSpec.CsprojRequiredReasons);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("net10.0", "net472", "win-x64", "portable PowerShell 7 payload")]
    [InlineData("net472", "net8.0", "win-x64", "is not supported")]
    [InlineData("netstandard2.0", "net8.0", "win-x64", "is not supported")]
    [InlineData("net10.0", "net8.0", "any", "platform-specific")]
    [InlineData("net10.0", "net8.0", "win", "platform-specific")]
    public void ValidateReadyToRun_RejectsLayoutsThatCannotLoadEverywhere(
        string framework, string moduleFramework, string runtimeIdentifier, string expectedMessage)
    {
        var payloads = ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { moduleFramework });
        var options = new ModuleReadyToRunOptions
        {
            Enabled = true,
            Framework = framework,
            RuntimeIdentifiers = new[] { runtimeIdentifier }
        };

        var error = Assert.Throws<InvalidOperationException>(() => ModuleBuilder.ValidateReadyToRun(options, payloads));
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReadyToRun_NormalizesRuntimeIdentifiers_AndStaysOffWhenDisabled()
    {
        var payloads = ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { "net472", "net8.0" });

        Assert.Null(ModuleBuilder.ValidateReadyToRun(new ModuleReadyToRunOptions { Enabled = false }, payloads));
        var normalized = ModuleBuilder.ValidateReadyToRun(
            new ModuleReadyToRunOptions { Enabled = true, RuntimeIdentifiers = new[] { " Win-X64 ", "win-x64", "linux-musl-x64" } },
            payloads);

        Assert.NotNull(normalized);
        Assert.Equal(new[] { "win-x64", "linux-musl-x64" }, normalized!.RuntimeIdentifiers);
    }

    [Fact]
    public void Generate_WithReadyToRunPayloads_SelectsTheMatchingRuntimeBeforePortableBands()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-readytorun-" + Guid.NewGuid().ToString("N"));
        var libRoot = Path.Combine(root, "Lib");
        void AddPayload(string folder, string framework)
        {
            Directory.CreateDirectory(Path.Combine(libRoot, folder));
            File.Copy(typeof(ModuleReadyToRunTests).Assembly.Location, Path.Combine(libRoot, folder, "DemoModule.dll"), overwrite: true);
            File.Copy(typeof(ModuleReadyToRunTests).Assembly.Location, Path.Combine(libRoot, folder, "Demo.Dependency.dll"), overwrite: true);
            File.WriteAllText(Path.Combine(libRoot, folder, ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName), framework);
        }

        AddPayload("Core", "net8.0");
        AddPayload("Default", "net472");
        AddPayload(ModuleBinaryPayloadLayout.GetReadyToRunFolderName("osx-arm64"), "net10.0");
        AddPayload(ModuleBinaryPayloadLayout.GetReadyToRunFolderName("win-x64"), "net10.0");

        try
        {
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                targetFrameworks: new[] { "net472", "net8.0" });

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            var libraries = File.ReadAllText(Path.Combine(root, "DemoModule.Libraries.ps1"));

            var osxCondition = "[Environment]::Version -ge [version]'10.0' -and " +
                               "[System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier -eq 'osx-arm64'";
            var osxBranch = bootstrapper.IndexOf(osxCondition, StringComparison.Ordinal);
            var osxPath = bootstrapper.IndexOf("Path = 'Lib/Core-ReadyToRun-osx-arm64/DemoModule.dll'", StringComparison.Ordinal);
            var winPath = bootstrapper.IndexOf("Path = 'Lib/Core-ReadyToRun-win-x64/DemoModule.dll'", StringComparison.Ordinal);
            var portablePath = bootstrapper.IndexOf("Path = 'Lib/Core/DemoModule.dll'", StringComparison.Ordinal);
            var desktopPath = bootstrapper.IndexOf("Path = 'Lib/Default/DemoModule.dll'", StringComparison.Ordinal);

            // Each precompiled payload is its own branch; every other PowerShell 7 host falls through to the portable build.
            Assert.True(osxBranch >= 0 && osxBranch < osxPath && osxPath < winPath && winPath < portablePath && portablePath < desktopPath,
                bootstrapper);
            Assert.Contains("} else {", bootstrapper.Substring(winPath, portablePath - winPath), StringComparison.Ordinal);
            // The dependency list of a precompiled branch comes from the same payload, never from the portable one.
            Assert.Contains(osxCondition, libraries, StringComparison.Ordinal);
            Assert.Contains("Lib\\Core-ReadyToRun-osx-arm64\\Demo.Dependency.dll", libraries, StringComparison.Ordinal);
            Assert.Contains("Lib\\Core\\Demo.Dependency.dll", libraries, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_ScriptModuleWithCoreOnlyHelper_KeepsWindowsPowerShellOffTheReadyToRunPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-readytorun-helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Public"));
        File.WriteAllText(Path.Combine(root, "Public", "Get-HelperStatus.ps1"), "function Get-HelperStatus { 'script' }");
        foreach (var folder in new[] { "Core", ModuleBinaryPayloadLayout.GetReadyToRunFolderName("win-x64") })
        {
            Directory.CreateDirectory(Path.Combine(root, "Lib", folder));
            File.Copy(typeof(ModuleReadyToRunTests).Assembly.Location, Path.Combine(root, "Lib", folder, "DemoModule.dll"), overwrite: true);
        }
        File.WriteAllText(Path.Combine(root, "Lib", ModuleBinaryPayloadLayout.GetReadyToRunFolderName("win-x64"), ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName), "net10.0");

        try
        {
            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(new[] { "Get-HelperStatus" }, Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                targetFrameworks: new[] { "net8.0" });

            var bootstrapper = File.ReadAllText(Path.Combine(root, "DemoModule.psm1"));
            var modulesStart = bootstrapper.IndexOf("$BinaryModules = ", StringComparison.Ordinal);
            var modules = bootstrapper.Substring(modulesStart, bootstrapper.IndexOf("$BinaryModulePaths", modulesStart, StringComparison.Ordinal) - modulesStart);
            var desktopModules = modules.Substring(modules.LastIndexOf("} else {", StringComparison.Ordinal));
            // Windows PowerShell keeps the script functions and never loads the PowerShell 7 helper.
            Assert.DoesNotContain("DemoModule.dll", desktopModules, StringComparison.Ordinal);
            Assert.Contains("Path = 'Lib/Core-ReadyToRun-win-x64/DemoModule.dll'", bootstrapper, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidateConfiguredAssemblies_ChecksReadyToRunPayloadsLikeThePortableOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-readytorun-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
        Directory.CreateDirectory(Path.Combine(root, "Lib", ModuleBinaryPayloadLayout.GetReadyToRunFolderName("win-x64")));
        File.Copy(typeof(ModuleReadyToRunTests).Assembly.Location, Path.Combine(root, "Lib", "Core", "DemoModule.dll"), overwrite: true);

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ModuleBinaryExportSurfaceValidator.ValidateConfiguredAssemblies(root, "DemoModule", new[] { "DemoModule.dll" }));
            Assert.Contains("Core-ReadyToRun-win-x64", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PublishReadyToRun_PrecompilesProjectReferencesAndKeepsTheProjectsOwnTargetsHook()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "ReadyToRunPublish", Guid.NewGuid().ToString("N"));
        var dependencyDirectory = Directory.CreateDirectory(Path.Combine(root, "Dependency")).FullName;
        var moduleDirectory = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
        var runtimeIdentifier = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux") +
                                (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "-arm64" : "-x64");
        try
        {
            File.WriteAllText(Path.Combine(dependencyDirectory, "Dependency.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(dependencyDirectory, "Dependency.cs"),
                "namespace Dependency; public static class Value { public static string Text => \"dependency\"; }");
            var moduleProject = Path.Combine(moduleDirectory, "Module.csproj");
            File.WriteAllText(moduleProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><CustomAfterMicrosoftCommonTargets>$(MSBuildProjectDirectory)/Custom.After.targets</CustomAfterMicrosoftCommonTargets></PropertyGroup><ItemGroup><ProjectReference Include=\"../Dependency/Dependency.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(moduleDirectory, "Custom.After.targets"),
                "<Project><Target Name=\"PreserveCustomAfterHook\" AfterTargets=\"Publish\"><WriteLinesToFile File=\"$(PublishDir)custom-after-hook.txt\" Lines=\"custom hook preserved\" Overwrite=\"true\" /></Target></Project>");
            File.WriteAllText(Path.Combine(moduleDirectory, "Module.cs"),
                "namespace Module; public static class Value { public static string Text => Dependency.Value.Text; }");

            var published = new DotnetPublisher(new NullLogger()).PublishReadyToRun(
                moduleProject, "Release", "net10.0", new[] { runtimeIdentifier }, ModuleReadyToRunScope.ModuleAssemblies,
                "1.2.3", Path.Combine(root, "artifacts"), restoreSources: null);
            var publishDirectory = published[runtimeIdentifier];

            Assert.Equal("custom hook preserved", File.ReadAllText(Path.Combine(publishDirectory, "custom-after-hook.txt")).Trim());
            Assert.True(HasReadyToRunCode(Path.Combine(publishDirectory, "Module.dll")));
            Assert.True(HasReadyToRunCode(Path.Combine(publishDirectory, "Dependency.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        static bool HasReadyToRunCode(string path)
        {
            using var reader = new PEReader(File.OpenRead(path));
            return reader.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0;
        }
    }

    private sealed class NullLogger : ILogger
    {
        public bool IsVerbose => false;
        public void Info(string message) { }
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
