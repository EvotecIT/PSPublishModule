# PowerForge Compiler app

A small desktop window over the PowerShell compiler: drop a script, module file or module folder, see what Package, Hybrid and Strict would produce and why, pick a target, and build it.

```powershell
dotnet run --project PowerForge.Compiler.App -- ./MyScript.ps1
```

The path argument is optional; you can also drop a file on the window or use Open. Output goes to `artifacts` next to the input unless you pick a folder.

**Edit details** opens a side panel for the files that go into the output (the script and what it loads, the whole folder, or only the script, plus include and leave-out patterns) and for saved settings. Settings are the engine's own `powerforge.psproject.json`, so a file saved here builds the same way with `powerforge powershell project build`. Saving adds the selected mode and target and keeps any targets already in the file. Opening a settings file, or passing or dropping one, checks the script it names with the saved files, mode, target and output folder.

## How it talks to the engine

The window only uses `Engine/ICompileEngine.cs`, which mirrors `Check` and `Compile` from [the host API proposal](../Docs/PowerForge.PowerShellCompilation.HostApi.md). `Engine/InterimCompileEngine.cs` maps today's compiler services onto it:

- `Check` resolves the input once, then analyses and explains it in each mode. Engine refusals that arrive as exceptions become a blocked mode with an error.
- `Compile` builds with the same defaults as `powerforge powershell build`. It allows unreviewed dependency resolution, so every build is a development build until the engine produces a reviewed lock from `Check`.
- Targets come from the compiler's support matrix.

When the engine gains its host service, `InterimCompileEngine` becomes a direct call and the window stays as it is.
