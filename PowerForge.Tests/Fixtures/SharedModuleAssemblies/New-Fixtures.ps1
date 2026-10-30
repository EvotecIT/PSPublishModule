param([Parameter(Mandatory = $true)][string] $Root)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $Root -Force | Out-Null
foreach ($feature in 0, 1) {
    $corePath = Join-Path $Root "Core$feature.dll"
    # OutputAssembly compiles without importing either version into this fixture-builder process.
    $coreSource = @"
using System.Reflection;
[assembly: AssemblyVersion("2.0.1.0")]
[assembly: AssemblyInformationalVersion("2.1.$feature")]
[assembly: AssemblyMetadata("FeatureLevel", "$feature")]
namespace SharedCore {
    public static class Runtime {
        public static int Feature { get { return $feature; } }
        $(if ($feature -eq 1) { 'public static string NewFeature() { return "rich"; }' })
    }
}
"@
    # Compile in a directory with the real assembly simple name.
    $dir = New-Item -ItemType Directory -Path (Join-Path $Root "Core$feature") -Force
    Add-Type -TypeDefinition $coreSource -OutputAssembly (Join-Path $dir.FullName 'SharedCore.dll')
    $displaySource = @"
using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("2.1.$feature")]
[assembly: AssemblyMetadata("FeatureLevel", "$feature")]
namespace SharedDisplay {
    public static class Broker {
        public static string Register() { return "display${feature}:" + SharedCore.Runtime.Feature; }
    }
}
"@
    Add-Type -TypeDefinition $displaySource -ReferencedAssemblies (Join-Path $dir.FullName 'SharedCore.dll') -OutputAssembly (Join-Path $dir.FullName 'SharedDisplay.dll')
}
foreach ($product in 'Alpha', 'Beta', 'Gamma') {
    $dir = New-Item -ItemType Directory -Path (Join-Path $Root $product) -Force
    $dependencySource = @"
namespace ${product}Private {
    public static class State { public static string Value { get { return "$product-private"; } } }
}
"@
    $privatePath = Join-Path $dir.FullName "$product.Private.dll"
    Add-Type -TypeDefinition $dependencySource -OutputAssembly $privatePath
    $hostSource = @"
using System;
using System.Linq;
using System.Management.Automation;
namespace $product {
    public sealed class Data {
        public int Feature { get; set; }
        public string Private { get; set; }
    }
    [Cmdlet(VerbsCommon.Get, "$product`Data")]
    public sealed class GetDataCommand : PSCmdlet {
        protected override void ProcessRecord() {
            WriteObject(new Data { Feature = SharedCore.Runtime.Feature, Private = ${product}Private.State.Value });
        }
    }
    public sealed class Import : IModuleAssemblyInitializer {
        public void OnImport() {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SharedCore"))
                throw new InvalidOperationException("Core must be preloaded before data-host initialization.");
        }
    }
}
"@
    $references = @((Join-Path $Root 'Core1\SharedCore.dll'), $privatePath, [System.Management.Automation.PSCmdlet].Assembly.Location, 'System.Core.dll')
    Add-Type -TypeDefinition $hostSource -ReferencedAssemblies $references -OutputAssembly (Join-Path $dir.FullName "$product.Host.dll")
    # This method cannot JIT against the old core. Product preflight must prevent loading it.
    $presentationSource = @"
namespace ${product}Presentation {
    public static class View {
        public static string Render() { return SharedCore.Runtime.NewFeature() + SharedDisplay.Broker.Register(); }
    }
}
"@
    Add-Type -TypeDefinition $presentationSource -ReferencedAssemblies @((Join-Path $Root 'Core1\SharedCore.dll'), (Join-Path $Root 'Core1\SharedDisplay.dll')) -OutputAssembly (Join-Path $dir.FullName "$product.Presentation.dll")
}
'FIXTURES_OK'
