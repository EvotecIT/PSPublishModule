using System;
using System.IO;

namespace PowerForge;

internal enum ModuleStateDirectoryProbeStatus
{
    Missing,
    Available,
    Inaccessible
}

internal readonly struct ModuleStateDirectoryProbeResult
{
    internal ModuleStateDirectoryProbeResult(ModuleStateDirectoryProbeStatus status, string? reason = null)
    {
        Status = status;
        Reason = reason;
    }

    internal ModuleStateDirectoryProbeStatus Status { get; }

    internal string? Reason { get; }
}

internal static class ModuleStateDirectoryProbe
{
    internal static ModuleStateDirectoryProbeResult Probe(string path)
    {
        if (Directory.Exists(path))
            return new ModuleStateDirectoryProbeResult(ModuleStateDirectoryProbeStatus.Available);

        try
        {
            var attributes = File.GetAttributes(path);
            var reason = (attributes & FileAttributes.Directory) != 0
                ? "The directory exists but could not be inspected."
                : "The path exists but is not a directory.";
            return new ModuleStateDirectoryProbeResult(ModuleStateDirectoryProbeStatus.Inaccessible, reason);
        }
        catch (FileNotFoundException)
        {
            return new ModuleStateDirectoryProbeResult(ModuleStateDirectoryProbeStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new ModuleStateDirectoryProbeResult(ModuleStateDirectoryProbeStatus.Missing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new ModuleStateDirectoryProbeResult(ModuleStateDirectoryProbeStatus.Inaccessible, ex.Message);
        }
    }
}
