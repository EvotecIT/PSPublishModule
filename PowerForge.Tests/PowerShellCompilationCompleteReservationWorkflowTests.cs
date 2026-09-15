namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompleteWorkflow_StrictReservationBudgetRunsInIndependentConsumer(string framework)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "PowerShellCompilationReservationWorkflow", "ReservationBudget.psm1"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "ReservationBudget", resolved.Kind, resolved.Mode,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var manifest = build.Manifest!;
        Assert.False(manifest.RequiresPowerShellRuntime);
        Assert.False(manifest.UsesPowerShellRuntimeFallback);
        Assert.True(manifest.DependencyClosureVerified);
        Assert.True(manifest.DependencyClosure!.Verified);
        Assert.Empty(manifest.DependencyClosure.Limitations);
        Assert.Equal(0, manifest.RuntimeFallbackUnits);
        Assert.Equal(0, manifest.OmittedUnits);
        Assert.Equal(5, manifest.CompiledMethods);
        Assert.All(manifest.PublicAbi!.Methods, method => Assert.True(method.IsInstanceMethod));
        var plan = new PowerShellCompilationAnalyzer().Analyze(resolved, resolved.Mode, framework);
        var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(resolved, plan, framework);
        foreach (var unit in manifest.UnitDispositionLedger!.Entries.Where(unit => unit.RegionGraph is not null))
        {
            var explained = Assert.Single(explanation.Files.SelectMany(file => file.Units), item => item.Name == unit.Name);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(unit.RegionGraph),
                System.Text.Json.JsonSerializer.Serialize(explained.RegionGraph));
        }

        var consumer = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "consumer")).FullName;
        var project = Path.Combine(consumer, "ReservationConsumer.csproj");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("TargetFramework", framework),
                new System.Xml.Linq.XElement("OutputType", "Exe"), new System.Xml.Linq.XElement("ImplicitUsings", "enable")),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "ReservationBudget"),
                new System.Xml.Linq.XElement("HintPath", build.ArtifactPath!))))).Save(project);
        File.WriteAllText(Path.Combine(consumer, "Program.cs"), "using Budget = global::" + manifest.PublicAbi.NamespaceName + "." + manifest.PublicAbi.TypeName + ";\n" + """
            static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
            static int Available(Budget budget) => Convert.ToInt32(budget.Get_AvailableCapacity());
            using var primary = new Budget(100);
            using var other = new Budget(7);
            Require(primary.Request_Capacity(60), "first reservation");
            Require(!primary.Request_Capacity(41), "overbooking rejected");
            Require(primary.Get_ReservedCapacity() == 60 && Available(primary) == 40, "unchanged on rejection");
            Require(primary.Release_Capacity(20) && primary.Request_Capacity(60), "release then fill");
            Require(!primary.Release_Capacity(101), "over-release rejected");
            Require(Available(other) == 7 && other.Get_ReservedCapacity() == 0, "independent instance");
            foreach (var invalid in new[] { -1, 0, 1000001 }) {
                try { primary.Request_Capacity(invalid); throw new Exception("invalid request admitted"); } catch (ArgumentException) { }
                try { primary.Release_Capacity(invalid); throw new Exception("invalid release admitted"); } catch (ArgumentException) { }
                try { primary.Reset(invalid); throw new Exception("invalid reset admitted"); } catch (ArgumentException) { }
                Require(primary.Get_ReservedCapacity() == 100 && Available(primary) == 0, "invalid operation changed state");
            }
            primary.Reset(1000);
            var accepted = 0;
            Parallel.For(0, 2000, _ => { if (primary.Request_Capacity(1)) Interlocked.Increment(ref accepted); });
            Require(accepted == 1000 && Available(primary) == 0, "concurrent reservations oversubscribed capacity");
            Parallel.For(0, 1000, _ => Require(primary.Release_Capacity(1), "concurrent release"));
            Require(primary.Get_ReservedCapacity() == 0 && Available(primary) == 1000, "concurrent release balance");
            primary.Reset();
            Require(Available(primary) == 100, "default reset");
            primary.Dispose(); primary.Dispose();
            try { primary.Request_Capacity(1); throw new Exception("disposed request admitted"); } catch (ObjectDisposedException) { }
            try { primary.Get_AvailableCapacity(); throw new Exception("disposed read admitted"); } catch (ObjectDisposedException) { }
            try { primary.Reset(); throw new Exception("disposed reset admitted"); } catch (ObjectDisposedException) { }
            Require(other.Request_Capacity(7), "other instance survives disposal");
            foreach (var capacity in new[] { 1, 10, 1000000 }) {
                using var budget = new Budget(capacity);
                foreach (var units in new[] { 1, 1, capacity, 1 }) {
                    Console.WriteLine("reserve:" + capacity + ":" + units + ":" + budget.Request_Capacity(units) + ":" + budget.Get_AvailableCapacity() + ":" + budget.Get_ReservedCapacity());
                }
                foreach (var units in new[] { 1, 1, capacity, 1 }) {
                    Console.WriteLine("release:" + capacity + ":" + units + ":" + budget.Release_Capacity(units) + ":" + budget.Get_AvailableCapacity() + ":" + budget.Get_ReservedCapacity());
                }
            }
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Management.Automation"), "PowerShell loaded");
            Console.WriteLine("reservation consumer passed");
            """);
        var consumerBuild = RunProcess("dotnet", "build", project, "-c", "Release", "--nologo", "-v:q");
        Assert.True(consumerBuild.ExitCode == 0, consumerBuild.StandardOutput + consumerBuild.StandardError);
        var run = RunProcess("dotnet", Path.Combine(consumer, "bin", "Release", framework, "ReservationConsumer.dll"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Contains("reservation consumer passed", run.StandardOutput, StringComparison.Ordinal);
        var observations = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("reserve:", StringComparison.Ordinal) || line.StartsWith("release:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(24, observations.Length);
        foreach (var configuration in StatementErrorHosts())
        {
            var original = RunStatementErrorProbe((string)configuration[1],
                "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + """
                foreach($capacity in 1,10,1000000) {
                    $module = Import-Module $modulePath -ArgumentList $capacity -PassThru -Force -DisableNameChecking
                    foreach($units in 1,1,$capacity,1) {
                        'reserve:' + $capacity + ':' + $units + ':' + (Request-Capacity $units) + ':' + (Get-AvailableCapacity) + ':' + (Get-ReservedCapacity)
                    }
                    foreach($units in 1,1,$capacity,1) {
                        'release:' + $capacity + ':' + $units + ':' + (Release-Capacity $units) + ':' + (Get-AvailableCapacity) + ':' + (Get-ReservedCapacity)
                    }
                    Remove-Module $module
                }
                """, fixture.RootPath, "reservation-original-" + configuration[0]);
            Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
            Assert.Equal(observations, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
