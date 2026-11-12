using PowerForge;
using PowerForge.Tests;

public sealed partial class ModuleBootstrapperGeneratorWindowsPowerShellTests
{
    // Windows PowerShell binds assemblies without a strong name by simple name, so the first loaded copy serves
    // every module. The rule: reuse a loaded copy of the same major.minor that is at least as new, warn once;
    // refuse anything else (minor releases change public API) with one actionable message before anything loads.
    [Fact]
    [Trait("Category", "Integration")]
    public void DependencyConflictCheck_ReusesCompatibleLoadedVersionsAndRefusesIncompatibleOnes()
    {
        var host = FindExecutableOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (host is null) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-dependency-rule-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            ModuleLoaderAssets.Write(Path.Combine(root, ModuleLoaderAssets.PackagedFolderName));
            var cases = new (string Name, string Loaded, string Packaged, string Expect)[]
            {
                ("NewerPatch", "1.0.6.0", "1.0.5.0", "reuse"),
                ("OlderPatch", "1.0.5.0", "1.0.6.0", "refuse"),
                ("NewerMinor", "1.1.0.0", "1.0.0.0", "refuse"),
                ("SameVersion", "1.2.0.0", "1.2.0.0", "silent"),
                ("OlderMinor", "1.0.0.0", "1.1.0.0", "refuse"),
                ("NewerMajor", "2.0.0.0", "1.1.0.0", "refuse")
            };
            var ownerLib = Directory.CreateDirectory(Path.Combine(root, "Owner", "Lib", "Default")).FullName;
            File.WriteAllText(Path.Combine(root, "Owner", "Owner.psd1"), "@{ ModuleVersion = '3.0.9' }");
            Directory.CreateDirectory(Path.Combine(root, "Consumer"));
            File.WriteAllText(Path.Combine(root, "Consumer", "Consumer.psd1"), "@{ ModuleVersion = '2.4.0' }");
            foreach (var (name, loaded, packaged, _) in cases)
            {
                EmitVersionedAssembly(ownerLib, "Dep" + name, loaded);
                EmitVersionedAssembly(Directory.CreateDirectory(Path.Combine(root, "Consumer", name)).FullName, "Dep" + name, packaged);
            }

            var script = Path.Combine(root, "Test-Rule.ps1");
            File.WriteAllText(script, "$cases = @(" + string.Join(", ", cases.Select(c => $"@{{ Name = '{c.Name}'; Expect = '{c.Expect}' }}")) + ")\n" + """
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'Loader/Core/PowerForge.ModuleLoader.V1.dll')
$owner = [System.Runtime.Loader.AssemblyLoadContext]::new('owner', $false)
foreach ($case in $cases) { $null = $owner.LoadFromAssemblyPath((Join-Path $PSScriptRoot "Owner/Lib/Default/Dep$($case.Name).dll")) }
foreach ($case in $cases) {
    $check = [PowerForge.ModuleLoader.V1.DependencyConflictCheck]::Evaluate('Consumer', (Join-Path $PSScriptRoot 'Consumer'), [string[]]@((Join-Path $PSScriptRoot "Consumer/$($case.Name)")), [string[]]@())
    $actual = if ($null -ne $check.Error) { 'refuse' } elseif ($check.Warnings.Count -gt 0) { 'reuse' } else { 'silent' }
    if ($actual -ne $case.Expect) { throw "$($case.Name): expected $($case.Expect), got $actual. $($check.Error) $($check.Warnings)" }
    $message = if ($null -ne $check.Error) { $check.Error } else { $check.Warnings -join ' ' }
    if ($actual -ne 'silent' -and ($message -notlike '*Consumer 2.4.0*' -or $message -notlike '*module Owner 3.0.9*')) { throw "$($case.Name) does not name both modules: $message" }
}
'DEPENDENCY_RULE_OK'
""");
            var result = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("DEPENDENCY_RULE_OK", result.StandardOutput);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* loaded fixtures stay locked until the host exits */ }
        }
    }

    // One accelerator name holds one type. When two modules expose the same name from their own assemblies, the
    // first keeps it, the second says so once, and the name passes to the second when the first is removed.
    [Fact]
    [Trait("Category", "Integration")]
    public void GeneratedAccelerators_PassToTheNextModuleWhenTheOwnerIsRemoved()
    {
        var host = FindExecutableOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        if (host is null) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-accelerator-handover-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            ModuleLoaderAssets.Write(Path.Combine(root, ModuleLoaderAssets.PackagedFolderName));
            foreach (var name in new[] { "First", "Second" })
            {
                var lib = Directory.CreateDirectory(Path.Combine(root, name, "Lib")).FullName;
                EmitAssembly(lib, name + "Types", "namespace Fixture { public sealed class Shared { public string Owner => \"" + name + "\"; } }");
                File.WriteAllText(Path.Combine(root, name, name + ".psm1"),
                    "$PowerForgeModuleName = '" + name + "'\n" +
                    ModuleBootstrapperGenerator.BuildModuleLoaderImportBlock("$PSScriptRoot, '..', 'Loader'") + "\n" +
                    "$ModuleAssembly = [System.Runtime.Loader.AssemblyLoadContext]::new('" + name + "', $false).LoadFromAssemblyPath((Join-Path $PSScriptRoot 'Lib/" + name + "Types.dll'))\n" +
                    ModuleBootstrapperGenerator.BuildTypeAcceleratorBlock(AssemblyTypeAcceleratorExportMode.AllowList, ["Fixture.Shared"], [], "(Join-Path $PSScriptRoot 'Lib')"));
            }

            var script = Path.Combine(root, "Test-Handover.ps1");
            File.WriteAllText(script, """
$ErrorActionPreference = 'Stop'
$catalog = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')::Get
Import-Module (Join-Path $PSScriptRoot 'First/First.psm1')
$warnings = @(Import-Module (Join-Path $PSScriptRoot 'Second/Second.psm1') 3>&1 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })
if (@($warnings).Count -ne 1 -or "$($warnings[0])" -notlike 'Second: 1 type accelerator(s) already point to types another module loaded (Fixture.Shared)*') { throw "Expected one summary warning: $($warnings -join ' | ')" }
if ([Fixture.Shared]::new().Owner -ne 'First') { throw 'The first module lost its accelerator.' }
Remove-Module First
if (-not $catalog.ContainsKey('Fixture.Shared') -or $catalog['Fixture.Shared'].Assembly.GetName().Name -ne 'SecondTypes') { throw "The accelerator did not pass to the remaining module: $($catalog['Fixture.Shared'].Assembly.FullName)" }
Remove-Module Second
if ($catalog.ContainsKey('Fixture.Shared')) { throw 'The accelerator outlived its last owner.' }
'ACCELERATOR_HANDOVER_OK'
""");
            var result = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{script}\"", root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("ACCELERATOR_HANDOVER_OK", result.StandardOutput);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* loaded fixtures stay locked until the host exits */ }
        }
    }

    [WindowsTheory]
    [InlineData("ModA", "ModB", "reuse")]
    [InlineData("ModB", "ModA", "refuse-patch")]
    [InlineData("ModD", "ModB", "refuse-minor")]
    [InlineData("ModA", "ModC", "refuse-major")]
    [InlineData("ModA", "ModE", "explain")]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopModules_WithConflictingDependencyVersions_ReuseOrRefuseClearly(string first, string second, string expectation)
    {
        var host = DesktopBindingHost();
        if (!File.Exists(host)) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-desktop-conflict-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var compile = Path.Combine(root, "Compile-Fixtures.ps1");
            File.WriteAllText(compile, """
param([string] $Root)
$ErrorActionPreference = 'Stop'
$automation = [psobject].Assembly.Location
function New-Fixture([string] $Path, [string] $Source, [string[]] $References = @()) {
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $Path)
    Add-Type -TypeDefinition $Source -ReferencedAssemblies $References -OutputAssembly $Path
}
$dependencies = Join-Path $Root 'deps'
New-Fixture "$dependencies\1.0\Dep.dll" '[assembly: System.Reflection.AssemblyVersion("1.0.0.0")] namespace Dep { public static class Lib { public static string Old() { return "old"; } } }'
New-Fixture "$dependencies\1.0e\Dep.dll" '[assembly: System.Reflection.AssemblyVersion("1.0.0.0")] namespace Dep { public static class Lib { public static string Old() { return "old"; } } public class Gone { } }'
New-Fixture "$dependencies\1.0.1\Dep.dll" '[assembly: System.Reflection.AssemblyVersion("1.0.1.0")] namespace Dep { public static class Lib { public static string Old() { return "old"; } public static string New() { return "new"; } } }'
New-Fixture "$dependencies\1.1\Dep.dll" '[assembly: System.Reflection.AssemblyVersion("1.1.0.0")] namespace Dep { public static class Lib { public static string Old() { return "old"; } } }'
New-Fixture "$dependencies\2.0\Dep.dll" '[assembly: System.Reflection.AssemblyVersion("2.0.0.0")] namespace Dep { public static class Lib { public static string Next() { return "next"; } } }'
# ModE derives a public type from Dep.Gone, which the 1.0.1 patch no longer has: loading ModE on ModA's copy fails.
$modules = @{ ModA = @('1.0.1', 'New', ''); ModB = @('1.0', 'Old', ''); ModC = @('2.0', 'Next', ''); ModD = @('1.1', 'Old', ''); ModE = @('1.0e', 'Old', ' public sealed class Derived : Dep.Gone { }') }
foreach ($name in $modules.Keys) {
    $version, $call, $extra = $modules[$name]
    $payload = Join-Path $Root "$name\Lib\Default"
    Copy-Item "$dependencies\$version\Dep.dll" (New-Item -ItemType Directory -Force -Path $payload) -Force
    New-Fixture "$payload\$name.dll" "using System.Management.Automation; namespace $name { [Cmdlet(VerbsCommon.Get, ""$name"")] public sealed class GetCommand : PSCmdlet { protected override void ProcessRecord() { WriteObject(Dep.Lib.$call()); } }$extra }" @($automation, "$payload\Dep.dll")
    Set-Content (Join-Path $Root "$name\$name.psd1") "@{ RootModule = '$name.psm1'; ModuleVersion = '1.0.0'; GUID = '$([guid]::NewGuid())'; CmdletsToExport = @('Get-$name'); FunctionsToExport = @(); AliasesToExport = @() }"
}
""");
            var compiled = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{compile}\" -Root \"{root}\"", root, timeoutMilliseconds: 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + Environment.NewLine + compiled.StandardError);
            foreach (var name in new[] { "ModA", "ModB", "ModC", "ModD", "ModE" })
            {
                ModuleBootstrapperGenerator.Generate(Path.Combine(root, name), name,
                    new ExportSet(Array.Empty<string>(), new[] { "Get-" + name }, Array.Empty<string>()),
                    new[] { name + ".dll" }, handleRuntimes: false, useAssemblyLoadContext: true);
            }

            var scenario = Path.Combine(root, "Test-Conflict.ps1");
            File.WriteAllText(scenario, """
param([string] $Root, [string] $First, [string] $Second, [string] $Expectation)
$ErrorActionPreference = 'Stop'
$expected = @{ ModA = 'new'; ModB = 'old'; ModC = 'next'; ModD = 'old'; ModE = 'old' }
Import-Module (Join-Path $Root "$First\$First.psd1")
if ((& "Get-$First") -ne $expected[$First]) { throw "$First does not work on its own." }
$warnings = @()
$failure = $null
try {
    $warnings = @(Import-Module (Join-Path $Root "$Second\$Second.psd1") -ErrorAction Stop 3>&1 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })
} catch {
    $failure = $_.Exception.Message
}
switch ($Expectation) {
    'reuse' {
        if ($failure) { throw "Compatible dependency was refused: $failure" }
        if ($warnings.Count -ne 1 -or "$($warnings[0])" -notlike "$Second 1.0.0 uses a library that module $First 1.0.0 already loaded: Dep 1.0.1 (ships 1.0.0).*") { throw "Expected one reuse warning: $($warnings -join ' | ')" }
        if ((& "Get-$Second") -ne $expected[$Second]) { throw "$Second does not work with the reused dependency." }
    }
    'explain' {
        if (-not $failure) { throw "$Second loaded although its type derives from one the reused dependency lacks." }
        if ($failure -notlike "$Second 1.0.0 cannot load in this Windows PowerShell session: it does not work with the library versions module $First 1.0.0 already loaded (Dep 1.0.1 (ships 1.0.0)).*Dep.Gone*separate Windows PowerShell session*") { throw "Load failure was not explained: $failure" }
        if (Get-Module $Second) { throw "$Second stayed imported after the failure." }
    }
    default {
        if (-not $failure) { throw 'Incompatible dependency was loaded.' }
        if ($failure -notlike "$Second 1.0.0 cannot load in this Windows PowerShell session:*module $First 1.0.0 already loaded Dep*") { throw "Unclear refusal: $failure" }
        if ($Expectation -eq 'refuse-patch' -and $failure -notlike "*import $Second first*") { throw "Refusal lacks the import-order advice: $failure" }
        if ($Expectation -in 'refuse-minor', 'refuse-major' -and $failure -notlike '*cannot be used together*') { throw "Refusal lacks the incompatible-version advice: $failure" }
        if (Get-Module $Second) { throw "$Second stayed imported after the refusal." }
    }
}
if ((& "Get-$First") -ne $expected[$First]) { throw "$First broke after importing $Second." }
"DESKTOP_CONFLICT_OK:$Expectation"
""");
            var result = RunProcess(host,
                $"-NoLogo -NoProfile -NonInteractive -File \"{scenario}\" -Root \"{root}\" -First {first} -Second {second} -Expectation {expectation}",
                root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("DESKTOP_CONFLICT_OK:" + expectation, result.StandardOutput);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void EmitVersionedAssembly(string directory, string name, string version)
        => EmitAssembly(directory, name, $"[assembly: System.Reflection.AssemblyVersion(\"{version}\")] public static class {name}Marker {{ }}");

    private static void EmitAssembly(string directory, string name, string source)
    {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(name,
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source) },
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var output = File.Create(Path.Combine(directory, name + ".dll"));
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    // Windows PowerShell keeps the first loaded version of a module's own assembly for the session.
    [WindowsTheory]
    [InlineData("1.0.1", "1.0.0", "reuse")]
    [InlineData("1.0.0", "1.0.1", "refuse")]
    [InlineData("1.1.0", "1.0.0", "refuse")]
    [Trait("Category", "Integration")]
    public void GeneratedDesktopModule_SecondVersionInOneSession_ReusesNewerOrAsksForNewSession(string first, string second, string expectation)
    {
        var host = DesktopBindingHost();
        if (!File.Exists(host)) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-desktop-version-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var compile = Path.Combine(root, "Compile-Versions.ps1");
            File.WriteAllText(compile, """
param([string] $Root)
$ErrorActionPreference = 'Stop'
foreach ($version in '1.0.0', '1.0.1', '1.1.0') {
    $payload = New-Item -ItemType Directory -Force -Path (Join-Path $Root "ModS\$version\Lib\Default")
    Add-Type -TypeDefinition "[assembly: System.Reflection.AssemblyVersion(""$version.0"")] namespace ModS { [System.Management.Automation.Cmdlet(""Get"", ""ModS"")] public sealed class GetCommand : System.Management.Automation.PSCmdlet { protected override void ProcessRecord() { WriteObject(""$version""); } } }" -ReferencedAssemblies ([psobject].Assembly.Location) -OutputAssembly (Join-Path $payload 'ModS.dll')
    Set-Content (Join-Path $Root "ModS\$version\ModS.psd1") "@{ RootModule = 'ModS.psm1'; ModuleVersion = '$version'; GUID = '6f1b1d7e-2c34-4a8e-9a63-1b8d2f0c9a11'; CmdletsToExport = @('Get-ModS'); FunctionsToExport = @(); AliasesToExport = @() }"
}
""");
            var compiled = RunProcess(host, $"-NoLogo -NoProfile -NonInteractive -File \"{compile}\" -Root \"{root}\"", root, timeoutMilliseconds: 60000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + Environment.NewLine + compiled.StandardError);
            foreach (var version in new[] { "1.0.0", "1.0.1", "1.1.0" })
            {
                // The accelerator proves registration uses the reused copy quietly instead of warning per type.
                ModuleBootstrapperGenerator.Generate(Path.Combine(root, "ModS", version), "ModS",
                    new ExportSet(Array.Empty<string>(), new[] { "Get-ModS" }, Array.Empty<string>()),
                    new[] { "ModS.dll" }, handleRuntimes: false, useAssemblyLoadContext: true,
                    assemblyTypeAcceleratorMode: AssemblyTypeAcceleratorExportMode.AllowList,
                    assemblyTypeAccelerators: new[] { "ModS.GetCommand" });
            }

            var scenario = Path.Combine(root, "Test-Version.ps1");
            File.WriteAllText(scenario, """
param([string] $Root, [string] $First, [string] $Second, [string] $Expectation)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $Root "ModS\$First\ModS.psd1")
$failure = $null
$warnings = @()
try {
    $warnings = @(Import-Module (Join-Path $Root "ModS\$Second\ModS.psd1") -ErrorAction Stop 3>&1 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })
} catch {
    $failure = $_.Exception.Message
}
if ($Expectation -eq 'reuse') {
    if ($failure) { throw "Newer loaded version was refused: $failure" }
    if ($warnings.Count -ne 1 -or "$($warnings[0])" -notlike "ModS $Second uses a library that module ModS $First already loaded: ModS $First (ships $Second).*") { throw "Expected one reuse warning: $($warnings -join ' | ')" }
    if ([ModS.GetCommand].Assembly.GetName().Version.ToString(3) -ne $First) { throw 'The accelerator does not point at the loaded copy.' }
} else {
    if ($failure -ne "ModS $Second cannot load: ModS $First is already loaded in this Windows PowerShell session, and its assemblies stay loaded after Remove-Module. Start a new Windows PowerShell session to use ModS $Second.") { throw "Unclear refusal: $failure" }
}
if ((Get-ModS) -ne $First) { throw 'The first loaded version stopped serving its command.' }
"DESKTOP_VERSION_OK:$Expectation"
""");
            var result = RunProcess(host,
                $"-NoLogo -NoProfile -NonInteractive -File \"{scenario}\" -Root \"{root}\" -First {first} -Second {second} -Expectation {expectation}",
                root, timeoutMilliseconds: 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + Environment.NewLine + result.StandardError);
            Assert.Contains("DESKTOP_VERSION_OK:" + expectation, result.StandardOutput);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
