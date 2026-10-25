using System.Diagnostics;
using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedLoader_ReusesPhysicalAssemblyThroughDirectoryAliasesAndKeepsCopiesIsolated(bool development)
    {
        var root = Directory.CreateTempSubdirectory("pf-module-path-identity-").FullName;
        var physical = Directory.CreateDirectory(Path.Combine(root, "physical module é")).FullName;
        var alias = Path.Combine(root, "alias");
        try
        {
            CreateModuleDirectoryAlias(alias, physical);
            const string moduleName = "PathIdentityModule";
            const string library = "PathIdentityFixture.dll";
            var module = Directory.CreateDirectory(Path.Combine(physical, "Module")).FullName;
            var binaries = Path.Combine(physical, "Binaries");
            var output = Directory.CreateDirectory(development
                ? Path.Combine(binaries, "Release", "net8.0")
                : Path.Combine(module, "Lib", "Core")).FullName;
            string Quote(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";
            // Compile against the PowerShell process that will import the module, not the test runner's newer runtime.
            var fixture = RunGeneratedModulePowerShell(root, $$"""
                $ErrorActionPreference = 'Stop'
                Add-Type -OutputAssembly {{Quote(Path.Combine(output, library))}} -ReferencedAssemblies ([System.Management.Automation.PSCmdlet].Assembly.Location) -TypeDefinition @'
                using System.Management.Automation;
                [Cmdlet("Get", "PathIdentityValue")]
                public sealed class GetPathIdentityValueCommand : PSCmdlet {
                    [Parameter] public int Value { get; set; }
                    protected override void ProcessRecord() { WriteObject("value:" + Value); }
                }
                '@
                """);
            Assert.True(fixture.ExitCode == 0, fixture.StdOut + fixture.StdErr);
            var copy = Directory.CreateDirectory(Path.Combine(root, "separate copy")).FullName;
            File.Copy(Path.Combine(output, library), Path.Combine(copy, library));
            ModuleBootstrapperGenerator.Generate(module, moduleName,
                new ExportSet(Array.Empty<string>(), new[] { "Get-PathIdentityValue" }, Array.Empty<string>()),
                new[] { library }, handleRuntimes: false, useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" },
                developmentBinaries: development ? new ModuleDevelopmentBinaryBootstrapperOptions(
                    ModuleDevelopmentBinaryMode.Auto, binaries,
                    "PF_PATH_IDENTITY_DEVELOPMENT", "PF_PATH_IDENTITY_CONFIGURATION",
                    new[] { "net8.0" }, new[] { "net472" }) : null);

            var relativeOutput = Path.GetRelativePath(physical, output);
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module {{Quote(Path.Combine(module, moduleName + ".psm1"))}} -Force
                $first = (Get-Command Get-PathIdentityValue -ErrorAction Stop).ImplementingType.Assembly
                Import-Module {{Quote(Path.Combine(alias, "Module", moduleName + ".psm1"))}} -Force
                $second = (Get-Command Get-PathIdentityValue -ErrorAction Stop).ImplementingType.Assembly
                if (-not [object]::ReferenceEquals($first, $second)) { throw 'Alias created a second module assembly' }
                $value = Get-PathIdentityValue -Value 17
                if ($value -ne 'value:17') { throw 'Imported cmdlet is unusable' }
                $loader = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($first).GetType()
                $physical = {{Quote(Path.Combine(output, library))}}
                $alias = {{Quote(Path.Combine(alias, relativeOutput, library))}}
                if ($loader.GetMethod('LoadModules')) {
                    $arguments = New-Object object[] 2
                    $arguments[0] = [string[]]@($physical, $alias)
                    $arguments[1] = 'PathIdentityModule'
                    $group = $loader.GetMethod('LoadModules').Invoke($null, $arguments)
                    if ($group.Length -ne 1 -or -not [object]::ReferenceEquals($first, $group[0])) { throw 'Aliases changed group identity' }
                    $arguments = New-Object object[] 3
                    $arguments[0] = [string[]]@($physical)
                    $arguments[1] = $alias
                    $arguments[2] = 'PathIdentityModule'
                    $member = $loader.GetMethod('LoadModuleFromGroup').Invoke($null, $arguments)
                    if (-not [object]::ReferenceEquals($first, $member)) { throw 'Alias is not recognized as a group member' }
                }
                $distinct = $loader.GetMethod('LoadModule').Invoke($null, @({{Quote(Path.Combine(copy, library))}}, 'SeparateModule'))
                if ([object]::ReferenceEquals($first, $distinct)) { throw 'Separate module copies shared a context' }
                'MODULE-PATH-IDENTITY-OK'
                """;
            var result = RunGeneratedModulePowerShell(root, command);
            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("MODULE-PATH-IDENTITY-OK", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(alias)) Directory.Delete(alias);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateModuleDirectoryAlias(string alias, string physical)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, Path.GetRelativePath(Path.GetDirectoryName(alias)!, physical));
            return;
        }

        // Directory junctions need no developer mode or symlink privilege on Windows.
        var start = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", alias, physical }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail("Junction creation did not finish");
        }
        Assert.True(process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
