using System.Diagnostics;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace PowerForge;

internal sealed class WindowsTemporaryIdentityLease : IDisposable
{
    private readonly WindowsAclGrantLease _aclLease;
    private readonly SecureString _password;
    private bool _disposed;
    private bool _retainProfileAndScratch;

    private WindowsTemporaryIdentityLease(string userName, SecureString password, string scratchRoot)
    {
        UserName = userName;
        AccountName = string.Concat(Environment.MachineName, "\\", userName);
        ScratchRoot = scratchRoot;
        _password = password;
        _aclLease = new WindowsAclGrantLease(AccountName);
    }

    internal string UserName { get; }

    internal string AccountName { get; }

    internal string ScratchRoot { get; }

    internal static WindowsTemporaryIdentityLease Create(WindowsTemporaryIdentityOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        ValidateWindowsAdministrator(options.CapabilityName);

        var userName = CreateUserName(options.UserNamePrefix);
        var password = ToSecureString(CreatePassword());
        var scratchRoot = Path.Combine(Path.GetTempPath(), string.Concat(options.ScratchRootPrefix, userName));
        try
        {
            Directory.CreateDirectory(scratchRoot);
            CreateLocalUser(userName, password, options.Description);
            return new WindowsTemporaryIdentityLease(userName, password, scratchRoot);
        }
        catch
        {
            password.Dispose();
            RemoveLocalUser(userName);
            TryDeleteDirectory(scratchRoot);
            throw;
        }
    }

    internal void GrantDirectoryAccess(string path, string rights)
        => _aclLease.GrantDirectoryAccess(path, rights);

    internal void GrantFileAccess(string path, string rights)
        => _aclLease.GrantFileAccess(path, rights);

    internal void RetainProfileAndScratch()
        => _retainProfileAndScratch = true;

    internal WindowsProcessResult RunProcess(
        string fileName,
        string workingDirectory,
        string stdoutPath,
        string stderrPath,
        params string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Process file name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            workingDirectory = ScratchRoot;

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true
        };
#pragma warning disable CA1416
        startInfo.Domain = Environment.MachineName;
        startInfo.UserName = UserName;
        startInfo.Password = _password;
        startInfo.LoadUserProfile = true;
#pragma warning restore CA1416
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
        WindowsProcessArguments.Add(startInfo, arguments);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start process under temporary Windows identity.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        File.WriteAllText(stdoutPath, stdout, new UTF8Encoding(false));
        File.WriteAllText(stderrPath, stderr, new UTF8Encoding(false));
        return new WindowsProcessResult(process.ExitCode, stdout, stderr);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _aclLease.Dispose();
        }
        finally
        {
            RemoveLocalUser(UserName);
            if (!_retainProfileAndScratch)
            {
                RemoveUserProfile(UserName);
                TryDeleteDirectory(ScratchRoot);
            }

            _password.Dispose();
        }
    }

    private static void ValidateWindowsAdministrator(string capabilityName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException($"{capabilityName} is supported only on Windows.");

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException($"{capabilityName} requires an elevated administrator PowerShell session so a temporary local user can be created and removed.");
    }

    private static string CreateUserName(string? prefix)
    {
        var safePrefix = new string((prefix ?? "PFTemp").Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safePrefix))
            safePrefix = "PFTemp";
        if (safePrefix.Length > 10)
            safePrefix = safePrefix.Substring(0, 10);
        return safePrefix + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    private static string CreatePassword()
        => "PFt!" + Guid.NewGuid().ToString("N") + "9a";

    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var ch in value)
            secure.AppendChar(ch);
        secure.MakeReadOnly();
        return secure;
    }

    private static void CreateLocalUser(string userName, SecureString password, string description)
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("New-LocalUser")
            .AddParameter("Name", userName)
            .AddParameter("Password", password)
            .AddParameter("Description", description)
            .AddParameter("AccountNeverExpires")
            .AddParameter("PasswordNeverExpires");
        InvokePowerShell(ps, $"create temporary Windows user '{userName}'");
    }

    private static void RemoveLocalUser(string userName)
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("Remove-LocalUser")
            .AddParameter("Name", userName)
            .AddParameter("ErrorAction", ActionPreference.SilentlyContinue);
        _ = ps.Invoke();
    }

    private static void RemoveUserProfile(string userName)
    {
        using var ps = PowerShell.Create();
        ps.AddScript("""
param([string] $UserName)
Get-CimInstance Win32_UserProfile |
    Where-Object { $_.LocalPath -like "*\$UserName" } |
    Remove-CimInstance -ErrorAction SilentlyContinue
""").AddArgument(userName);
        _ = ps.Invoke();
    }

    private static void InvokePowerShell(PowerShell ps, string action)
    {
        _ = ps.Invoke();
        if (!ps.HadErrors)
            return;

        var errors = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        throw new InvalidOperationException($"Failed to {action}. {errors}");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup is best effort; the temporary account has already been removed.
        }
    }
}
