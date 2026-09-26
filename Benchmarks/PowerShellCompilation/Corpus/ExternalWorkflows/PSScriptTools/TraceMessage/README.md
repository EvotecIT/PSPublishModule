# Offline trace-message append

`Trace.ps1` is unchanged from PSScriptTools commit `549fa3e3d769532320054fc0b3c8f42df0452361`, authored path `functions/Trace.ps1`, SHA-256 `90073b0a58a6ca86bc3bbb569f77f3561f98496ade65bc49d47bfb3035bce110`. The original MIT license is included.

The fallback check targets the existing message append branch with a fixed session clock and an in-memory dispatcher/text box. It invokes the authored Action callback without opening a window. The complete function remains hosted because its independent WPF/runspace type references are unavailable in the compilation target. This check proves fallback behavior, not execution of a compiled function. WPF rendering, STA scheduling, new-runspace initialization, file export, platform integration, and full-module import remain unqualified.
