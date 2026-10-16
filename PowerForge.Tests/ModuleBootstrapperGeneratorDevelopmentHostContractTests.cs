using Generic.External.Binary.Dependency;
using Generic.External.Binary.Module;
using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void DevelopmentAssemblyLoadContext_UsesHostPowerShellContractWhenOutputContainsPrivateCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-development-host-contract-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
        var binaryRoot = Path.Combine(root, "Binaries");
        var output = Directory.CreateDirectory(Path.Combine(binaryRoot, "Release", "net8.0")).FullName;
        try
        {
            const string moduleName = "HostContractModule";
            const string libraryName = "Generic.External.Binary.Module.dll";
            File.Copy(typeof(GetGenericExternalValueCommand).Assembly.Location, Path.Combine(output, libraryName));
            File.Copy(typeof(GenericValueSource).Assembly.Location, Path.Combine(output, "Generic.External.Binary.Dependency.dll"));
            // A development output may contain PowerShell SDK runtime assets even though
            // the packaged module excludes the host's automation assembly.
            File.Copy(typeof(System.Management.Automation.PSCmdlet).Assembly.Location,
                Path.Combine(output, "System.Management.Automation.dll"));
            var exports = new ExportSet(Array.Empty<string>(), new[] { "Get-GenericExternalValue" }, Array.Empty<string>());
            ModuleBootstrapperGenerator.Generate(moduleRoot, moduleName, exports, new[] { libraryName },
                handleRuntimes: false, useAssemblyLoadContext: true,
                developmentBinaries: new ModuleDevelopmentBinaryBootstrapperOptions(
                    ModuleDevelopmentBinaryMode.Auto, binaryRoot,
                    "PF_HOST_CONTRACT_DEVELOPMENT", "PF_HOST_CONTRACT_CONFIGURATION",
                    new[] { "net8.0" }, new[] { "net472" }));
            var result = RunGeneratedModulePowerShell(root,
                "$ErrorActionPreference = 'Stop'; Import-Module -Name '" +
                Path.Combine(moduleRoot, moduleName + ".psm1").Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; Get-Command -Module HostContractModule -Name Get-GenericExternalValue -ErrorAction Stop | Out-Null; " +
                "$record = Get-GenericExternalValue -Value 17; 'host-contract:' + $record.DependencyValue");
            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("host-contract:dependency:17", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
