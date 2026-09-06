using System.Runtime.InteropServices;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private RunnerHousekeepingStepResult CleanWindowsComponentStore(string workingDirectory, bool dryRun)
    {
        const string id = "windows-component-store";
        const string title = "Cleanup Windows component store";

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return SkippedStep(id, title, "Windows component-store cleanup is supported only on Windows runners.");

        if (!CommandExists("dism"))
            return SkippedStep(id, title, "dism.exe is unavailable.");

        return RunCommand(
            id,
            title,
            "dism",
            new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/NoRestart" },
            workingDirectory,
            dryRun);
    }
}
