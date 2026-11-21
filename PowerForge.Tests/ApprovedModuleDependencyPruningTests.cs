using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using Xunit;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ApprovedModuleDependencyPruningTests
{
    [Theory]
    [InlineData("$script:SharedSetting='shared'; Export-ModuleMember -Variable SharedSetting", "$SharedSetting")]
    [InlineData("$script:SharedSetting='shared'; function Get-SharedHelper { 'shared' }; Export-ModuleMember -Function Get-SharedHelper -Variable SharedSetting", "Get-SharedHelper; $SharedSetting")]
    [InlineData("function Get-SharedHelper { 'shared' }; Set-Alias SharedAlias Get-SharedHelper; Export-ModuleMember -Function Get-SharedHelper -Alias SharedAlias", "Get-Alias SharedAlias")]
    public void Analyze_PreservesDonorsWithExportedRuntimeState(string body, string code)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "SharedHelpers", body);
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, code,
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Analyze_PreservesBinaryRuntimeSurfaceWithoutCommandReferences(bool nested, bool required)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var initialState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
            initialState.AuthorizationManager = new System.Management.Automation.AuthorizationManager("PowerForge.Tests");
            using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialState);
            runspace.Open();
            using var ps = System.Management.Automation.PowerShell.Create();
            ps.Runspace = runspace;
            var command = Assert.IsAssignableFrom<System.Management.Automation.CmdletInfo>(
                Assert.Single(ps.AddCommand("Get-Command").AddParameter("Name", "Get-Item").Invoke()).BaseObject);
            string binary = command.ImplementingType.Assembly.Location.Replace("'", "''");
            WriteModule(root, "SharedHelpers", "Export-ModuleMember");
            string providerManifest = Path.Combine(root, "SharedHelpers", "Provider.psd1");
            File.WriteAllText(providerManifest, "@{RootModule='" + binary + "';ModuleVersion='1.0.0'}");
            File.WriteAllText(Path.Combine(root, "SharedHelpers", "SharedHelpers.psd1"), required
                ? "@{RootModule='SharedHelpers.psm1';ModuleVersion='1.0.0';RequiredModules=@('" + providerManifest.Replace("'", "''") + "')}"
                : nested
                ? "@{RootModule='SharedHelpers.psm1';ModuleVersion='1.0.0';NestedModules=@('" + binary + "')}"
                : "@{RootModule='" + binary + "';ModuleVersion='1.0.0'}");
            ps.Commands.Clear();
            var imported = Assert.IsType<System.Management.Automation.PSModuleInfo>(Assert.Single(
                ps.AddCommand("Import-Module").AddParameter("Name", Path.Combine(root, "SharedHelpers", "SharedHelpers.psd1"))
                    .AddParameter("PassThru").AddParameter("Force").AddParameter("ErrorAction", "Stop").Invoke()).BaseObject);
            Assert.Equal("SharedHelpers", imported.Name);
            Assert.True(imported.ModuleType == System.Management.Automation.ModuleType.Binary ||
                imported.NestedModules.Concat(imported.RequiredModules).Any(module => module.ModuleType == System.Management.Automation.ModuleType.Binary));
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "Get-Item Demo:\\x",
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("(Get-Module 'SharedHelpers').Version")]
    [InlineData("Get-Module -ListAvailable -Name 'Shared*'")]
    [InlineData("Microsoft.PowerShell.Core\\Get-Module -Name SharedHelpers")]
    [InlineData("gmo SharedHelpers")]
    [InlineData("Remove-Module -Name SharedHelpers")]
    [InlineData("rmo SharedHelpers")]
    [InlineData("Get-Module -Name $moduleName")]
    [InlineData("Get-Command -Module SharedHelpers")]
    [InlineData("Get-Command -Mod SharedHelpers")]
    [InlineData("Get-Command -FullyQualifiedModule @{ModuleName='SharedHelpers';ModuleVersion='1.0.0'}")]
    [InlineData("Get-Command -FullyQualifiedM @{ModuleName='SharedHelpers'}")]
    [InlineData("gcm -FullyQualifiedModule @{ModuleName='SharedHelpers'}")]
    [InlineData("Get-Command @parameters")]
    public void Analyze_PreservesApprovedModuleIdentityUsedByModuleManagement(string code)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, code,
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("@(\"$env:PF_DEPENDENCY\")")]
    [InlineData("@(@{ModuleName=\"$env:PF_DEPENDENCY\";ModuleVersion='1.0.0'})")]
    [InlineData("@($env:PF_DEPENDENCY)")]
    public void Run_RejectsApprovedDependenciesWhenNestedRequirementsAreComputed(string requirements)
        => AssertRetainedDependency("NestedModules", "Nested.psd1", "'nested'", nestedRequirements: requirements);

    [Theory]
    [InlineData("NestedModules", "@($env:PF_NESTED_MODULE)")]
    [InlineData("ScriptsToProcess", "@($env:PF_HOOK)")]
    [InlineData("RootModule", "$env:PF_ROOT_MODULE")]
    public void Run_RejectsApprovedDependenciesWhenManifestLoadedContentIsComputed(string property, string expression)
        => AssertRetainedDependency("NestedModules", "Nested.psd1", "'nested'", nestedRequirements: "@()",
            computedProperty: property, computedExpression: expression);

    [Theory]
    [InlineData("ScriptsToProcess", "Classes/Initialize.ps1", "SharedHelpers\\Get-SharedHelper")]
    [InlineData("ScriptsToProcess", "Classes/Initialize.ps1", "Get-SharedHelper")]
    [InlineData("NestedModules", "Nested.psm1", "SharedHelpers\\Get-SharedHelper")]
    [InlineData("NestedModules", "Nested.psm1", "Get-SharedHelper")]
    [InlineData("NestedModules", "Nested.psd1", "'nested'")]
    public void Run_RejectsApprovedDependencyUsedByRetainedManifestContent(string key, string relativePath, string script)
        => AssertRetainedDependency(key, relativePath, script);

    [Theory]
    [InlineData("ScriptsToProcess", "Hooks/initialize.ps1", "Hooks/Initialize.ps1")]
    [InlineData("NestedModules", "Nested.psm1", "nested.psm1")]
    [InlineData("NestedModules", "SOURCE.psm1", null)]
    public void Run_RejectsApprovedDependencyUsedByCaseDistinctRetainedContent(
        string key, string relativePath, string? predecessor)
        => AssertRetainedDependency(key, relativePath, "SharedHelpers\\Get-SharedHelper", predecessor, requireCaseSensitiveStorage: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_RejectsRetainedApprovedRuntimeRequirementsWithoutHelperMerging(bool merge)
        => AssertRetainedDependency("NestedModules", "Nested.psd1", "'nested'", merge: merge, mergeMissing: false);

    [Theory]
    [InlineData("FormatsToProcess", "Consumer.format.ps1xml", "<Configuration><ViewDefinitions><View><Name>Consumer</Name><ViewSelectedBy><TypeName>System.String</TypeName></ViewSelectedBy><TableControl><TableRowEntries><TableRowEntry><TableColumnItems><TableColumnItem><ScriptBlock>SharedHelpers\\Get-SharedHelper</ScriptBlock></TableColumnItem></TableColumnItems></TableRowEntry></TableRowEntries></TableControl></View></ViewDefinitions></Configuration>")]
    [InlineData("TypesToProcess", "Consumer.types.ps1xml", "<Types><Type><Name>System.String</Name><Members><ScriptProperty><Name>Shared</Name><GetScriptBlock>SharedHelpers\\Get-SharedHelper</GetScriptBlock></ScriptProperty></Members></Type></Types>")]
    [InlineData("TypesToProcess", "Consumer.types.ps1xml", "<Types><Type><Name>System.String</Name><Members><ScriptMethod><Name>Shared</Name><Script>SharedHelpers\\Get-SharedHelper</Script></ScriptMethod></Members></Type></Types>")]
    public void Run_RejectsApprovedRuntimeReferencesInExecutableXml(string key, string path, string xml)
        => AssertRetainedDependency(key, path, xml);

    [Fact]
    public void ReadRelativePaths_PreservesCaseDistinctReferencesForFilesystemResolution()
    {
        string manifest = Path.GetTempFileName();
        try
        {
            File.WriteAllText(manifest, "@{ScriptsToProcess=@('Hooks/Initialize.ps1','Hooks/initialize.ps1')}");
            Assert.Equal(new[] { "Hooks/Initialize.ps1", "Hooks/initialize.ps1" },
                ModuleManifestLoadedContent.ReadRelativePaths(manifest, StringComparer.Ordinal));
        }
        finally { File.Delete(manifest); }
    }

    private static void AssertRetainedDependency(string key, string relativePath, string script,
        string? predecessor = null, bool requireCaseSensitiveStorage = false,
        string nestedRequirements = "@('SharedHelpers')", string? computedProperty = null, string? computedExpression = null,
        bool merge = true, bool mergeMissing = true)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string modules = Path.Combine(root, "installed");
            WriteModule(modules, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            WriteModule(root, "source", "function Get-ConsumerThing { 'consumer' }");
            string source = Path.Combine(root, "source");
            if (requireCaseSensitiveStorage)
            {
                string probe = Path.Combine(source, "CaseProbe");
                File.WriteAllText(probe, "probe");
                bool caseSensitive = !File.Exists(Path.Combine(source, "caseprobe"));
                File.Delete(probe);
                if (!caseSensitive)
                    return; // The same fixtures execute against real case-sensitive storage on Linux CI.
            }
            string retained = Path.Combine(source, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
            if (relativePath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                string rootExpression = computedProperty == "RootModule" ? computedExpression! : "'NestedRuntime.psm1'";
                File.WriteAllText(retained, "@{RootModule=" + rootExpression + ";ModuleVersion='1.0.0';RequiredModules=" + nestedRequirements +
                    (computedProperty is null || computedProperty == "RootModule" ? string.Empty : ";" + computedProperty + "=" + computedExpression) + "}");
                File.WriteAllText(Path.Combine(source, "NestedRuntime.psm1"), script);
            }
            else
                File.WriteAllText(retained, script);
            if (predecessor is not null)
                File.WriteAllText(Path.Combine(source, predecessor), "'no dependency'");
            Assert.True(ManifestEditor.TrySetTopLevelStringArray(Path.Combine(source, "source.psd1"), key,
                predecessor is null ? new[] { relativePath } : new[] { predecessor, relativePath }));
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "source", SourcePath = source, Version = "1.0.0", KeepStaging = true },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { Merge = merge, MergeMissing = mergeMissing } },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "SharedHelpers", RequiredVersion = "1.0.0", VersionSource = ModuleDependencyVersionSource.Installed }
                    },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = "SharedHelpers", VersionSource = ModuleDependencyVersionSource.Installed }
                    }
                }
            };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
                moduleDependencyMetadataProvider: new FixtureMetadataProvider(modules),
                missingFunctionAnalysisService: new PowerShellMissingFunctionAnalysisService());
            var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec)));
            Assert.Contains("Approved modules cannot remain runtime dependencies", error.Message);
            Assert.Contains("SharedHelpers", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("function Get-SharedAliasTarget { [Alias('SharedAlias')] param() 'alias-target' }", null)]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", null)]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "consumer")]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "nested donor")]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "consumer alias")]
    [InlineData("function Get-Date { 'alias-target' }; Set-Alias SharedAlias Get-Date", "consumer cmdlet")]
    [InlineData("function Get-Date { 'alias-target' }; Set-Alias SharedAlias Get-Date", "donor cmdlet")]
    [InlineData("function Get-Date { 'alias-target' }; Set-Alias SharedAlias Get-Date", "qualified cmdlet")]
    [InlineData("function Get-SharedAliasTarget { [Alias(('Get-' + 'Date'))] param() 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "attribute cmdlet")]
    [InlineData("function Get-SharedAliasTarget { [Alias('Get-Date')] param() 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "attribute donor cmdlet")]
    [InlineData("function Get-SharedAliasTarget { [Alias('Get-AdditionalFixture')] param() 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "additional alias")]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "nested consumer function")]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "nested donor function")]
    [InlineData("filter Convert-DonorItem { \"alias-$_\" }; Set-Alias SharedAlias Convert-DonorItem", "filter")]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "nested local alias")]
    [InlineData("function Get-SharedAliasTarget { $MyInvocation.MyCommand.Name }; Set-Alias SharedAlias Get-SharedAliasTarget", "current function name")]
    [InlineData("$sharedValue='module'; $parameterValue='module'; function Get-SharedAliasTarget { param($parameterValue='parameter') $sharedValue = 'local'; $sharedValue; $parameterValue }; Set-Alias SharedAlias Get-SharedAliasTarget", "local bindings")]
    [InlineData("function Get-PrivateAliasTarget { 'alias-target' }; function Get-SharedAliasTarget { Set-Alias Get-Date Get-PrivateAliasTarget; Get-Date }; Set-Alias SharedAlias Get-SharedAliasTarget", "local alias target")]
    [InlineData("function Get-SharedAliasTarget { Set-Alias LocalDate Get-Date -Scope Private; if ((LocalDate) -is [datetime]) { 'alias-target' } }; Set-Alias SharedAlias Get-SharedAliasTarget", "private scope alias")]
    [InlineData("$value='module'; function Get-SharedAliasTarget { param($value='alias-target', $result=$value) $result }; Set-Alias SharedAlias Get-SharedAliasTarget", "parameter default")]
    [InlineData("$value='module'; function Get-SharedAliasTarget { param($value='alias-target') 1 | ForEach-Object { $value } }; Set-Alias SharedAlias Get-SharedAliasTarget", "synchronous callback")]
    [InlineData("$PSBoundParameters=@{value='module'}; function Get-SharedAliasTarget { param($value) $PSBoundParameters['value'] }; Set-Alias SharedAlias Get-SharedAliasTarget", "invocation parameters")]
    [InlineData("function Get-SharedAliasTarget { Set-Alias LocalDate Get-Date; (LocalDate) -is [datetime] }; Set-Alias SharedAlias Get-SharedAliasTarget", "shadowed consumer alias")]
    [InlineData("$value='module'; function Get-SharedAliasTarget { param($value='alias-target') $temporary=1; Remove-Variable temporary; $value }; Set-Alias SharedAlias Get-SharedAliasTarget", "unrelated removal")]
    [InlineData("$value='module'; function Get-SharedAliasTarget { param($value='alias-target') $value; Remove-Variable value }; Set-Alias SharedAlias Get-SharedAliasTarget", "later removal")]
    [InlineData("function Get-PrivateAliasTarget { 'alias-target' }; function Get-SharedAliasTarget { Set-Alias InnerAlias Get-PrivateAliasTarget; Set-Alias OuterAlias InnerAlias; OuterAlias }; Set-Alias SharedAlias Get-SharedAliasTarget", "local alias chain")]
    [InlineData("$value='module'; function Get-SharedAliasTarget { param($value='alias-target') function Get-Nested { $value }; Get-Nested }; Set-Alias SharedAlias Get-SharedAliasTarget", "nested parameter")]
    [InlineData("function Get-SharedAliasTarget { 'alias-target' }; Set-Alias SharedAlias Get-SharedAliasTarget", "marked single root")]
    public void Run_PackagesApprovedDonorAliasOrReportsANameConflict(string aliasDeclaration, string? conflictScope)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        Directory.CreateDirectory(root);
        try
        {
            string modules = Path.Combine(root, "installed");
            WriteModule(modules, "SharedHelpers", aliasDeclaration + "; Export-ModuleMember -Function @() -Alias SharedAlias");
            string parentDonor = conflictScope switch
            {
                "nested donor" or "consumer alias" => "function Get-ParentAliasTarget { 'parent-copy' }; Set-Alias SharedAlias Get-ParentAliasTarget; function Get-ParentThing { SharedAlias }",
                "donor cmdlet" or "attribute donor cmdlet" => "function Get-ParentThing { Get-Date | Out-Null; 'parent-copy' }",
                "nested donor function" => "function Get-ParentThing { function SharedAlias { 'parent-copy' }; SharedAlias }",
                _ => "function Get-SharedHelper { 'parent-copy' }; function Get-ParentThing { Get-SharedHelper }"
            };
            WriteModule(modules, "ParentDonor", parentDonor + "; Export-ModuleMember -Function Get-ParentThing");
            WriteModule(modules, "RuntimeDependency", "function Get-RuntimeThing { 'runtime' }; Export-ModuleMember Get-RuntimeThing");
            Environment.SetEnvironmentVariable("PSModulePath", modules + Path.PathSeparator + previousModulePath);
            string source = Path.Combine(root, "source");
            string consumerPrefix = conflictScope switch
            {
                "consumer" => "function Get-SharedAliasTarget { 'consumer' }; ",
                "consumer alias" => "function Get-LocalTarget { 'consumer' }; Set-Alias SharedAlias Get-LocalTarget; ",
                "nested consumer function" => "function Invoke-DonorAlias { SharedAlias }; ",
                "nested local alias" => "function Initialize-LocalAlias { Set-Alias SharedAlias Get-ParentThing -Scope Local }; Initialize-LocalAlias; ",
                "shadowed consumer alias" => "Set-Alias LocalDate Get-Item; ",
                "marked single root" => "# Auto-generated by PowerForge. Do not edit.\n",
                _ => string.Empty
            };
            string consumerSetup = conflictScope switch
            {
                "consumer cmdlet" or "attribute cmdlet" => "Get-Date | Out-Null; ",
                "qualified cmdlet" => "Microsoft.PowerShell.Utility\\Get-Date | Out-Null; ",
                "nested consumer function" => "function SharedAlias { 'local' }; SharedAlias; ",
                _ => string.Empty
            };
            string aliasCall = conflictScope switch
            {
                "additional alias" => "SharedAlias; (Get-Alias -Name Get-AdditionalFixture).Definition; ",
                "nested consumer function" => "Invoke-DonorAlias; ",
                "filter" => "1,2 | SharedAlias; ",
                "invocation parameters" => "SharedAlias -Value 'bound'; ",
                "shadowed consumer alias" => "SharedAlias; (Get-Alias LocalDate).Definition; ",
                _ => "SharedAlias; "
            };
            WriteModule(root, "source",
                consumerPrefix +
                "function Get-ConsumerThing { " + consumerSetup +
                "Get-ParentThing; " + aliasCall + "}; Export-ModuleMember Get-ConsumerThing");
            File.WriteAllText(Path.Combine(source, "Consumer.format.ps1xml"),
                "<Configuration><ViewDefinitions><View><Name>Consumer</Name><ViewSelectedBy><TypeName>System.String</TypeName></ViewSelectedBy><TableControl><TableRowEntries><TableRowEntry><TableColumnItems><TableColumnItem><PropertyName>Length</PropertyName></TableColumnItem></TableColumnItems></TableRowEntry></TableRowEntries></TableControl></View></ViewDefinitions></Configuration>");
            Assert.True(ManifestEditor.TrySetTopLevelStringArray(Path.Combine(source, "source.psd1"), "FormatsToProcess", new[] { "Consumer.format.ps1xml" }));
            string hooks = Path.Combine(source, "Hooks");
            Directory.CreateDirectory(hooks);
            File.WriteAllText(Path.Combine(hooks, "Initialize.ps1"), "$null = 1");
            Assert.True(ManifestEditor.TrySetTopLevelStringArray(Path.Combine(source, "source.psd1"), "ScriptsToProcess", new[] { "Hooks/Initialize.ps1" }));
            // WriteModule uses the directory name as identity; the consumer has a normal manifest.
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { Merge = true, MergeMissing = true } },
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true, Path = Path.Combine(root, "packed"),
                        RequiredModules = new ArtefactRequiredModulesConfiguration { Enabled = true, Source = RequiredModulesSource.Installed }
                    }
                }
            };
            foreach (string name in new[] { "ParentDonor", "SharedHelpers", "RuntimeDependency" })
            {
                segments.Add(new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.RequiredModule,
                    Configuration = new ModuleDependencyConfiguration { ModuleName = name, RequiredVersion = "1.0.0", VersionSource = ModuleDependencyVersionSource.Installed }
                });
                if (name != "RuntimeDependency")
                    segments.Add(new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.ApprovedModule,
                        Configuration = new ModuleDependencyConfiguration { ModuleName = name, VersionSource = ModuleDependencyVersionSource.Installed }
                    });
            }
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "source", SourcePath = source, Version = "1.0.0", KeepStaging = true },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
                moduleDependencyMetadataProvider: new FixtureMetadataProvider(modules),
                missingFunctionAnalysisService: new PowerShellMissingFunctionAnalysisService());
            if (conflictScope is not (null or "qualified cmdlet" or "additional alias" or "filter" or "nested local alias"
                or "current function name" or "local bindings" or "local alias target" or "private scope alias"
                or "parameter default" or "synchronous callback" or "invocation parameters" or "shadowed consumer alias"
                or "unrelated removal" or "later removal" or "local alias chain" or "nested parameter"
                or "marked single root"))
            {
                var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec)));
                Assert.Contains("SharedAlias", error.Message);
                Assert.Contains(conflictScope.EndsWith("cmdlet", StringComparison.Ordinal) ? "Get-Date" : "AliasTarget", error.Message);
                Assert.Contains("name conflict", error.Message);
                Assert.False(File.Exists(Path.Combine(root, "packed", "source.zip")));
                return;
            }

            var result = runner.Run(spec, runner.Plan(spec));
            string manifest = File.ReadAllText(result.BuildResult.ManifestPath);
            Assert.Contains("RuntimeDependency", manifest);
            Assert.DoesNotContain("SharedHelpers", manifest);
            Assert.DoesNotContain("ParentDonor", manifest);
            using var zip = ZipFile.OpenRead(Path.Combine(root, "packed", "source.zip"));
            Assert.Contains(zip.Entries, entry => entry.FullName == "RuntimeDependency/RuntimeDependency.psd1");
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("SharedHelpers/", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ParentDonor/", StringComparison.Ordinal));
            string extracted = Path.Combine(root, "consumer");
            string consumerManifest = Assert.Single(zip.Entries, entry => entry.Name == "source.psd1").FullName;
            zip.ExtractToDirectory(extracted);
            Environment.SetEnvironmentVariable("PSModulePath", extracted + Path.PathSeparator + previousModulePath);
            var initialState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
            initialState.AuthorizationManager = new System.Management.Automation.AuthorizationManager("PowerForge.Tests");
            using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialState);
            runspace.Open();
            using var ps = System.Management.Automation.PowerShell.Create();
            ps.Runspace = runspace;
            ps.AddCommand("Import-Module").AddParameter("Name", Path.Combine(extracted, consumerManifest))
                .AddParameter("ErrorAction", "Stop").Invoke();
            Assert.Empty(ps.Streams.Error);
            ps.Commands.Clear();
            var output = ps.AddCommand("Get-ConsumerThing").AddParameter("ErrorAction", "Stop").Invoke();
            Assert.Empty(ps.Streams.Error);
            string[] expected = conflictScope switch
            {
                "additional alias" => new[] { "parent-copy", "alias-target", "Get-SharedAliasTarget" },
                "filter" => new[] { "parent-copy", "alias-1", "alias-2" },
                "current function name" => new[] { "parent-copy", "Get-SharedAliasTarget" },
                "local bindings" => new[] { "parent-copy", "local", "parameter" },
                "invocation parameters" => new[] { "parent-copy", "bound" },
                "shadowed consumer alias" => new[] { "parent-copy", "True", "Get-Item" },
                _ => new[] { "parent-copy", "alias-target" }
            };
            Assert.Equal(expected, output.Select(item => item.BaseObject.ToString()));
            ps.Commands.Clear();
            Assert.Empty(ps.AddCommand("Get-Module").AddParameter("Name", "SharedHelpers").Invoke());
            ps.Commands.Clear();
            Assert.Empty(ps.AddCommand("Get-Alias").AddParameter("Name", "SharedAlias")
                .AddParameter("ErrorAction", "SilentlyContinue").Invoke());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previousModulePath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("consumer function", "Get-SharedHelper", "name conflict")]
    [InlineData("sibling donor", "Get-SharedHelper", "name conflict")]
    [InlineData("consumer cmdlet", "Get-Date", "name conflict")]
    [InlineData("computed consumer alias", "Get-SharedAlias", "consumer alias declaration")]
    [InlineData("nested script alias", "Get-SharedHelper", "name conflict")]
    [InlineData("unqualified state", "Get-SharedEntry", "SharedValue")]
    [InlineData("donor alias write", "Get-SharedEntry", "donor alias scope")]
    [InlineData("qualified call", "Get-SharedEntry", "referenced at runtime")]
    [InlineData("dynamic call", "SharedHelpers", "dynamic command")]
    [InlineData("unbound runtime import", "SharedHelpers", "referenced at runtime")]
    [InlineData("merge disabled", "Get-SharedEntry", "without merging")]
    [InlineData("script state", "Get-SharedEntry", "script:SharedValue")]
    [InlineData("relative file", "Get-SharedEntry", "PSScriptRoot")]
    [InlineData("scoped path", "Get-SharedEntry", "PSScriptRoot")]
    [InlineData("computed local alias", "Get-SharedEntry", "alias whose name or target")]
    [InlineData("forward parameter default", "Get-SharedEntry", "SharedValue")]
    [InlineData("function header default", "Get-SharedEntry", "SharedValue")]
    [InlineData("module state in callback", "Get-SharedEntry", "SharedValue")]
    [InlineData("escaping callback", "Get-SharedEntry", "SharedValue")]
    [InlineData("removed local binding", "Get-SharedEntry", "SharedValue")]
    [InlineData("provider removal", "Get-SharedEntry", "SharedValue")]
    [InlineData("conditional alias", "Get-SharedEntry", "WhatIf or Confirm")]
    [InlineData("shared consumer alias", "Get-SharedEntry", "shared with child scopes")]
    [InlineData("local alias cycle", "Get-SharedEntry", "function-local binding is ambiguous")]
    [InlineData("script removal", "Get-SharedEntry", "outside the function's local scope")]
    [InlineData("global removal", "Get-SharedEntry", "outside the function's local scope")]
    [InlineData("script variable cmdlet", "Get-SharedEntry", "outside the function's local scope")]
    [InlineData("escaped nested function", "Get-SharedEntry", "SharedValue")]
    [InlineData("authored root without merging", "Get-SharedEntry", "without merging")]
    public void Run_RejectsUnpreservableApprovedDonorCommands(
        string scenario, string commandOrModule, string reason)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string installed = Path.Combine(root, "installed");
            string donor = scenario switch
            {
                "consumer cmdlet" => "function Get-Date { 'private' }; function Get-SharedEntry { Get-Date }",
                "computed consumer alias" =>
                    "function Get-SharedHelper { 'donor' }; Set-Alias Get-SharedAlias Get-SharedHelper; function Get-SharedEntry { Get-SharedAlias }",
                "script state" => "$script:SharedValue = 'donor'; function Get-SharedEntry { $script:SharedValue }",
                "unqualified state" => "$SharedValue = 'donor'; function Get-SharedEntry { $SharedValue }",
                "donor alias write" => "function Get-SharedHelper { 'donor' }; function Get-SharedEntry { Set-Alias Get-SharedAlias Get-SharedHelper -Scope Script }",
                "relative file" => "function Get-SharedEntry { Get-Content (Join-Path $PSScriptRoot 'data.txt') }",
                "scoped path" => "function Get-SharedEntry { $local:PSScriptRoot }",
                "computed local alias" => "function Get-PrivateAliasTarget { 'donor' }; function Get-SharedEntry { $name = 'Get-PrivateAliasTarget'; Set-Alias Get-Date $name; Get-Date }",
                "forward parameter default" => "$SharedValue = 'donor'; function Get-SharedEntry { param($other=$SharedValue, $SharedValue='local') $other }",
                "function header default" => "$SharedValue = 'donor'; function Get-SharedEntry($other=$SharedValue) { $other }",
                "module state in callback" => "$SharedValue = 'donor'; function Get-SharedEntry { 1 | ForEach-Object { $SharedValue } }",
                "escaping callback" => "$SharedValue = 'donor'; function Get-SharedEntry { param($SharedValue='local') { $SharedValue } }",
                "removed local binding" => "$SharedValue = 'donor'; function Get-SharedEntry { param($SharedValue='local') Remove-Variable SharedValue; 1 | ForEach-Object { $SharedValue } }",
                "provider removal" => "$SharedValue = 'donor'; function Get-SharedEntry { param($SharedValue='local') . Remove-Item -LiteralPath Variable:SharedValue; $SharedValue }",
                "conditional alias" => "function Get-PrivateAliasTarget { 'donor' }; function Get-SharedEntry { Set-Alias Get-Date Get-PrivateAliasTarget -wi; Get-Date }",
                "shared consumer alias" => "function Get-SharedEntry { Set-Alias LocalDate Get-Date; (LocalDate) -is [datetime] }",
                "local alias cycle" => "function Get-SharedEntry { Set-Alias FirstAlias SecondAlias; Set-Alias SecondAlias FirstAlias; FirstAlias }",
                "script removal" => "function Get-SharedEntry { Remove-Variable -Name SharedValue -Scope Script }",
                "global removal" => "function Get-SharedEntry { Remove-Variable -Name SharedValue -Scope Global }",
                "script variable cmdlet" => "$SharedValue = 'donor'; function Get-SharedEntry { Get-Variable -Name SharedValue -Scope Script -ValueOnly }",
                "escaped nested function" => "$SharedValue = 'donor'; function Get-SharedEntry { param($SharedValue='local') function Get-Nested { $SharedValue }; Get-Nested; ${function:Get-Nested} }",
                _ => "function Get-SharedHelper { 'donor' }; function Get-SharedEntry { Get-SharedHelper }"
            };
            if (scenario != "unbound runtime import")
                WriteModule(installed, "SharedHelpers", donor + "; Export-ModuleMember -Function Get-SharedEntry");
            string consumer = scenario switch
            {
                "consumer function" =>
                    "function Get-SharedHelper { 'consumer' }; function Get-ConsumerThing { Get-SharedHelper; Get-SharedEntry }",
                "nested script alias" =>
                    "function Get-LocalEntry { 'consumer' }; function Initialize-SharedAliases { Set-Alias Get-SharedHelper Get-LocalEntry -Scope Script }; Initialize-SharedAliases; function Get-ConsumerThing { Get-SharedEntry }",
                "shared consumer alias" => "Set-Alias LocalDate Get-Item -Option AllScope; function Get-ConsumerThing { Get-SharedEntry }",
                "computed consumer alias" =>
                    "function Get-LocalEntry { [Alias(('Get-' + 'SharedAlias'))] param() 'consumer' }; function Get-ConsumerThing { Get-SharedAlias; Get-SharedEntry }",
                "consumer cmdlet" => "function Get-ConsumerThing { Get-Date | Out-Null; Get-SharedEntry }",
                "qualified call" => "function Get-ConsumerThing { SharedHelpers\\Get-SharedEntry }",
                "dynamic call" => "function Get-ConsumerThing { $name = 'Get-SharedEntry'; & $name }",
                "unbound runtime import" => "function Get-ConsumerThing { Import-Module SharedHelpers }",
                "sibling donor" => "function Get-ConsumerThing { Get-SharedEntry; Get-SiblingEntry }",
                _ => "function Get-ConsumerThing { Get-SharedEntry }"
            };
            WriteModule(root, "source", consumer + "; Export-ModuleMember -Function Get-ConsumerThing");
            if (scenario == "authored root without merging")
            {
                string custom = Path.Combine(root, "source", "Custom");
                Directory.CreateDirectory(custom);
                File.WriteAllText(Path.Combine(custom, "Helper.ps1"), "function Get-Unrelated { 'helper' }");
            }
            if (scenario == "sibling donor")
                WriteModule(installed, "SiblingHelpers",
                    "function Get-SharedHelper { 'sibling' }; function Get-SiblingEntry { Get-SharedHelper }; Export-ModuleMember -Function Get-SiblingEntry");
            if (scenario == "relative file")
                File.WriteAllText(Path.Combine(installed, "SharedHelpers", "data.txt"), "donor asset");

            string packed = Path.Combine(root, "packed");
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        Merge = scenario is not ("merge disabled" or "authored root without merging"),
                        MergeMissing = scenario is not ("merge disabled" or "authored root without merging"),
                    }
                },
                new ConfigurationArtefactSegment { ArtefactType = ArtefactType.Packed, Configuration = new ArtefactConfiguration { Enabled = true, Path = packed } }
            };
            if (scenario == "authored root without merging")
            {
                segments.Add(new ConfigurationInformationSegment
                {
                    Configuration = new InformationConfiguration { IncludePS1 = new[] { "Custom" } }
                });
            }
            foreach (string name in scenario == "sibling donor" ? new[] { "SharedHelpers", "SiblingHelpers" } : new[] { "SharedHelpers" })
            {
                segments.Add(new ConfigurationModuleSegment
                {
                    Kind = ModuleDependencyKind.ApprovedModule,
                    Configuration = new ModuleDependencyConfiguration { ModuleName = name, VersionSource = ModuleDependencyVersionSource.Installed }
                });
            }

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = "source", SourcePath = Path.Combine(root, "source"), Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = segments.ToArray()
            };
            var runner = new ModulePipelineRunner(new NullLogger(), powerShellRunner: null,
                moduleDependencyMetadataProvider: new FixtureMetadataProvider(installed),
                missingFunctionAnalysisService: new PowerShellMissingFunctionAnalysisService());

            var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec, runner.Plan(spec)));
            Assert.Contains("SharedHelpers", error.Message);
            Assert.Contains(commandOrModule, error.Message);
            Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(packed, "source.zip")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void Analyze_PrunesApprovedDependencyWhoseHelperIsAlreadyLocal()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "SharedHelpers", "function Get-LocalHelper { 'donor' }; Export-ModuleMember Get-LocalHelper");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null,
                "function Get-LocalHelper { 'local' }; Get-LocalHelper",
                new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" },
                    approvedModuleSources: new[] { new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers")) },
                    requireApprovedModuleSources: true));
            Assert.Equal(new[] { "SharedHelpers" }, result.FullyInlinedApprovedModules);
            Assert.Empty(result.Functions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Analyze_PreservesAbsentDonorWhenItsRuntimeSurfaceCannotBeInspected()
    {
        var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "'consumer'",
            new MissingFunctionsOptions(approvedModules: new[] { "UnavailableDonor" }));
        Assert.Empty(result.FullyInlinedApprovedModules);
    }

    [Fact]
    public void Analyze_PrunesRedundantApprovedDependencyWhenAnotherDonorContainsItsHelper()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteModule(root, "SharedHelpers", "function Get-SharedHelper { 'shared' }; Export-ModuleMember Get-SharedHelper");
            WriteModule(root, "ParentDonor", "function Get-SharedHelper { 'parent-copy' }; function Get-ParentThing { Get-SharedHelper }; Export-ModuleMember Get-ParentThing");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "Get-ParentThing",
                new MissingFunctionsOptions(
                    approvedModules: new[] { "ParentDonor", "SharedHelpers" },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[]
                    {
                        new ApprovedModuleSource("ParentDonor", "1.0.0", Path.Combine(root, "ParentDonor")),
                        new ApprovedModuleSource("SharedHelpers", "1.0.0", Path.Combine(root, "SharedHelpers"))
                    },
                    requireApprovedModuleSources: true));

            Assert.Equal(new[] { "ParentDonor", "SharedHelpers" }, result.FullyInlinedApprovedModules);
            Assert.Contains(result.Functions, function => function.Contains("parent-copy", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Functions, function => function.Contains("'shared'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_PreservesApprovedDependenciesWhenDynamicInvocationPreventsProof()
    {
        var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, "& $commandName",
            new MissingFunctionsOptions(approvedModules: new[] { "SharedHelpers" }));

        Assert.Empty(result.FullyInlinedApprovedModules);
    }

    private static void WriteModule(string root, string name, string body)
    {
        var moduleRoot = Path.Combine(root, name);
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, name + ".psm1"), body);
        File.WriteAllText(Path.Combine(moduleRoot, name + ".psd1"),
            $"@{{RootModule='{name}.psm1'; ModuleVersion='1.0.0'}}");
    }

    private sealed class FixtureMetadataProvider(string modulesRoot) : IModuleDependencyVersionedMetadataProvider
    {
        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetInstalledModules(IReadOnlyList<RequiredModuleReference> references)
            => GetLatestInstalledModules(references.Select(reference => reference.ModuleName).ToArray());

        public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
            => names.Where(name => Directory.Exists(Path.Combine(modulesRoot, name)))
                .ToDictionary(name => name, name => new InstalledModuleMetadata(name, "1.0.0", null, Path.Combine(modulesRoot, name)), StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<RequiredModuleReference> GetRequiredModulesForInstalledModule(string moduleName)
            => Array.Empty<RequiredModuleReference>();

        public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
            IReadOnlyCollection<string> names, string? repository, RepositoryCredential? credential, bool prerelease)
            => throw new InvalidOperationException("The fixture must resolve installed dependencies without network access.");
    }
}
