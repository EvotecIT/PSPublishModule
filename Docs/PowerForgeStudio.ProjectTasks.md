# Project tasks in PowerForge Studio

Studio can run explicit commands from a repository's `Build/powerforge.tasks.json`. Use this for project-specific PowerShell, .NET, executable, or other command-line steps that do not already have a PowerForge package-build contract. The file is optional; a repository without it still uses the usual Build & Run inspection.

```json
{
  "SchemaVersion": 1,
  "Tasks": [
    {
      "Id": "build-project",
      "Name": "Build project",
      "Description": "Run the repository's existing build script.",
      "Executable": "pwsh",
      "Arguments": ["-NoProfile", "-NonInteractive", "-File", "Build/Build-Project.ps1"],
      "WorkingDirectory": ".",
      "TimeoutSeconds": 1800
    },
    {
      "Id": "check-sdk",
      "Name": "Check .NET SDK",
      "Executable": "dotnet",
      "Arguments": ["--version"],
      "WorkingDirectory": ".",
      "TimeoutSeconds": 30
    }
  ]
}
```

Open the repository in Studio, choose **Build & Run**, then **Inspect & plan**. Select a task to review its exact executable, each separate argument, working directory and timeout. **Run selected task** starts only that task and displays its output, result and captured working-copy path. **Cancel task** requests process cancellation; an interrupted command may leave partial project changes.

`Id`, `Name` and `Executable` are required. `Description`, `Arguments`, `WorkingDirectory` and `TimeoutSeconds` are optional; the defaults are no arguments, the repository root and 600 seconds. Use an executable name available on `PATH`, an absolute executable path, or a relative executable path inside the working copy. The working directory must exist inside the working copy. Arguments are passed as individual process arguments, without shell expansion. For PowerShell scripts, invoke `pwsh` explicitly as above; JSON task configuration does not interpret `.ps1` files itself.

Studio validates the JSON and fingerprints it when inspected. If it changes before execution, Studio refuses the old selection and asks for another inspection. It also rechecks immediately before the process starts. Switching projects does not redirect a running task: the run remains tied to its original working copy. Build and release operations cannot start in parallel with a task in the same Studio session. Studio owns the task's process tree: when the command exits or is cancelled, background descendants started by that command are stopped as well. Run persistent services outside this task menu.

Project tasks execute trusted local code. A task may change files, call external services, sign, publish, or consume credentials inherited from Studio's process environment. The regular **Build current configuration** action keeps publication disabled, but that guard cannot constrain an arbitrary task. Review task JSON before running it, keep credentials and tokens out of JSON arguments and task names, and use the owning tool's credential store or environment at execution time. Studio's Connections page reports capability without revealing secrets; this task format has no secret or environment-variable fields.

This task file is an execution menu, not a replacement for PowerForge's JSON module, NuGet, MSI, signing or release specifications. Use the existing package and release configuration when you need their plan, artifact tracking, publication approval and durable receipts. A task's stdout and exit code are visible in Build & Run, but its arbitrary side effects do not become verified PowerForge release receipts.
