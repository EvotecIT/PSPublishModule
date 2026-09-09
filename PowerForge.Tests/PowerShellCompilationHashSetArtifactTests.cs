namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void HashSet_StrictLibraryPreservesPublicSetIdentity(string framework)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Set {
                param([Collections.Generic.HashSet[string]]$Items)
                return $Items
            }
            function New-Set {
                param([string]$Value)
                $items = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                [void]$items.Add($Value)
                return $items
            }
            """);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StrictSet", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        Assert.True(result.Manifest.DependencyClosureVerified);
        Assert.Equal(2, result.Manifest.CompiledMethods);
        var directory = Path.Combine(fixture.RootPath, "set-consumer");
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "SetConsumer.csproj");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("TargetFramework", framework),
                new System.Xml.Linq.XElement("OutputType", "Exe"), new System.Xml.Linq.XElement("ImplicitUsings", "enable")),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "Generated.StrictSet"),
                new System.Xml.Linq.XElement("HintPath", result.ArtifactPath!))))).Save(project);
        var abi = result.Manifest.PublicAbi!;
        File.WriteAllText(Path.Combine(directory, "Program.cs"), "using Api = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            var items = Api.New_Set("alpha");
            if (!ReferenceEquals(items, Api.Get_Set(items))) return 1;
            if (items.Add("ALPHA")) return 2;
            if (!items.Add("東京") || items.Count != 2) return 3;
            if (Api.Get_Set(null) != null) return 4;
            if (Api.New_Set("fresh").Count != 1) return 5;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation")) return 6;
            Console.WriteLine("set consumer passed");
            return 0;
            """);
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var run = RunProcess("dotnet", Path.Combine(directory, "bin", "Release", framework, "SetConsumer.dll"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError + " exit=" + run.ExitCode);
        Assert.Contains("set consumer passed", run.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HashSet_NativeArtifactPreservesComparerAliasingAndEnumeration(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-SetFlow {
                [CmdletBinding()] param([ValidateNotNull()][string[]]$Values)
                $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                $alias = $set
                foreach ($value in $Values) { $set.Add($value) }
                $alias.Remove('ALPHA')
                $set.Contains('alpha')
                $set.Count
                foreach ($item in $set) { 'item:' + $item }
                $set.Clear()
                $alias.Count
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HashSet", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == "Invoke-SetFlow");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.False(unit.RetainedHostedSource);
        const string probe = """
            foreach ($values in @(@(),@('alpha'),@('alpha','ALPHA','beta','東京'),@($null,'','beta'))) {
                $records=@(Invoke-SetFlow -Values $values)
                [pscustomobject]@{records=$records;types=@($records | ForEach-Object { $_.GetType().FullName })} |
                    ConvertTo-Json -Depth 5 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "set-flow");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "set-flow");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.False(string.IsNullOrWhiteSpace(original.StandardOutput));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
