using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineRegressionParityTests
{
    [Fact]
    public void Plan_MirrorsExternalModulesToRequiredModules_ButKeepsPackagingListEmpty()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.PowerShell.Utility"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.PowerShell.Management"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.PowerShell.Diagnostics"
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.Contains("Microsoft.PowerShell.Utility", plan.ExternalModuleDependencies, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Microsoft.PowerShell.Management", plan.ExternalModuleDependencies, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Microsoft.PowerShell.Diagnostics", plan.ExternalModuleDependencies, StringComparer.OrdinalIgnoreCase);

            Assert.Contains(plan.RequiredModules, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.RequiredModules, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Management", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.RequiredModules, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Diagnostics", StringComparison.OrdinalIgnoreCase));

            Assert.DoesNotContain(plan.RequiredModulesForPackaging, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(plan.RequiredModulesForPackaging, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Management", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(plan.RequiredModulesForPackaging, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Diagnostics", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_WritesExternalModulesToRequiredModulesAndPsData()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.PowerShell.Utility"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.PowerShell.Management"
                        }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ExternalModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Microsoft.PowerShell.Diagnostics"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.True(ManifestEditor.TryGetRequiredModules(result.BuildResult.ManifestPath, out var required));
            Assert.NotNull(required);
            Assert.Contains(required!, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(required!, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Management", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(required!, m => string.Equals(m.ModuleName, "Microsoft.PowerShell.Diagnostics", StringComparison.OrdinalIgnoreCase));

            Assert.True(ManifestEditor.TryGetPsDataStringArray(result.BuildResult.ManifestPath, "ExternalModuleDependencies", out var external));
            Assert.NotNull(external);
            Assert.Contains("Microsoft.PowerShell.Utility", external!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Microsoft.PowerShell.Management", external!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Microsoft.PowerShell.Diagnostics", external!, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Plan_EmitsGuidAutoWarningOnlyOnce_WhenPackagingAndManifestDraftsMatch()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            const string missingModuleName = "PSEventViewer-Missing-For-Tests";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                            ModuleName = missingModuleName,
                            ModuleVersion = "1.0.0",
                            Guid = "Auto"
                        }
                    }
                }
            };

            var logger = new CollectingLogger();
            var plan = new ModulePipelineRunner(logger).Plan(spec);

            var guidWarnings = logger.Warnings
                .Where(w => w.Contains("RequiredModules set Guid=Auto but module not installed", StringComparison.OrdinalIgnoreCase))
                .Where(w => w.Contains(missingModuleName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.Single(guidWarnings);
            Assert.False(ReferenceEquals(plan.RequiredModules, plan.RequiredModulesForPackaging));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void MissingFunctionsAnalyzer_DoesNotTreatScriptBlockAsCommand()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = "& { process { if ($_.TrustedDomain -eq $Trust.Target ) { $_ } } }";

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, c =>
            !string.IsNullOrWhiteSpace(c.Name) &&
            c.Name.Contains("process { if ($_.TrustedDomain -eq $Trust.Target ) { $_ } }", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_PersistsCommandModuleDependencies_FromConfigurationCommand()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationCommandSegment
                    {
                        Configuration = new CommandConfiguration
                        {
                            ModuleName = "ActiveDirectory",
                            CommandName = new[] { "Get-ADUser", "Get-ADGroup" }
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            var result = runner.Run(spec, plan);

            Assert.True(plan.CommandModuleDependencies.TryGetValue("ActiveDirectory", out var activeDirectoryDeps));
            Assert.Contains("Get-ADUser", activeDirectoryDeps, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Get-ADGroup", activeDirectoryDeps, StringComparer.OrdinalIgnoreCase);

            var manifest = File.ReadAllText(result.BuildResult.ManifestPath);
            Assert.Contains("CommandModuleDependencies", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ActiveDirectory", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Get-ADUser", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Get-ADGroup", manifest, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateMissingFunctions_InfersLikelyModule_FromCommandModuleDependencies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationCommandSegment
                    {
                        Configuration = new CommandConfiguration
                        {
                            ModuleName = "ActiveDirectory",
                            CommandName = new[] { "Get-ADUser" }
                        }
                    }
                }
            };

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger);
            var plan = runner.Plan(spec);
            var report = new MissingFunctionsReport(
                summary: new[] { new MissingFunctionCommand("Get-ADUser", string.Empty, string.Empty, false, false, string.Empty, null) },
                summaryFiltered: Array.Empty<MissingFunctionCommand>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());

            InvokeValidateMissingFunctions(runner, report, plan);

            Assert.Contains(logger.Errors, e =>
                e.Contains("Get-ADUser", StringComparison.OrdinalIgnoreCase) &&
                e.Contains("ActiveDirectory", StringComparison.OrdinalIgnoreCase) &&
                e.Contains("CommandModuleDependencies", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("Get-DnsServerZone", "DnsServer")]
    [InlineData("Get-DhcpServerv4Scope", "DhcpServer")]
    [InlineData("Get-ADDomain", "ActiveDirectory")]
    public void ValidateMissingFunctions_InfersLikelyModule_FromKnownCommandPatterns(string commandName, string expectedModule)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger);
            var plan = runner.Plan(spec);
            var report = new MissingFunctionsReport(
                summary: new[] { new MissingFunctionCommand(commandName, string.Empty, string.Empty, false, false, string.Empty, null) },
                summaryFiltered: Array.Empty<MissingFunctionCommand>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());

            InvokeValidateMissingFunctions(runner, report, plan);

            Assert.Contains(logger.Errors, e =>
                e.Contains(commandName, StringComparison.OrdinalIgnoreCase) &&
                e.Contains(expectedModule, StringComparison.OrdinalIgnoreCase) &&
                e.Contains("command pattern", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateMissingFunctions_HonorsIgnoreModule_ForInferredModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

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
                    new ConfigurationModuleSkipSegment
                    {
                        Configuration = new ModuleSkipConfiguration
                        {
                            IgnoreModuleName = new[] { "ActiveDirectory" }
                        }
                    }
                }
            };

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger);
            var plan = runner.Plan(spec);
            var report = new MissingFunctionsReport(
                summary: new[] { new MissingFunctionCommand("Get-ADUser", string.Empty, string.Empty, false, false, string.Empty, null) },
                summaryFiltered: Array.Empty<MissingFunctionCommand>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());

            InvokeValidateMissingFunctions(runner, report, plan);

            Assert.DoesNotContain(logger.Errors, e => e.Contains("Get-ADUser", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Warnings, w =>
                w.Contains("Get-ADUser", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("ActiveDirectory", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("ignored by configuration", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateMissingFunctions_IgnoresScriptBlockLikeUnresolvedTokens()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            };

            var logger = new CollectingLogger();
            var runner = new ModulePipelineRunner(logger);
            var plan = runner.Plan(spec);
            var report = new MissingFunctionsReport(
                summary: new[] { new MissingFunctionCommand("{ process { if ($_.TrustedDomain -eq $Trust.Target ) { $_ } } }", string.Empty, string.Empty, false, false, string.Empty, null) },
                summaryFiltered: Array.Empty<MissingFunctionCommand>(),
                functions: Array.Empty<string>(),
                functionsTopLevelOnly: Array.Empty<string>());

            InvokeValidateMissingFunctions(runner, report, plan);

            Assert.DoesNotContain(logger.Errors, e => e.Contains("TrustedDomain", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("Missing commands detected during merge", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AreRequiredModuleDraftListsEquivalent_AllowsDuplicateEntries_WhenMultisetMatches()
    {
        var left = new (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[]
        {
            ("ModuleA", "1.0.0", (string?)null, (string?)null, (string?)null),
            ("ModuleA", "2.0.0", (string?)null, (string?)null, (string?)null),
            ("ModuleB", "1.0.0", (string?)null, (string?)null, (string?)null)
        };
        var right = new (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[]
        {
            ("ModuleB", "1.0.0", (string?)null, (string?)null, (string?)null),
            ("ModuleA", "2.0.0", (string?)null, (string?)null, (string?)null),
            ("ModuleA", "1.0.0", (string?)null, (string?)null, (string?)null)
        };

        Assert.True(InvokeAreRequiredModuleDraftListsEquivalent(left, right));
    }

    [Fact]
    public void AreRequiredModuleDraftListsEquivalent_ReturnsFalse_WhenDuplicateEntriesDiffer()
    {
        var left = new (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[]
        {
            ("ModuleA", "1.0.0", (string?)null, (string?)null, (string?)null),
            ("ModuleA", "2.0.0", (string?)null, (string?)null, (string?)null)
        };
        var right = new (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[]
        {
            ("ModuleA", "1.0.0", (string?)null, (string?)null, (string?)null),
            ("ModuleA", "1.0.0", (string?)null, (string?)null, (string?)null)
        };

        Assert.False(InvokeAreRequiredModuleDraftListsEquivalent(left, right));
    }

    [Fact]
    public void AreRequiredModuleDraftListsEquivalent_TreatsNullAndEmptyVersionFieldsAsEqual()
    {
        var left = new (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[]
        {
            ("ModuleA", (string?)null, (string?)null, (string?)null, (string?)null)
        };
        var right = new (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[]
        {
            ("ModuleA", string.Empty, "  ", string.Empty, " ")
        };

        Assert.True(InvokeAreRequiredModuleDraftListsEquivalent(left, right));
    }

    [Fact]
    public void Plan_ResolvesLegacyFlatHandling_FromBuildSegment_WhenInstallOptionsDoNotOverride()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = false,
                    LegacyFlatHandling = null,
                    PreserveVersions = null
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            LegacyFlatHandling = LegacyFlatModuleHandling.Convert,
                            PreserveInstallVersions = new[] { "2.0.26", "2.0.27" }
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.Equal(LegacyFlatModuleHandling.Convert, plan.InstallLegacyFlatHandling);
            Assert.Contains("2.0.26", plan.InstallPreserveVersions, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("2.0.27", plan.InstallPreserveVersions, StringComparer.OrdinalIgnoreCase);
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

    private static void InvokeValidateMissingFunctions(
        ModulePipelineRunner runner,
        MissingFunctionsReport report,
        ModulePipelinePlan plan,
        IReadOnlyCollection<string>? dependentModules = null)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("ValidateMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "ValidateMissingFunctions method signature may have changed.");
        method!.Invoke(runner, new object?[] { report, plan, dependentModules });
    }

    private static bool InvokeAreRequiredModuleDraftListsEquivalent(
        (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[] left,
        (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[] right)
    {
        var runnerType = typeof(ModulePipelineRunner);
        var draftType = runnerType.GetNestedType("RequiredModuleDraft", BindingFlags.NonPublic);
        Assert.True(draftType is not null, "RequiredModuleDraft nested type may have changed.");

        var method = runnerType.GetMethod("AreRequiredModuleDraftListsEquivalent", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(method is not null, "AreRequiredModuleDraftListsEquivalent method signature may have changed.");

        var leftArray = CreateDraftArray(draftType!, left);
        var rightArray = CreateDraftArray(draftType!, right);

        return (bool)method!.Invoke(null, new object?[] { leftArray, rightArray })!;
    }

    private static Array CreateDraftArray(
        Type draftType,
        (string ModuleName, string? ModuleVersion, string? MinimumVersion, string? RequiredVersion, string? Guid)[] items)
    {
        var result = Array.CreateInstance(draftType, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var entry = items[i];
            var instance = Activator.CreateInstance(
                draftType,
                entry.ModuleName,
                entry.ModuleVersion,
                entry.MinimumVersion,
                entry.RequiredVersion,
                entry.Guid);
            result.SetValue(instance, i);
        }

        return result;
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
