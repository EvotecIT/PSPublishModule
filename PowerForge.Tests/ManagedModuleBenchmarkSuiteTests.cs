using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkSuiteTests
{
    private static readonly Lazy<System.Management.Automation.Runspaces.Runspace> BenchmarkDslRunspace = new(CreateBenchmarkDslRunspace);

    [Fact]
    public void BenchmarkFolder_ContainsReusableSuiteSpec()
    {
        var folder = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules");
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(static name => name).ToArray();

        Assert.Equal(
            new[]
            {
                "managed-modules.benchmark.ps1",
                "README.md"
            },
            files);
    }

    [Fact]
    public void ManagedModuleSuiteSpec_UsesGenericBenchmarkDsl()
    {
        var path = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "managed-modules.benchmark.ps1");
        var text = File.ReadAllText(path);
        var lines = File.ReadLines(path).Where(static line => !string.IsNullOrWhiteSpace(line)).Count();

        Assert.True(lines <= 210, "Managed module benchmark spec should stay readable and data-driven.");
        Assert.Contains("benchmark 'managed-modules'", text, StringComparison.Ordinal);
        Assert.Contains("caseSource", text, StringComparison.Ordinal);
        Assert.Contains("engine Managed", text, StringComparison.Ordinal);
        Assert.Contains("operation Install", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleFastCSharp", text, StringComparison.Ordinal);
        Assert.DoesNotContain("New-ManagedModuleBenchmarkSuite", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-BenchmarkSuite", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PSPUBLISHMODULE_BENCHMARK_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("function ", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedModuleSuite_PlansFocusedInstallComparison()
    {
        var suite = LoadSuite(new PowerShellBenchmarkSelection
        {
            Engines = new[] { "Managed", "ModuleFast" },
            Operations = new[] { "Install" }
        });
        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Equal("managed-modules", suite.Name);
        Assert.Equal(PowerShellBenchmarkProfileKind.Current, suite.Profile);
        Assert.Equal(new[] { "Managed", "ModuleFast" }, suite.Axes.Single(a => a.Name == "Engine").Values.Select(static v => v?.ToString()).ToArray());
        Assert.Equal(new[] { "Install" }, suite.Axes.Single(a => a.Name == "Operation").Values.Select(static v => v?.ToString()).ToArray());
        Assert.Equal(10, plan.Length);
        Assert.Contains(plan, item => item.Scenario == "SingleModule" && item.Engine == "Managed" && item.Operation == "Install");
        Assert.Contains(plan, item => item.Scenario == "Az" && item.Engine == "ModuleFast" && item.Operation == "Install");
        Assert.DoesNotContain(plan, item => item.Operation == "Repair");
    }

    [Fact]
    public void ManagedModuleSuite_DropsComparisonWhenBaselineEngineIsFilteredOut()
    {
        var suite = LoadSuite(new PowerShellBenchmarkSelection
        {
            Engines = new[] { "ModuleFast" },
            Operations = new[] { "Install" },
            Cases = new[] { "SingleModule" }
        });

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Single(plan);
        Assert.Empty(suite.Comparisons);
    }

    [Fact]
    public void ManagedModuleSuite_ExplicitHostSelectionKeepsHostMatrix()
    {
        var suite = LoadSuite(new PowerShellBenchmarkSelection
        {
            Cases = new[] { "SingleModule" },
            Engines = new[] { "Managed", "ModuleFast" },
            Operations = new[] { "Install" },
            Hosts = new[] { "Core", "Desktop" }
        });

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Equal(4, plan.Length);
        Assert.Contains(plan, item => item.Host.StartsWith("Core-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan, item => item.Host == "Desktop");
    }

    [Fact]
    public void ManagedModuleSuite_CanPlanThroughRealBenchmarkCommandsAndAliases()
    {
        var path = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "managed-modules.benchmark.ps1");
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault2();
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        ps.AddCommand("Import-Module").AddArgument(typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).Assembly.Location);
        ps.Invoke();
        Assert.Empty(ps.Streams.Error);
        ps.Commands.Clear();
        ps.AddCommand("Get-Command").AddArgument("benchmark");
        var alias = Assert.Single(ps.Invoke());
        Assert.Equal("Alias", alias.Properties["CommandType"].Value?.ToString());
        ps.Commands.Clear();
        ps.AddCommand("Invoke-BenchmarkSuite")
            .AddParameter("Path", path)
            .AddParameter("Plan")
            .AddParameter("Scenario", new[] { "SingleModule" })
            .AddParameter("Operation", new[] { "Install" })
            .AddParameter("Engine", new[] { "Managed" });

        var output = ps.Invoke();

        Assert.Empty(ps.Streams.Error);
        var item = Assert.IsType<PowerShellBenchmarkWorkItem>(Assert.Single(output).BaseObject);
        Assert.Equal("SingleModule", item.Scenario);
        Assert.Equal("Managed", item.Engine);
        Assert.Equal("Install", item.Operation);
    }

    [Fact]
    public void ManagedModuleSuite_FullMatrixMarksNonEquivalentModuleFastOperationsSkipped()
    {
        var suite = LoadSuite(new PowerShellBenchmarkSelection
        {
            Cases = new[] { "SingleModule" },
            Operations = new[] { "Find", "Install", "Save" },
            Engines = new[] { "Managed", "ModuleFast", "PSResourceGet", "PowerShellGet" }
        });

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Equal(12, plan.Length);
        Assert.Contains(plan, item => item.Engine == "ModuleFast" && item.Operation == "Find" && item.IsSkipped);
        Assert.Contains(plan, item => item.Engine == "ModuleFast" && item.Operation == "Save" && item.IsSkipped);
        Assert.Contains(plan, item => item.Engine == "Managed" && item.Operation == "Find");
        Assert.Contains(plan, item => item.Engine == "PSResourceGet" && item.Operation == "Save");
        Assert.Contains(suite.Comparisons, comparison => comparison.Baseline == "Managed" && comparison.Metrics.Contains("MedianMs"));
    }

    [Fact]
    public void ManagedModuleSuite_TemporaryProfileEnablesNativeInstallPlanning()
    {
        var suite = LoadSuite(
            new PowerShellBenchmarkSelection
            {
                Cases = new[] { "SingleModule" },
                Operations = new[] { "Install" },
                Engines = new[] { "Managed", "PSResourceGet", "PowerShellGet" }
            },
            profile: PowerShellBenchmarkProfileKind.TemporaryLocalUser);

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Equal(PowerShellBenchmarkProfileKind.TemporaryLocalUser, suite.Profile);
        Assert.Contains(plan, item => item.Engine == "PSResourceGet" && item.Operation == "Install" && !item.IsSkipped);
        Assert.Contains(plan, item => item.Engine == "PowerShellGet" && item.Operation == "Install" && !item.IsSkipped);
    }

    [Fact]
    public void ManagedModuleSuite_CurrentProfileSkipsNativeInstalls()
    {
        var suite = LoadSuite(new PowerShellBenchmarkSelection
        {
            Cases = new[] { "SingleModule" },
            Operations = new[] { "Install" },
            Engines = new[] { "Managed", "PSResourceGet", "PowerShellGet" }
        });

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Contains(plan, item => item.Engine == "PSResourceGet" && item.Operation == "Install" && item.IsSkipped);
        Assert.Contains(plan, item => item.Engine == "PowerShellGet" && item.Operation == "Install" && item.IsSkipped);
    }

    private static PowerShellBenchmarkSuite LoadSuite(
        PowerShellBenchmarkSelection? selection = null,
        PowerShellBenchmarkProfileKind? profile = null,
        IReadOnlyDictionary<string, string?>? variables = null)
    {
        var path = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "managed-modules.benchmark.ps1");
        var scriptRoot = Path.GetDirectoryName(path)!;
        var suites = EvaluateBenchmarkDsl(System.Management.Automation.ScriptBlock.Create(File.ReadAllText(path)), scriptRoot, variables);
        var suite = Assert.Single(suites);
        if (profile.HasValue)
            suite.Profile = profile.Value;
        PowerShellBenchmarkSuiteFilter.Apply(suite, selection);
        return suite;
    }

    private static PowerShellBenchmarkSuite[] EvaluateBenchmarkDsl(
        System.Management.Automation.ScriptBlock scriptBlock,
        string? scriptRoot,
        IReadOnlyDictionary<string, string?>? variables = null)
    {
        var previousRunspace = System.Management.Automation.Runspaces.Runspace.DefaultRunspace;
        var runspace = BenchmarkDslRunspace.Value;
        System.Management.Automation.Runspaces.Runspace.DefaultRunspace = runspace;
        try
        {
            using var powerShell = System.Management.Automation.PowerShell.Create(runspace);
            powerShell.AddCommand("Import-Module")
                .AddArgument(typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).Assembly.Location)
                .AddParameter("Force");
            powerShell.Invoke();
            if (powerShell.HadErrors)
            {
                var message = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(static error => error.ToString()));
                throw new InvalidOperationException("Failed to import PSPublishModule benchmark commands for test evaluation." + Environment.NewLine + message);
            }

            return PowerShellBenchmarkDslRuntime.Evaluate(scriptBlock, scriptRoot, variables);
        }
        finally
        {
            System.Management.Automation.Runspaces.Runspace.DefaultRunspace = previousRunspace;
        }
    }

    private static System.Management.Automation.Runspaces.Runspace CreateBenchmarkDslRunspace()
    {
        var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(System.Management.Automation.Runspaces.InitialSessionState.CreateDefault2());
        runspace.Open();
        return runspace;
    }
}
