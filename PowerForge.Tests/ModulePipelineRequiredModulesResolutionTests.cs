using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineRequiredModulesResolutionTests
{
    private const string InstalledModuleInfoMarker = "PFMODINFO::ITEM::";
    private const string RepositoryInfoMarker = "PFPSRG::ITEM::";

    [Fact]
    public void Plan_ResolvesGuidAutoFromRepository_WhenModuleIsNotInstalled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSSharedGoods";
            const string dependencyGuid = "11111111-2222-3333-4444-555555555555";

            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = dependencyName,
                            ModuleVersion = "0.25.0",
                            Guid = "Auto"
                        }
                    }
                }
            };

            var logger = new CollectingLogger();
            var runner = new StubPowerShellRunner(
                installedModules: new Dictionary<string, (string? Version, string? Guid, string? ModuleBasePath)>(StringComparer.OrdinalIgnoreCase),
                repositoryModules: new Dictionary<string, (string Version, string Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("0.30.0", dependencyGuid)
                });

            var plan = new ModulePipelineRunner(logger, runner).Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Equal(dependencyName, required.ModuleName);
            Assert.Equal("0.25.0", required.ModuleVersion);
            Assert.Equal(dependencyGuid, required.Guid);
            Assert.DoesNotContain(logger.Warnings, warning =>
                warning.Contains("Guid=Auto", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains(dependencyName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_ResolvesGuidAutoFromRepository_WhenVersionIsInstalledButGuidIsMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string dependencyName = "PSSharedGoods";
            const string dependencyGuid = "66666666-7777-8888-9999-000000000000";

            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0"
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = dependencyName,
                            ModuleVersion = "0.25.0",
                            Guid = "Auto"
                        }
                    }
                }
            };

            var logger = new CollectingLogger();
            var runner = new StubPowerShellRunner(
                installedModules: new Dictionary<string, (string? Version, string? Guid, string? ModuleBasePath)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("0.25.0", null, @"C:\Modules\PSSharedGoods\0.25.0")
                },
                repositoryModules: new Dictionary<string, (string Version, string Guid)>(StringComparer.OrdinalIgnoreCase)
                {
                    [dependencyName] = ("0.30.0", dependencyGuid)
                });

            var plan = new ModulePipelineRunner(logger, runner).Plan(spec);

            var required = Assert.Single(plan.RequiredModules);
            Assert.Equal("0.25.0", required.ModuleVersion);
            Assert.Equal(dependencyGuid, required.Guid);
            Assert.DoesNotContain(logger.Warnings, warning =>
                warning.Contains("Guid=Auto", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains(dependencyName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static void WriteMinimalModule(string rootPath, string moduleName, string moduleVersion)
    {
        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psm1"), "function Test-Example { 'ok' }");
        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psd1"),
            "@{" + Environment.NewLine +
            "    RootModule = '" + moduleName + ".psm1'" + Environment.NewLine +
            "    ModuleVersion = '" + moduleVersion + "'" + Environment.NewLine +
            "    GUID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'" + Environment.NewLine +
            "    Author = 'Tests'" + Environment.NewLine +
            "    CompanyName = 'Tests'" + Environment.NewLine +
            "    Description = 'Test module'" + Environment.NewLine +
            "    FunctionsToExport = @('*')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    VariablesToExport = @('*')" + Environment.NewLine +
            "    AliasesToExport = @('*')" + Environment.NewLine +
            "}");
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, (string? Version, string? Guid, string? ModuleBasePath)> _installedModules;
        private readonly IReadOnlyDictionary<string, (string Version, string Guid)> _repositoryModules;

        public StubPowerShellRunner(
            IReadOnlyDictionary<string, (string? Version, string? Guid, string? ModuleBasePath)> installedModules,
            IReadOnlyDictionary<string, (string Version, string Guid)> repositoryModules)
        {
            _installedModules = installedModules;
            _repositoryModules = repositoryModules;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var script = File.ReadAllText(request.ScriptPath);

            if (script.Contains(InstalledModuleInfoMarker, StringComparison.Ordinal))
            {
                var names = DecodeLines(request.Arguments[0]);
                var lines = names.Select(name =>
                {
                    _installedModules.TryGetValue(name, out var module);
                    return InstalledModuleInfoMarker +
                           Encode(name) + "::" +
                           Encode(module.Version ?? string.Empty) + "::" +
                           Encode(module.Guid ?? string.Empty) + "::" +
                           Encode(module.ModuleBasePath ?? string.Empty);
                });

                return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
            }

            if (script.Contains(RepositoryInfoMarker, StringComparison.Ordinal))
            {
                var names = DecodeLines(request.Arguments[0]);
                var lines = names
                    .Where(name => _repositoryModules.ContainsKey(name))
                    .Select(name =>
                    {
                        var module = _repositoryModules[name];
                        return RepositoryInfoMarker +
                               Encode(name) + "::" +
                               Encode(module.Version) + "::" +
                               Encode("PSGallery") + "::" +
                               Encode(string.Empty) + "::" +
                               Encode(string.Empty) + "::" +
                               Encode(module.Guid);
                    });

                return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
            }

            throw new InvalidOperationException("Unexpected script invocation in test.");
        }

        private static string[] DecodeLines(string value)
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return text
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0)
                .ToArray();
        }

        private static string Encode(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool IsVerbose => false;

        public void Info(string message) => Infos.Add(message ?? string.Empty);
        public void Success(string message) { }
        public void Warn(string message) => Warnings.Add(message ?? string.Empty);
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
