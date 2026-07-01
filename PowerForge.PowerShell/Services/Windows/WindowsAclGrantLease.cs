using System.Diagnostics;

namespace PowerForge;

internal sealed class WindowsAclGrantLease : IDisposable
{
    private readonly string _accountName;
    private readonly List<string> _paths = new();
    private bool _disposed;

    internal WindowsAclGrantLease(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("Account name is required.", nameof(accountName));

        _accountName = accountName;
    }

    internal void GrantDirectoryAccess(string path, string rights)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        Directory.CreateDirectory(path);
        GrantAccess(path, rights);
    }

    internal void GrantFileAccess(string path, string rights)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        GrantAccess(path, rights);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var path in _paths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Reverse<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                continue;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "icacls.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
                WindowsProcessArguments.Add(startInfo, path, "/remove:g", _accountName);
                using var process = Process.Start(startInfo);
                process?.WaitForExit();
            }
            catch
            {
                // ACL cleanup is best effort; the temporary user cleanup still runs.
            }
        }
    }

    private void GrantAccess(string path, string rights)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
        WindowsProcessArguments.Add(startInfo, path, "/grant", string.Concat(_accountName, ":", rights));

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start icacls.exe.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Failed to grant temporary user access to '{path}' (icacls exit {process.ExitCode}). STDOUT: {stdout} STDERR: {stderr}");

        _paths.Add(path);
    }
}
