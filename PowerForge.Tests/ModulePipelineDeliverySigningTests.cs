using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDeliverySigningTests
{
    [Fact]
    public void Plan_DeliverySign_EnablesSigningAndIncludesInternals()
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
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                Sign = true,
                                InternalsPath = "Assets"
                            },
                            Signing = new SigningOptionsConfiguration
                            {
                                CertificateThumbprint = "ABC123",
                                IncludeInternals = false,
                                ExcludePaths = new[] { "Assets", "Modules" }
                            }
                        }
                    }
                }
            };

            var plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);

            Assert.True(plan.SignModule);
            Assert.NotNull(plan.Signing);
            Assert.True(plan.Signing!.IncludeInternals);
            Assert.Equal("ABC123", plan.Signing.CertificateThumbprint);
            Assert.DoesNotContain("Assets", plan.Signing.ExcludePaths!);
            Assert.Contains("Modules", plan.Signing.ExcludePaths!);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Run_DeliverySign_WithoutCertificate_ThrowsClearError()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var internalsDir = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals"));
            File.WriteAllText(Path.Combine(internalsDir.FullName, "helper.ps1"), "function Get-Helper { 'ok' }");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            SignMerged = false
                        }
                    },
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                Sign = true,
                                InternalsPath = "Internals"
                            },
                            Signing = new SigningOptionsConfiguration()
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);

            var ex = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, plan));
            Assert.Contains("no signing certificate was configured", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string root, string moduleName, string version)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, $"{moduleName}.psd1"), $"@{{ ModuleVersion = '{version}'; RootModule = '{moduleName}.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }}");
        File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
    }
}
