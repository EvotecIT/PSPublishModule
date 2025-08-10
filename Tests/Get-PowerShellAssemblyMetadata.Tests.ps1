Describe 'Get-PowerShellAssemblyMetadata' {
    It 'loads cmdlets when runtime contains a different assembly version' {
        $runtimeDir = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
        $testRoot = Join-Path $TestDrive 'metadata'
        New-Item -Path $testRoot -ItemType Directory | Out-Null

        # Build runtime version of dependency
        $projRuntime = Join-Path $testRoot 'DummyDependencyRuntime'
        dotnet new classlib -n DummyDependency -f net8.0 -o $projRuntime | Out-Null
        Set-Content -Path (Join-Path $projRuntime 'Class1.cs') -Value 'namespace DummyDep { public class Dummy {} }'
        dotnet build $projRuntime -c Release -p:Version=1.0.0 | Out-Null
        $runtimeAssemblyPath = Join-Path $runtimeDir 'DummyDependency.dll'
        Copy-Item -Path (Join-Path $projRuntime 'bin/Release/net8.0/DummyDependency.dll') -Destination $runtimeAssemblyPath -Force

        try {
            # Build assembly version of dependency
            $projDep = Join-Path $testRoot 'DummyDependency'
            dotnet new classlib -n DummyDependency -f net8.0 -o $projDep | Out-Null
            Set-Content -Path (Join-Path $projDep 'Class1.cs') -Value 'namespace DummyDep { public class Dummy {} }'
            dotnet build $projDep -c Release -p:Version=2.0.0 | Out-Null
            Copy-Item -Path (Join-Path $projDep 'bin/Release/net8.0/DummyDependency.dll') -Destination (Join-Path $testRoot 'DummyDependency.dll')

            # Build test module referencing dependency
            $projTest = Join-Path $testRoot 'TestModule'
            dotnet new classlib -n TestModule -f net8.0 -o $projTest | Out-Null
            Set-Content -Path (Join-Path $projTest 'Class1.cs') -Value @'
using System.Management.Automation;
using DummyDep;
namespace TestModule {
  [Cmdlet("Get","Sample")]
  public class GetSample : Cmdlet {
    protected override void ProcessRecord() { WriteObject("hi"); }
  }
}
'@
            dotnet add (Join-Path $projTest 'TestModule.csproj') reference (Join-Path $projDep 'DummyDependency.csproj') | Out-Null
            dotnet add (Join-Path $projTest 'TestModule.csproj') package Microsoft.PowerShell.SDK --version 7.4.1 | Out-Null
            dotnet build $projTest -c Release | Out-Null
            Copy-Item -Path (Join-Path $projTest 'bin/Release/net8.0/TestModule.dll') -Destination (Join-Path $testRoot 'TestModule.dll')

            $result = Get-PowerShellAssemblyMetadata -Path (Join-Path $testRoot 'TestModule.dll')
            $result.CmdletsToExport | Should -Contain 'Get-Sample'
        }
        finally {
            Remove-Item -Path $runtimeAssemblyPath -ErrorAction SilentlyContinue
        }
    }
}
