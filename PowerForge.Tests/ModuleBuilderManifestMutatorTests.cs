using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModuleBuilderManifestMutatorTests
{
    [Fact]
    public void MergeDeclaredAliases_PreservesScriptAliasesAndAddsDetectedBinaryAliases()
    {
        var aliases = ModuleBuilder.MergeDeclaredAliases(
            new[] { "New-HeroImage", "TeamsMessage" },
            new[] { "TeamsMessage", "TeamsSection" });

        Assert.Equal(new[] { "New-HeroImage", "TeamsMessage", "TeamsSection" }, aliases);
    }

    [Fact]
    public void MergeDeclaredAliases_CombinesOnlyProvenScriptAndCurrentBinaryAliases()
    {
        var aliases = ModuleBuilder.MergeDeclaredAliases(
            new[] { "ScriptAlias" },
            new[] { "CurrentAlias" });

        Assert.Equal(new[] { "ScriptAlias", "CurrentAlias" }, aliases);
    }

    [Fact]
    public void BuildInPlace_UsesManifestMutatorForManifestUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "TestModule";
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "Public"));
            File.WriteAllText(Path.Combine(root, "Public", "Install-TestModule.ps1"), "function Install-TestModule { }");

            var mutator = new RecordingManifestMutator();
            var scriptDetector = new RecordingScriptFunctionExportDetector("Install-TestModule");
            var builder = new ModuleBuilder(new NullLogger(), mutator, scriptDetector);

            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
                Author = "Author",
                CompanyName = "Company",
                Description = "Description",
                CompatiblePSEditions = new[] { "Desktop", "Core" },
                Tags = new[] { "TagOne", "TagTwo" },
                IconUri = "https://example.com/icon.png",
                ProjectUri = "https://example.com/project"
            });

            Assert.Contains(mutator.TopLevelVersionWrites, static write => write.NewVersion == "2.0.0");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "RootModule" && write.Value == "TestModule.psm1");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "Author" && write.Value == "Author");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "CompanyName" && write.Value == "Company");
            Assert.Contains(mutator.TopLevelStringWrites, static write => write.Key == "Description" && write.Value == "Description");
            Assert.Contains(mutator.TopLevelStringArrayWrites, static write => write.Key == "CompatiblePSEditions" && write.Values.SequenceEqual(new[] { "Desktop", "Core" }));
            Assert.Contains(mutator.PsDataStringArrayWrites, static write => write.Key == "Tags" && write.Values.SequenceEqual(new[] { "TagOne", "TagTwo" }));
            Assert.Contains(mutator.PsDataStringWrites, static write => write.Key == "IconUri" && write.Value == "https://example.com/icon.png");
            Assert.Contains(mutator.PsDataStringWrites, static write => write.Key == "ProjectUri" && write.Value == "https://example.com/project");
            Assert.Contains(mutator.TopLevelStringArrayWrites, static write => write.Key == "FunctionsToExport");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_DoesNotExportGeneratedDevelopmentBinaryHelper()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "TestModule";
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psd1"), "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), "function Import-TestModuleDevelopmentBinaryModule { } function Invoke-TestModule { }");

            var mutator = new RecordingManifestMutator();
            var scriptDetector = new RecordingScriptFunctionExportDetector(
                "Import-TestModuleDevelopmentBinaryModule",
                "Invoke-TestModule");
            var builder = new ModuleBuilder(new NullLogger(), mutator, scriptDetector);

            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0"
            });

            var exportWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "FunctionsToExport");
            Assert.Equal(new[] { "Invoke-TestModule" }, exportWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_PreservesAliasesDeclaredInPrivateScripts()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PSPublishModule";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PSPublishModule.psm1'; CmdletsToExport = @(); AliasesToExport = @('StaleBinaryAlias') }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            var privateRoot = Directory.CreateDirectory(Path.Combine(root, "Private"));
            File.WriteAllText(Path.Combine(privateRoot.FullName, "Aliases.ps1"), "Set-Alias -Name ScriptAlias -Value Invoke-ScriptAlias");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(PSPublishModule.NewConfigurationBuildCommand).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("ScriptAlias", aliasWrite.Values);
            Assert.DoesNotContain("StaleBinaryAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_PreservesManifestAliasesWhenScriptAliasSetIsIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PSPublishModule";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PSPublishModule.psm1'; CmdletsToExport = @(); AliasesToExport = @('DynamicScriptAlias') }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            var privateRoot = Directory.CreateDirectory(Path.Combine(root, "Private"));
            File.WriteAllText(
                Path.Combine(privateRoot.FullName, "Aliases.ps1"),
                "$name = Get-DynamicAliasName; Set-Alias -Name $name -Value Invoke-DynamicAlias");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(PSPublishModule.NewConfigurationBuildCommand).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("DynamicScriptAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_DetectsAliasesFromEveryBootstrapperScriptFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PSPublishModule";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PSPublishModule.psm1'; CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), "Set-Alias -Name RootAlias -Value Get-Root");

            foreach (var folder in new[] { "Classes", "Enums", "Private", "Public" })
            {
                var scriptRoot = Directory.CreateDirectory(Path.Combine(root, folder, "Nested"));
                File.WriteAllText(
                    Path.Combine(scriptRoot.FullName, "Aliases.ps1"),
                    $"Set-Alias -Name {folder}Alias -Value Get-{folder}");
            }

            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(PSPublishModule.NewConfigurationBuildCommand).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("RootAlias", aliasWrite.Values);
            Assert.Contains("ClassesAlias", aliasWrite.Values);
            Assert.Contains("EnumsAlias", aliasWrite.Values);
            Assert.Contains("PrivateAlias", aliasWrite.Values);
            Assert.Contains("PublicAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_AnalyzesAliasesInBootstrapperExecutionOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            var enumsRoot = Directory.CreateDirectory(Path.Combine(root, "Enums"));
            File.WriteAllText(
                Path.Combine(enumsRoot.FullName, "Aliases.ps1"),
                "Set-Alias -Name TemporaryAlias -Value Get-Item");
            var classesRoot = Directory.CreateDirectory(Path.Combine(root, "Classes"));
            File.WriteAllText(
                Path.Combine(classesRoot.FullName, "Aliases.ps1"),
                "Remove-Alias -Name TemporaryAlias");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.DoesNotContain("TemporaryAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_AnalyzesScriptsInOrdinalPathOrderWithinFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            var privateRoot = Directory.CreateDirectory(Path.Combine(root, "Private"));
            var creationPath = Path.Combine(privateRoot.FullName, "01-Create.ps1");
            var removalPath = Path.Combine(privateRoot.FullName, "02-Remove.ps1");
            File.WriteAllText(creationPath, "Set-Alias -Name TemporaryAlias -Value Get-Item");
            File.WriteAllText(removalPath, "Remove-Alias -Name TemporaryAlias");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            IEnumerable<string> EnumerateScripts(string directory)
            {
                if (Path.GetFileName(directory).Equals("Private", StringComparison.OrdinalIgnoreCase))
                    return new[] { removalPath, creationPath };
                return Directory.EnumerateFiles(directory, "*.ps1", SearchOption.AllDirectories);
            }

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(
                new NullLogger(),
                mutator,
                new PowerShellScriptFunctionExportDetector(),
                EnumerateScripts);
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.DoesNotContain("TemporaryAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_PreservesAliasesExportedByNestedModules()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; NestedModules = @('Nested.psm1'); CmdletsToExport = @(); AliasesToExport = @('NestedAlias') }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            File.WriteAllText(
                Path.Combine(root, "Nested.psm1"),
                "Set-Alias -Name NestedAlias -Value Get-Item");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("NestedAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_PreservesAliasesFromImmediatelyInvokedScriptBlocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; CmdletsToExport = @(); AliasesToExport = @('InvokedAlias') }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            var privateRoot = Directory.CreateDirectory(Path.Combine(root, "Private"));
            File.WriteAllText(
                Path.Combine(privateRoot.FullName, "Aliases.ps1"),
                "1 | ForEach-Object { Set-Alias -Scope Script InvokedAlias Get-Item }");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("InvokedAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_IgnoresGeneratedAliasBridgeWhenRefreshingBinaryAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; CmdletsToExport = @(); AliasesToExport = @('RemovedBinaryAlias') }");
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psm1"),
                "# PowerForge bootstrapper\n# Auto-generated by PowerForge. Do not edit.\nSet-Alias -Name $Alias.Name -Value $AliasTarget");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Empty(aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_PreservesExportsWhenScriptDiscoveryIsIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; FunctionsToExport = @('ExistingFunction'); CmdletsToExport = @(); AliasesToExport = @('ExistingAlias') }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "Classes"));
            Directory.CreateDirectory(Path.Combine(root, "Public"));
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            IEnumerable<string> EnumerateScripts(string directory)
            {
                var folder = Path.GetFileName(directory);
                if (folder is "Classes" or "Public")
                    throw new IOException("Simulated recursive discovery failure.");
                return Directory.EnumerateFiles(directory, "*.ps1", SearchOption.AllDirectories);
            }

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(
                new NullLogger(),
                mutator,
                new PowerShellScriptFunctionExportDetector(),
                EnumerateScripts);
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            Assert.DoesNotContain(mutator.TopLevelStringArrayWrites, static write => write.Key == "FunctionsToExport");
            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("ExistingAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_RemovesReplacedRootExportsWhenFolderDiscoveryIsIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; FunctionsToExport = @('Invoke-RootOnly', 'Invoke-PublicExisting'); CmdletsToExport = @(); AliasesToExport = @('RootAlias', 'ExistingAlias') }");
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psm1"),
                "function Invoke-RootOnly { 'root' }; Set-Alias -Name RootAlias -Value Invoke-RootOnly");
            Directory.CreateDirectory(Path.Combine(root, "Classes"));
            Directory.CreateDirectory(Path.Combine(root, "Public"));
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            IEnumerable<string> EnumerateScripts(string directory)
            {
                var folder = Path.GetFileName(directory);
                if (folder is "Classes" or "Public")
                    throw new IOException("Simulated recursive discovery failure.");
                return Directory.EnumerateFiles(directory, "*.ps1", SearchOption.AllDirectories);
            }

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(
                new NullLogger(),
                mutator,
                new PowerShellScriptFunctionExportDetector(),
                EnumerateScripts);
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
                RootModuleScriptWillBeReplaced = true,
            });

            var functionWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "FunctionsToExport");
            Assert.Equal(new[] { "Invoke-PublicExisting" }, functionWrite.Values);
            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("ExistingAlias", aliasWrite.Values);
            Assert.DoesNotContain("RootAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_PreservesRootAliasesForFunctionOnlyCustomDetector()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @('ExistingAlias') }");
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psm1"),
                "Set-Alias -Name ExistingAlias -Value Get-Item");
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(
                new NullLogger(),
                mutator,
                new RecordingScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
                RootModuleScriptWillBeReplaced = true,
            });

            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Contains("ExistingAlias", aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void BuildInPlace_ClearsStaleBinaryExportsWhenCurrentAssemblyExportsNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string moduleName = "PowerForge";
            File.WriteAllText(
                Path.Combine(root, $"{moduleName}.psd1"),
                "@{ ModuleVersion = '1.0.0'; RootModule = 'PowerForge.psm1'; CmdletsToExport = @('Get-Stale'); AliasesToExport = @('StaleAlias') }");
            File.WriteAllText(Path.Combine(root, $"{moduleName}.psm1"), string.Empty);
            var libCore = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core"));
            File.Copy(
                typeof(ModuleBuilder).Assembly.Location,
                Path.Combine(libCore.FullName, moduleName + ".dll"),
                overwrite: true);

            var mutator = new RecordingManifestMutator();
            var builder = new ModuleBuilder(new NullLogger(), mutator, new PowerShellScriptFunctionExportDetector());
            builder.BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = root,
                ModuleName = moduleName,
                ModuleVersion = "2.0.0",
            });

            var cmdletWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "CmdletsToExport");
            var aliasWrite = Assert.Single(mutator.TopLevelStringArrayWrites, static write => write.Key == "AliasesToExport");
            Assert.Empty(cmdletWrite.Values);
            Assert.Empty(aliasWrite.Values);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private sealed class RecordingManifestMutator : IModuleManifestMutator
    {
        public List<(string FilePath, string NewVersion)> TopLevelVersionWrites { get; } = new();
        public List<(string FilePath, string Key, string Value)> TopLevelStringWrites { get; } = new();
        public List<(string FilePath, string Key, string[] Values)> TopLevelStringArrayWrites { get; } = new();
        public List<(string FilePath, string Key, string Value)> PsDataStringWrites { get; } = new();
        public List<(string FilePath, string Key, string[] Values)> PsDataStringArrayWrites { get; } = new();

        public bool TrySetTopLevelModuleVersion(string filePath, string newVersion)
        {
            TopLevelVersionWrites.Add((filePath, newVersion));
            return true;
        }

        public bool TrySetTopLevelString(string filePath, string key, string newValue)
        {
            TopLevelStringWrites.Add((filePath, key, newValue));
            return true;
        }

        public bool TrySetTopLevelStringArray(string filePath, string key, string[] values)
        {
            TopLevelStringArrayWrites.Add((filePath, key, values ?? Array.Empty<string>()));
            return true;
        }

        public bool TrySetPsDataString(string filePath, string key, string value)
        {
            PsDataStringWrites.Add((filePath, key, value));
            return true;
        }

        public bool TrySetPsDataStringArray(string filePath, string key, string[] values)
        {
            PsDataStringArrayWrites.Add((filePath, key, values ?? Array.Empty<string>()));
            return true;
        }

        public bool TrySetPsDataBool(string filePath, string key, bool value) => true;
        public bool TryRemoveTopLevelKey(string filePath, string key) => true;
        public bool TryRemovePsDataKey(string filePath, string key) => true;
        public bool TrySetRequiredModules(string filePath, RequiredModuleReference[] modules) => true;
        public bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value) => true;
        public bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values) => true;
        public bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value) => true;
        public bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> values) => true;
        public bool TrySetManifestExports(string filePath, string[]? functions, string[]? cmdlets, string[]? aliases) => true;
        public bool TrySetRepository(string filePath, string? branch, string[]? paths) => true;
    }

    private sealed class RecordingScriptFunctionExportDetector : IScriptFunctionExportDetector
    {
        private readonly string[] _functions;

        public RecordingScriptFunctionExportDetector(params string[] functions)
        {
            _functions = functions ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
            => _functions;

    }
}
