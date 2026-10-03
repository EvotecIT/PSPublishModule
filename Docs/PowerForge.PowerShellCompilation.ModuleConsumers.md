# Consume a compiled PowerShell module

Hybrid compiled modules retain PowerShell command resolution and export behavior while emitting qualified CLR methods. Use PowerShell 7.6 for `net10.0` artifacts and Windows PowerShell 5.1 for `net472` artifacts.

Build the local CLI as shown in the [development guide](PowerForge.PowerShellCompilation.Development.md) and keep its DLL path in `$powerforge`. Run the following commands in a clean project directory on Windows. Spaces, apostrophes, and non-ASCII characters are qualified in the module workflow. Keep the final artifact and launch directory compact; isolated restore workspaces support deep internal package paths within the [tested Windows path boundary](PowerForge.PowerShellCompilation.Assessment.md#windows-distribution-checkpoint).

Save this as `Example.psm1`:

```powershell
function Get-CompiledValue { return 1 }
function Get-FallbackValue { return [int](Get-Date -Format yyyy) }
function Get-PrivateValue { return 2 }
```

Save this as `Example.psd1` beside it:

```powershell
@{
    RootModule        = 'Example.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = '2dc63369-e036-418c-9ded-d4e542240e1b'
    FunctionsToExport = @('Get-CompiledValue', 'Get-FallbackValue')
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
```

The manifest controls the exported surface. Hybrid mode emits eligible functions as CLR methods and retains source requiring the PowerShell runtime. `Get-FallbackValue` emits a method with a hosted `Get-Date` command region; this example requires PowerShell and makes no performance claim.

## Build, inspect, and import

From the directory containing the two files:

```powershell
dotnet $powerforge powershell project init ./Example.psd1 --name Example --kind dll --mode Hybrid --framework net10.0 --emit-source
dotnet $powerforge powershell project explain ./powerforge.psproject.json
dotnet $powerforge powershell project lock ./powerforge.psproject.json
dotnet $powerforge powershell project restore ./powerforge.psproject.json
dotnet $powerforge powershell project restore ./powerforge.psproject.json --offline

$build = dotnet $powerforge powershell project build ./powerforge.psproject.json --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $build.success) { throw 'Compiled module build failed.' }
$module = Import-Module -Name $build.result.targets[0].path -Force -PassThru
Get-Command -Module $module.Name | Select-Object -ExpandProperty Name
Get-CompiledValue
Get-FallbackValue
Get-Command Get-PrivateValue -ErrorAction SilentlyContinue
Remove-Module -Name $module.Name
```

The first two commands appear in `Get-Command`; the private function does not. `Get-CompiledValue` returns `1`. Despite its example name, `Get-FallbackValue` also emits as a CLR method: it invokes `Get-Date` through one hosted command region, then converts the returned year to an integer. Its per-function disposition reports an emitted method with a runtime command region, rather than retained function source. Unqualified `Get-Date` uses the importing session's command lookup, so an authored function or alias can override it. This Hybrid example requires PowerShell.

Qualify the exact reviewed revision before packaging:

```powershell
dotnet $powerforge powershell project test ./powerforge.psproject.json
dotnet $powerforge powershell project diagnose ./powerforge.psproject.json
dotnet $powerforge powershell project pack ./powerforge.psproject.json
dotnet $powerforge powershell project install ./powerforge.psproject.json
```

`test` performs a clean module import. `diagnose` checks source decisions and the authenticated artifact, lock, and environment. `pack` creates the qualified project ZIP; `install` places that ZIP's verified artifact set under the project's content-addressed installation directory. These commands do not publish to PowerShell Gallery.

## Edit and rebuild

Change `Get-CompiledValue` to return `2`. `project explain` reads the new source, but `project diagnose` and `project build` reject the stale reviewed revision. Review the edit, then run:

```powershell
dotnet $powerforge powershell project lock ./powerforge.psproject.json
dotnet $powerforge powershell project restore ./powerforge.psproject.json --offline
dotnet $powerforge powershell project build ./powerforge.psproject.json
dotnet $powerforge powershell project test ./powerforge.psproject.json
dotnet $powerforge powershell project diagnose ./powerforge.psproject.json
dotnet $powerforge powershell project pack ./powerforge.psproject.json
```

Import the newly built manifest in a fresh PowerShell session to avoid an already-loaded assembly. It now returns `2`. Do not infer in-process replacement from a successful rebuild.

## Windows PowerShell 5.1

Use a **separate copy** of the same source and manifest for a `net472` project. Run the same commands above with `--framework net472` at init, then import its built manifest from a Windows PowerShell 5.1 session. Keep the `net10.0` and `net472` projects, locks, environments, and artifacts separate; a restored environment belongs to its own manifest and target.

Keep the target-specific qualification evidence with the packaged project. The Windows x64 baseline uses PowerShell 7.6.6/`net10.0` and Windows PowerShell 5.1.26100.9444/`net472`; the [assessment](PowerForge.PowerShellCompilation.Assessment.md) records the observed exports, values, command shadowing, and project lifecycle checks. Additional operating systems and architectures require their own execution evidence.

The generated project includes PDBs and a portable `source-map.json` under its emitted source directory. A working authored-source breakpoint, stepping, locals, stack, and exception path has **not** yet been observed. Do not use PDB presence or the map alone as debugger qualification; that [developer-tooling gate](PowerForge.PowerShellCompilation.NextMilestones.md) is deferred from the coverage milestone.
