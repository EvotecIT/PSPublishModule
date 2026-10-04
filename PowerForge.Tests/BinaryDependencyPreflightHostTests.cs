using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class BinaryDependencyPreflightHostTests
{
    [Fact]
    public void CoreInventory_UsesTheResolvedHostAndIsReusedWithinTheBuild()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "System.Security.Cryptography.dll"), "host inventory fixture");
            var runner = new InventoryRunner(new PowerShellRunResult(0, root.FullName + Environment.NewLine, "", "pwsh"));
            var service = new BinaryDependencyPreflightService(new NullLogger(), runner);

            var first = service.ResolveCoreHostAssemblyNames();
            var second = service.ResolveCoreHostAssemblyNames();

            Assert.Contains("System.Security.Cryptography", first);
            Assert.DoesNotContain("Dependency", first);
            Assert.Same(first, second);
            Assert.Equal(1, runner.Calls);
            Assert.NotNull(runner.Request);
            Assert.True(runner.Request.RequiredRuntimeMajor > 0);
            Assert.Equal("[Console]::WriteLine($PSHOME)", runner.Request.CommandText);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void CoreInventory_DoesNotFallBackToDesktopWhenCoreDiscoveryFails()
    {
        var runner = new InventoryRunner(new PowerShellRunResult(1, "", "No compatible PowerShell Core host found.", "pwsh"));
        var service = new BinaryDependencyPreflightService(new NullLogger(), runner);

        var error = Assert.Throws<InvalidOperationException>(() => service.ResolveCoreHostAssemblyNames());

        Assert.Contains("PowerShell Core runtime", error.Message);
        Assert.Equal(1, runner.Calls);
    }

    private sealed class InventoryRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;
        public int Calls { get; private set; }
        public PowerShellRunRequest? Request { get; private set; }

        public InventoryRunner(PowerShellRunResult result) => _result = result;

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            Calls++;
            Request = request;
            return _result;
        }
    }
}
