using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// A machine-wide benchmark lock held as a directory with an <c>owner</c> file, compatible with shell scripts that
/// take the same lock with <c>mkdir</c>. The directory is prepared next to the lock path and renamed into place, so
/// two processes never both hold it.
/// </summary>
internal sealed class BenchmarkLockDirectory : IDisposable
{
    internal const string OwnerFileName = "owner";
    private readonly string _path;
    private readonly string _token;
    private bool _released;

    private BenchmarkLockDirectory(string path, string token)
    {
        _path = path;
        _token = token;
    }

    /// <summary>Lock directory path.</summary>
    internal string Path => _path;

    /// <summary>
    /// Waits until the lock is free and takes it.
    /// </summary>
    internal static async Task<BenchmarkLockDirectory> AcquireAsync(
        string path,
        string description,
        TimeSpan timeout,
        TimeSpan pollInterval,
        Action<string>? waiting,
        CancellationToken cancellationToken)
    {
        string fullPath = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        string parent = System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The lock path needs a parent folder.", nameof(path));
        Directory.CreateDirectory(parent);
        var stopwatch = Stopwatch.StartNew();
        string? reportedOwner = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string token = Guid.NewGuid().ToString("N");
            if (TryTake(fullPath, parent, description, token))
                return new BenchmarkLockDirectory(fullPath, token);

            string owner = ReadOwner(fullPath);
            if (TryClearStale(fullPath, owner))
                continue;
            if (!string.Equals(owner, reportedOwner, StringComparison.Ordinal))
            {
                waiting?.Invoke(owner.Length == 0 ? "owner not recorded yet" : owner.Replace('\n', ' ').Trim());
                reportedOwner = owner;
            }
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException($"Timed out after {timeout} waiting for benchmark lock {fullPath}. Held by: {owner.Replace('\n', ' ').Trim()}");
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the lock when this process still owns it.
    /// </summary>
    public void Dispose()
    {
        if (_released) return;
        _released = true;
        try
        {
            string owner = ReadOwner(_path);
            if (owner.IndexOf("token=" + _token, StringComparison.Ordinal) >= 0)
                Directory.Delete(_path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Takes the lock without ever replacing a directory someone else created. On Linux and macOS this is the same
    /// <c>mkdir</c> a shell script uses, followed by the owner file. On Windows the directory is prepared next to the
    /// lock and moved into place, which fails when the lock already exists.
    /// </summary>
    private static bool TryTake(string fullPath, string parent, string description, string token)
    {
        if (Directory.Exists(fullPath) || File.Exists(fullPath))
            return false;
        string owner = OwnerText(description, token);
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryTakeByMove(fullPath, parent, owner, token)
            : TryTakeByMkdir(fullPath, owner, token);
    }

    private static bool TryTakeByMkdir(string fullPath, string owner, string token)
    {
        if (UnixMkdir(fullPath, Convert.ToUInt32("755", 8)) != 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == UnixErrorExists) return false;
            throw new IOException($"Could not create benchmark lock {fullPath} (errno {error}).");
        }

        // The owner file appears in one step, so a reader never sees half of it.
        string staging = System.IO.Path.Combine(fullPath, "." + OwnerFileName + "." + token);
        try
        {
            File.WriteAllText(staging, owner);
            File.Move(staging, System.IO.Path.Combine(fullPath, OwnerFileName));
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // Someone removed the directory before the owner file was written: the lock was lost, so start over.
            return false;
        }
        catch (IOException)
        {
            // Another runner's owner file is already there (the move never overwrites it): the lock is theirs.
            try { File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }
    }

    private static bool TryTakeByMove(string fullPath, string parent, string owner, string token)
    {
        string staging = System.IO.Path.Combine(parent, "." + System.IO.Path.GetFileName(fullPath) + "." + token + ".tmp");
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllText(System.IO.Path.Combine(staging, OwnerFileName), owner);
            Directory.Move(staging, fullPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
            }
        }
    }

    private static string OwnerText(string description, string token)
    {
        using var process = Process.GetCurrentProcess();
        return string.Join("\n", new[]
        {
            "pid=" + process.Id.ToString(CultureInfo.InvariantCulture),
            "host=" + System.Environment.MachineName,
            "started=" + DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            "command=" + description,
            "token=" + token
        }) + "\n";
    }

    // EEXIST is 17 on Linux and macOS.
    private const int UnixErrorExists = 17;

    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int UnixMkdir(string path, uint mode);

    private static string ReadOwner(string fullPath)
    {
        try
        {
            string file = System.IO.Path.Combine(fullPath, OwnerFileName);
            return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    /// <summary>
    /// Removes a lock left behind by a process of this host that no longer runs. Only owner files that record
    /// both <c>host=</c> and <c>pid=</c> qualify, so a lock taken by a script in another format is never removed.
    /// </summary>
    private static bool TryClearStale(string fullPath, string owner)
    {
        string? host = Field(owner, "host");
        string? pidText = Field(owner, "pid");
        if (host is null || pidText is null ||
            !string.Equals(host, System.Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ||
            IsRunning(pid))
        {
            return false;
        }

        // Two runners can find the same stale lock. Reclaiming runs under an exclusive file lock, released by the
        // operating system if the process dies, and re-reads the owner inside it, so a runner never deletes a lock
        // that another runner has taken since it looked.
        string reclaimPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fullPath)!, "." + System.IO.Path.GetFileName(fullPath) + ".reclaim");
        try
        {
            using var reclaim = new FileStream(reclaimPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!string.Equals(ReadOwner(fullPath), owner, StringComparison.Ordinal))
                return false;
            Directory.Delete(fullPath, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string? Field(string owner, string name)
        => owner.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(name + "=", StringComparison.Ordinal))
            .Select(line => line.Substring(name.Length + 1))
            .FirstOrDefault();

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
