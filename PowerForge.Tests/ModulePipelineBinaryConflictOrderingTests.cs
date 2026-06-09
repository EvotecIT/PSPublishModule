using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModulePipelineBinaryConflictOrderingTests
{
    [Fact]
    public void Plan_ReordersRequiredModules_WhenPreferBinaryConflictOrderEnabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var alphaAssembly = BuildLibrary(root.FullName, "SharedAuth", "1.0.0", projectFolderName: "SharedAuth_Alpha");
            var betaAssembly = BuildLibrary(root.FullName, "SharedAuth", "2.0.0", projectFolderName: "SharedAuth_Beta");

            var alphaModuleBase = Directory.CreateDirectory(Path.Combine(root.FullName, "Alpha.Tools", "1.0.0", "bin"));
            File.Copy(alphaAssembly, Path.Combine(alphaModuleBase.FullName, "SharedAuth.dll"), overwrite: true);

            var betaModuleBase = Directory.CreateDirectory(Path.Combine(root.FullName, "Beta.Tools", "2.0.0", "bin"));
            File.Copy(betaAssembly, Path.Combine(betaModuleBase.FullName, "SharedAuth.dll"), overwrite: true);

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger, new StubPowerShellRunner(new Dictionary<string, string>
            {
                ["Alpha.Tools"] = alphaModuleBase.Parent!.FullName,
                ["Beta.Tools"] = betaModuleBase.Parent!.FullName
            }));

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Alpha.Tools",
                            RequiredVersion = "1.0.0"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Beta.Tools",
                            RequiredVersion = "2.0.0"
                        }
                    },
                    new ConfigurationImportModulesSegment
                    {
                        ImportModules = new ImportModulesConfiguration
                        {
                            RequiredModules = true,
                            PreferBinaryConflictOrder = true
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);

            Assert.Equal(new[] { "Beta.Tools", "Alpha.Tools" }, plan.RequiredModules.Select(static module => module.ModuleName).ToArray());
            Assert.Contains(logger.Infos, static message => message.Contains("PreferBinaryConflictOrder reordered RequiredModules", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName, string version)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);

        var psd1 = string.Join(Environment.NewLine, new[]
        {
            "@{",
            $"    RootModule = '{moduleName}.psm1'",
            $"    ModuleVersion = '{version}'",
            "    FunctionsToExport = @()",
            "    CmdletsToExport = @()",
            "    AliasesToExport = @()",
            "}"
        }) + Environment.NewLine;

        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psd1"), psd1);
    }

    private static string BuildLibrary(string rootPath, string assemblyName, string version, string projectFolderName)
    {
        var projectRoot = Directory.CreateDirectory(Path.Combine(rootPath, projectFolderName));
        var projectPath = Path.Combine(projectRoot.FullName, assemblyName + ".csproj");
        var sourcePath = Path.Combine(projectRoot.FullName, "Class1.cs");

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{{assemblyName}}</AssemblyName>
    <Version>{{version}}</Version>
    <AssemblyVersion>{{version}}.0</AssemblyVersion>
    <FileVersion>{{version}}.0</FileVersion>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(sourcePath, $$"""
namespace {{assemblyName}}Lib;

public sealed class Marker
{
}
""");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -nologo --verbosity quiet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot.FullName
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"dotnet build failed for test fixture.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

        var assemblyPath = Path.Combine(projectRoot.FullName, "bin", "Release", "net8.0", assemblyName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Built assembly not found: {assemblyPath}");
        return assemblyPath;
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, string> _moduleBasePaths;

        public StubPowerShellRunner(IReadOnlyDictionary<string, string> moduleBasePaths)
        {
            _moduleBasePaths = moduleBasePaths;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var lines = _moduleBasePaths
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static item =>
                    "PFMODINFO::ITEM::" +
                    Encode(item.Key) + "::" +
                    Encode(string.Empty) + "::" +
                    Encode(string.Empty) + "::" +
                    Encode(item.Value));

            return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
        }

        private static string Encode(string value)
            => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Successes { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> VerboseMessages { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message) => Infos.Add(message ?? string.Empty);
        public void Success(string message) => Successes.Add(message ?? string.Empty);
        public void Warn(string message) => Warnings.Add(message ?? string.Empty);
        public void Error(string message) => Errors.Add(message ?? string.Empty);
        public void Verbose(string message) => VerboseMessages.Add(message ?? string.Empty);
    }
}
