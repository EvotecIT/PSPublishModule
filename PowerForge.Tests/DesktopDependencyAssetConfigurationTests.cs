using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge.Tests;

public sealed class DesktopDependencyAssetConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));

    public DesktopDependencyAssetConfigurationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cmdlet_BindsHashtable_EmitsTypedCaseInsensitiveMapIncludingExplicitEmpty(bool empty)
    {
        InitialSessionState state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("New-ConfigurationBuild", typeof(PSPublishModule.NewConfigurationBuildCommand), null));
        using Runspace runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        using System.Management.Automation.PowerShell shell = System.Management.Automation.PowerShell.Create();
        shell.Runspace = runspace;
        Hashtable supplied = empty ? new() : new() { ["System.Memory"] = "netstandard2.0" };
        var output = shell.AddCommand("New-ConfigurationBuild").AddParameter("NETDesktopDependencyAssetFrameworks", supplied).Invoke();

        Assert.False(shell.HadErrors, string.Join(Environment.NewLine, shell.Streams.Error.Select(error => error.ToString())));
        var segment = Assert.IsType<ConfigurationBuildLibrariesSegment>(Assert.Single(output).BaseObject);
        var map = Assert.IsType<Dictionary<string, string>>(segment.BuildLibraries.DesktopDependencyAssetFrameworks);
        Assert.Equal(StringComparer.OrdinalIgnoreCase, map.Comparer);
        if (empty) Assert.Empty(map);
        else Assert.Equal("netstandard2.0", map["system.memory"]);
    }

    [Fact]
    public void Factory_UnspecifiedMap_DoesNotEmitLibraryConfiguration()
    {
        var request = new BuildConfigurationRequest { NETDesktopDependencyAssetFrameworks = Selection() };
        Assert.Empty(new BuildConfigurationFactory().Create(request));

        request.NETDesktopDependencyAssetFrameworksSpecified = true;
        var libraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(Assert.Single(new BuildConfigurationFactory().Create(request)));
        Assert.Equal("netstandard2.0", libraries.BuildLibraries.DesktopDependencyAssetFrameworks!["system.memory"]);
        request.NETDesktopDependencyAssetFrameworks!.Clear();
        Assert.Single(libraries.BuildLibraries.DesktopDependencyAssetFrameworks!);
    }

    [Fact]
    public void JsonRoundTrip_PreservesBaseAndSegmentMaps_AndPlanUsesLastExplicitMap()
    {
        ModulePipelineSpec spec = Spec();
        spec.Build.DesktopDependencyAssetFrameworks = Selection("System.Buffers");
        spec.Segments = new IConfigurationSegment[] {
            new ConfigurationBuildLibrariesSegment { BuildLibraries = new() { DesktopDependencyAssetFrameworks = Selection() } }
        };
        JsonSerializerOptions options = JsonOptions();
        ModulePipelineSpec restored = JsonSerializer.Deserialize<ModulePipelineSpec>(JsonSerializer.Serialize(spec, options), options)!;
        Assert.Equal("netstandard2.0", restored.Build.DesktopDependencyAssetFrameworks["system.buffers"]);
        var segment = Assert.IsType<ConfigurationBuildLibrariesSegment>(Assert.Single(restored.Segments));
        Assert.Equal("netstandard2.0", segment.BuildLibraries.DesktopDependencyAssetFrameworks!["system.memory"]);

        ModulePipelinePlan plan = new ModulePipelineRunner(new NullLogger()).Plan(restored);
        Assert.Single(plan.BuildSpec.DesktopDependencyAssetFrameworks);
        Assert.Equal("netstandard2.0", plan.BuildSpec.DesktopDependencyAssetFrameworks["SYSTEM.MEMORY"]);
        Assert.Equal(new[] { "NETFramework", "NETDesktopDependencyAssetFrameworks" }, plan.BuildSpec.CsprojRequiredReasons);
        segment.BuildLibraries.DesktopDependencyAssetFrameworks!.Clear();
        Assert.Single(plan.BuildSpec.DesktopDependencyAssetFrameworks);
    }

    [Fact]
    public void Plan_ExplicitEmptyMap_ClearsEarlierSelectionWithoutBinaryIntent()
    {
        ModulePipelineSpec spec = Spec();
        spec.Build.DesktopDependencyAssetFrameworks = Selection();
        spec.Segments = new IConfigurationSegment[] {
            new ConfigurationBuildLibrariesSegment { BuildLibraries = new() { DesktopDependencyAssetFrameworks = Selection("System.Buffers") } },
            new ConfigurationBuildLibrariesSegment { BuildLibraries = new() { DesktopDependencyAssetFrameworks = new() } }
        };
        ModulePipelinePlan plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
        Assert.Empty(plan.BuildSpec.DesktopDependencyAssetFrameworks);
        Assert.Empty(plan.BuildSpec.CsprojRequiredReasons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_BaseSelection_RecordsProjectIntentWhilePreservingNoBuildMode(bool skipDotNetBuild)
    {
        ModulePipelineSpec spec = Spec();
        spec.Build.DesktopDependencyAssetFrameworks = Selection();
        spec.Build.SkipDotNetBuild = skipDotNetBuild;
        ModulePipelinePlan plan = new ModulePipelineRunner(new NullLogger()).Plan(spec);
        Assert.Equal(skipDotNetBuild, plan.BuildSpec.SkipDotNetBuild);
        Assert.Equal("netstandard2.0", plan.BuildSpec.DesktopDependencyAssetFrameworks["system.memory"]);
        Assert.Equal(new[] { "NETFramework", "NETDesktopDependencyAssetFrameworks" }, plan.BuildSpec.CsprojRequiredReasons);
    }

    [Fact]
    public void LegacyAdapter_PreservesMapFromStepsAndSegmentDictionary()
    {
        Hashtable payload = new() { ["DesktopDependencyAssetFrameworks"] = new Hashtable { ["System.Memory"] = "netstandard2.0" } };
        Hashtable legacy = new() { ["Steps"] = new Hashtable { ["BuildLibraries"] = payload } };
        var fromSteps = Assert.IsType<ConfigurationBuildLibrariesSegment>(Assert.Single(LegacySegmentAdapter.CollectFromLegacyConfiguration(legacy)));
        Assert.Equal("netstandard2.0", fromSteps.BuildLibraries.DesktopDependencyAssetFrameworks!["system.memory"]);

        using Runspace runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        Runspace? previous = Runspace.DefaultRunspace;
        Runspace.DefaultRunspace = runspace;
        try
        {
            var settings = ScriptBlock.Create("@{ Type='BuildLibraries'; BuildLibraries=@{ DesktopDependencyAssetFrameworks=@{ 'System.Memory'='netstandard2.0' } } }");
            var fromSettings = Assert.IsType<ConfigurationBuildLibrariesSegment>(Assert.Single(LegacySegmentAdapter.CollectFromSettings(settings)));
            Assert.Equal("netstandard2.0", fromSettings.BuildLibraries.DesktopDependencyAssetFrameworks!["SYSTEM.MEMORY"]);
        }
        finally { Runspace.DefaultRunspace = previous; }
    }

    [Fact]
    public void Schemas_DeclareExplicitPackageMapWithOnlySupportedAssetFramework()
    {
        foreach (string schemaName in new[] { "powerforge.buildspec.schema.json", "powerforge.segments.schema.json" })
        {
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(SchemaPath(schemaName)));
            JsonElement properties = schemaName.Contains("segments", StringComparison.Ordinal)
                ? schema.RootElement.GetProperty("$defs").GetProperty("BuildLibrariesConfiguration").GetProperty("properties")
                : schema.RootElement.GetProperty("properties");
            JsonElement map = properties.GetProperty("DesktopDependencyAssetFrameworks");
            Assert.Equal(1, map.GetProperty("propertyNames").GetProperty("minLength").GetInt32());
            JsonElement values = map.GetProperty("additionalProperties");
            Assert.Equal("string", values.GetProperty("type").GetString());
            Assert.Equal("netstandard2.0", Assert.Single(values.GetProperty("enum").EnumerateArray()).GetString());
        }
    }

    [Fact]
    public void ReleaseContract_AssetSelectionChanges_InvalidateFrozenContractWhilePackageIdCaseDoesNot()
    {
        File.WriteAllText(Path.Combine(_root, "Sample.psm1"), "function Get-Sample { 'sample' }");
        File.WriteAllText(Path.Combine(_root, "Sample.psd1"), "@{ RootModule='Sample.psm1'; ModuleVersion='1.0.0'; FunctionsToExport=@('Get-Sample') }");
        ModulePipelinePlan plan = new ModulePipelineRunner(new NullLogger()).Plan(Spec());
        plan.BuildSpec.PowerShellCompilation = new() { Enabled = true, Mode = PowerShellCompilationMode.Hybrid };
        string Fingerprint() => PowerShellModuleCompilationReleaseContract.Create(plan, null, null).Sha256;
        string before = Fingerprint();
        plan.BuildSpec.DesktopDependencyAssetFrameworks = Selection();
        string selected = Fingerprint();
        Assert.NotEqual(before, selected);
        plan.BuildSpec.DesktopDependencyAssetFrameworks = Selection("system.memory");
        Assert.Equal(selected, Fingerprint());
    }

    private ModulePipelineSpec Spec() => new()
    {
        Build = new() { Name = "Sample", SourcePath = _root, Version = "1.0.0", Frameworks = new[] { "net472" } },
        Install = new() { Enabled = false }
    };

    private static Dictionary<string, string> Selection(string package = "System.Memory") => new() { [package] = "netstandard2.0" };

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ConfigurationSegmentJsonConverter());
        return options;
    }

    private static string SchemaPath(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        string sourceSchema = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "Schemas", fileName));
        if (File.Exists(sourceSchema)) return sourceSchema;
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "Schemas", fileName);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Repository schema was not found.", fileName);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
