# PowerForge Compiler app

A small desktop window over the PowerShell compiler. Drop a script, module file or module folder, see what Package, Hybrid and Strict would produce, and pick a target.

```powershell
dotnet run --project PowerForge.Compiler.App -- ./MyScript.ps1
```

The path argument is optional; you can also drop a file on the window or use Open. Output goes to `artifacts` next to the input unless you pick a folder.

Select **Review dependencies** to check the selected mode, target and file rules. The Dependencies tab shows what the build includes and what the target machine needs. **Confirm and compile** builds from that exact review. Changing the selection clears the review, and changed inputs require a fresh review when you build.

**Edit details** opens a side panel for the files that go into the output (the script and what it loads, the whole folder, or only the script, plus include and leave-out patterns) and for saved settings. Settings are the engine's own `powerforge.psproject.json`, so a file saved here builds the same way with `powerforge powershell project build`. Saving adds the selected mode and target and keeps any targets already in the file. Opening a settings file, or passing or dropping one, checks the script it names with the saved files, mode, target and output folder.

## How it talks to the engine

The window uses the same `PowerShellCompilationHostService` as the CLI. It calls `Check` for the mode previews and the selected build, `Doctor` for the selected build's prerequisites, and `Compile` with the confirmed review. The target picker uses the compiler's support matrix. See the [host API and packaging roadmap](../Docs/PowerForge.PowerShellCompilation.HostApi.md).
