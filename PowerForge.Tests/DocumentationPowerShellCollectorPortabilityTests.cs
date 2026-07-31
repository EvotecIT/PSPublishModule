using System.Text;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void DocumentationEngine_CollectsOnlyPortablePointerAndReflectedTypeDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-portable-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "PortableDefaultsFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'PortableDefaultsFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '88888888-8888-8888-8888-888888888888'
    FunctionsToExport = @('Get-PortableDefaultsFixture')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "PortableDefaultsFixture.psm1"), """
$assemblyName = [System.Reflection.AssemblyName]::new('PortableDefaultsFixtureDynamic')
$factory = [System.Reflection.Emit.AssemblyBuilder].GetMethods(
    [System.Reflection.BindingFlags]'Public,Static') |
    Where-Object { $_.Name -eq 'DefineDynamicAssembly' -and $_.GetParameters().Count -eq 2 } |
    Select-Object -First 1
if ($factory) {
    $assemblyBuilder = [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
        $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
} else {
    $assemblyBuilder = [System.AppDomain]::CurrentDomain.DefineDynamicAssembly(
        $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
}
$moduleBuilder = $assemblyBuilder.DefineDynamicModule('PortableDefaultsFixtureDynamic')
$typeBuilder = $moduleBuilder.DefineType(
    ('PortableDefaultsFixture.Control' + [char]1), [System.Reflection.TypeAttributes]::Public)
$script:xmlInvalidType = if ($typeBuilder.PSObject.Methods['CreateTypeInfo']) {
    $typeBuilder.CreateTypeInfo().AsType()
} else {
    $typeBuilder.CreateType()
}

function Get-PortableDefaultsFixture {
    [CmdletBinding()]
    param()

    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
        foreach ($entry in @(
            @('XmlInvalidType', [type], $script:xmlInvalidType),
            @('SafeIntPtr', [System.IntPtr], [System.IntPtr]::new(([long]42))),
            @('SafeUIntPtr', [System.UIntPtr], [System.UIntPtr]::new(([uint64]42))),
            @('LargeIntPtr', [System.IntPtr], $(if ([System.IntPtr]::Size -eq 8) { [System.IntPtr]::new(([long]2147483648)) } else { [System.IntPtr]::new(([long]2147483647)) })),
            @('LargeUIntPtr', [System.UIntPtr], $(if ([System.IntPtr]::Size -eq 8) { [System.UIntPtr]::new(([uint64]4294967296)) } else { [System.UIntPtr]::new(([uint32]4294967295)) }))
        )) {
            $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $default = [System.Management.Automation.PSDefaultValueAttribute]::new()
            $default.Value = $entry[2]
            $attributes.Add($default)
            $parameters.Add($entry[0], [System.Management.Automation.RuntimeDefinedParameter]::new($entry[0], $entry[1], $attributes))
        }
        $parameters
    }
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var evaluatorPath = Path.Combine(root, "EvaluatePortableDefaults.ps1");
            File.WriteAllText(evaluatorPath, """
param([string]$ManifestPath, [string]$TypeExpression, [string]$IntPtrExpression, [string]$UIntPtrExpression)
Import-Module -Name $ManifestPath -Force -ErrorAction Stop
$typeValue = & ([scriptblock]::Create($TypeExpression))
if ($typeValue.FullName -cne ('PortableDefaultsFixture.Control' + [char]1)) { throw 'Type value did not round-trip.' }
$intPtrValue = & ([scriptblock]::Create($IntPtrExpression))
if ($intPtrValue.ToInt64() -ne 42) { throw 'IntPtr value did not round-trip.' }
$uintPtrValue = & ([scriptblock]::Create($UIntPtrExpression))
if ($uintPtrValue.ToUInt64() -ne 42) { throw 'UIntPtr value did not round-trip.' }
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var engine = new DocumentationEngine(new ExecutablePowerShellRunner(host, root), new NullLogger());
                var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
                var command = Assert.Single(payload.Commands);

                var xmlInvalidType = Default("XmlInvalidType");
                Assert.DoesNotContain('\u0001', xmlInvalidType);
                Assert.Contains("([char]1)", xmlInvalidType, StringComparison.Ordinal);
                Assert.Equal("[System.IntPtr]::new(([System.Int64]42))", Default("SafeIntPtr"));
                Assert.Equal("[System.UIntPtr]::new(([System.UInt64]42))", Default("SafeUIntPtr"));
                if (Environment.Is64BitOperatingSystem)
                {
                    Assert.True(string.IsNullOrEmpty(Default("LargeIntPtr")));
                    Assert.True(string.IsNullOrEmpty(Default("LargeUIntPtr")));
                }

                var execution = new ExecutablePowerShellRunner(host, root).Run(new PowerShellRunRequest(
                    evaluatorPath,
                    new[] { manifestPath, xmlInvalidType, Default("SafeIntPtr"), Default("SafeUIntPtr") },
                    TimeSpan.FromMinutes(1)));
                Assert.True(execution.ExitCode == 0, execution.StdErr);

                var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                    payload,
                    "PortableDefaultsFixture",
                    Path.Combine(root, "generated-" + host));
                Assert.DoesNotContain('\u0001', File.ReadAllText(mamlPath));

                string Default(string name)
                    => Assert.Single(command.Parameters, parameter => parameter.Name == name).DefaultValue;
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }
}
