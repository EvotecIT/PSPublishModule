using System.Management.Automation;
using System.Text.Json;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ImportModuleOptionalDependencyConfigurationTests
{
    [Fact]
    public void DslDeclarationSurvivesJsonAndPlanning()
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("Import-Module").AddParameter("Name", typeof(NewConfigurationImportModuleCommand).Assembly.Location);
        ps.Invoke();
        Assert.False(ps.HadErrors, string.Join(Environment.NewLine, ps.Streams.Error));
        ps.Commands.Clear();
        ps.AddScript("New-ConfigurationImportModule -ImportSelf -OptionalBinaryDependencies @{ 'Connector.dll' = @('Optional.dll') }");
        var output = ps.Invoke();
        Assert.False(ps.HadErrors, string.Join(Environment.NewLine, ps.Streams.Error));
        var segment = Assert.IsType<ConfigurationImportModulesSegment>(Assert.Single(output).BaseObject);
        var restored = JsonSerializer.Deserialize<ConfigurationImportModulesSegment>(JsonSerializer.Serialize(segment))!;
        var plan = new ModulePipelineRunner(new NullLogger()).Plan(new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec { Name = "Consumer", SourcePath = Path.GetTempPath(), Version = "1.0.0" },
            Segments = [restored]
        });
        Assert.Equal(new[] { "Optional.dll" }, plan.ImportModules!.OptionalBinaryDependencies!["Connector.dll"]);
        Assert.NotEqual(true, plan.ImportModules.SkipBinaryDependencyCheck);
        Assert.Equal(true, plan.ImportModules.Self);
    }
}
