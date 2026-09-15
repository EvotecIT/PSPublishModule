using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

internal sealed partial class WindowsOwnedProcessExecution
{
    /// <summary>Uses native executable lookup with the child search path when that environment is overridden.</summary>
    private string? ResolveExecutablePath()
    {
        var fileName = StartInfo.FileName;
        var directory = string.IsNullOrEmpty(StartInfo.WorkingDirectory) ? Environment.CurrentDirectory
            : Path.GetFullPath(StartInfo.WorkingDirectory);
        if (Path.IsPathRooted(fileName) || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
            return Path.GetFullPath(Path.IsPathRooted(fileName) ? fileName : Path.Combine(directory, fileName));

        var path = StartInfo.EnvironmentVariables["PATH"];
        // Retain CreateProcess's ordinary application/system lookup for an inherited environment.
        if (path is not null && string.Equals(path, Environment.GetEnvironmentVariable("PATH"), StringComparison.Ordinal))
            return null;
        if (path is null) throw new Win32Exception(2, $"Executable '{fileName}' cannot be located because the child PATH was removed.");

        var searchPath = string.Join(";", path.Split(';').Select(entry => {
            var value = entry.Trim('"');
            return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(directory, value));
        }));
        var buffer = new StringBuilder(32768);
        var length = SearchPath(searchPath, fileName, ".exe", (uint)buffer.Capacity, buffer, IntPtr.Zero);
        if (length == 0) throw Error("SearchPath(child PATH)");
        if (length >= buffer.Capacity) throw new Win32Exception(206, "The resolved executable path is too long.");
        return buffer.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "SearchPathW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SearchPath(string path, string fileName, string extension, uint bufferLength,
        StringBuilder buffer, IntPtr filePart);
}
