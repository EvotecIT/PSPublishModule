using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

/// <summary>Creates a separate POSIX process group atomically with spawn and retains its leader until cleanup.</summary>
internal sealed partial class UnixOwnedProcessExecution : OwnedProcessExecution
{
    private readonly bool _mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private int _id;
    private bool _exited;
    private int _exitCode;
    private bool _terminationRequested;
    internal UnixOwnedProcessExecution(ProcessStartInfo startInfo) : base(startInfo)
    {
        if (IntPtr.Size != 8 || (!_mac && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)))
            throw new PlatformNotSupportedException("Owned validation processes require Windows or 64-bit Linux/macOS.");
    }
    internal override int Id => _id;
    internal override bool HasExited
    {
        get
        {
            if (_exited || _id == 0) return _exited;
            var info = new byte[128];
            // WNOWAIT leaves the group leader unreaped, reserving its PID/PGID until
            // group termination. A later signal cannot target an unrelated reused PID.
            if (WaitId(1, (uint)_id, info, 1 | 4 | (_mac ? 0x20 : 0x01000000)) != 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 4) return false; // EINTR
                if (error == 10) _id = 0; // ECHILD: no longer own the identity, so never signal it.
                throw new Win32Exception(error, "waitid failed for the owned validation process.");
            }
            var child = BitConverter.ToInt32(info, _mac ? 12 : 16);
            if (child == 0) return false;
            var status = BitConverter.ToInt32(info, _mac ? 20 : 24);
            _exitCode = BitConverter.ToInt32(info, 8) == 1 ? status : 128 + status; // CLD_EXITED
            return _exited = true;
        }
    }
    internal override int ExitCode => HasExited ? _exitCode : throw new InvalidOperationException("The process has not exited.");

    internal override void Start()
    {
        using var arguments = new NativeStringArray(new[] { StartInfo.FileName }.Concat(ReadArguments()));
        using var environment = new NativeStringArray(StartInfo.EnvironmentVariables.Keys.Cast<string>()
            .Select(key => key + "=" + StartInfo.EnvironmentVariables[key]));
        OpenPipes();
        // spawn.h ABI storage: Darwin exposes pointers; 64-bit glibc/musl expose
        // fixed 336-byte attributes and 80-byte file actions. No fields are interpreted here.
        var attributes = Marshal.AllocHGlobal(_mac ? IntPtr.Size : 336);
        var actions = Marshal.AllocHGlobal(_mac ? IntPtr.Size : 80);
        var attributesReady = false;
        var actionsReady = false;
        try
        {
            Check(SpawnAttributesInit(attributes), "posix_spawnattr_init");
            attributesReady = true;
            Check(SpawnAttributesSetGroup(attributes, 0), "posix_spawnattr_setpgroup");
            Check(SpawnAttributesSetFlags(attributes, 2), "posix_spawnattr_setflags"); // POSIX_SPAWN_SETPGROUP
            Check(SpawnActionsInit(actions), "posix_spawn_file_actions_init");
            actionsReady = true;
            Check(SpawnActionsChangeDirectory(actions, StartInfo.WorkingDirectory), "posix_spawn_file_actions_addchdir_np");
            AddPipe(actions, OutputPipe, 1);
            AddPipe(actions, ErrorPipe, 2);
            Check(Spawn(out var processId, ResolveExecutablePath(), actions, attributes, arguments.Pointer, environment.Pointer), "posix_spawnp");
            _id = processId;
        }
        finally
        {
            if (actionsReady) SpawnActionsDestroy(actions);
            if (attributesReady) SpawnAttributesDestroy(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
            CloseChildPipeHandles();
        }
    }

    private IEnumerable<string> ReadArguments()
    {
#if NET472
        throw new PlatformNotSupportedException("Owned Unix processes require modern .NET.");
#else
        return StartInfo.ArgumentList;
#endif
    }

    private string ResolveExecutablePath()
    {
        var file = StartInfo.FileName;
        if (Path.IsPathRooted(file)) return file;
#if !NET472
        // Preserve Process.Start's Unix lookup roots before changing the child
        // directory. In particular, a dotnet host can find itself outside PATH.
        var hostDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(hostDirectory))
        {
            var sibling = Path.Combine(hostDirectory, file);
            if (File.Exists(sibling)) return sibling;
        }
#endif
        var current = Path.GetFullPath(file);
        if (File.Exists(current)) return current;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(directory)) continue;
            var candidate = Path.GetFullPath(Path.Combine(directory, file));
            if (File.Exists(candidate) && Access(candidate, 1) == 0) return candidate;
        }
        throw new Win32Exception(2, $"Executable '{file}' was not found.");
    }

    private static void AddPipe(IntPtr actions, AnonymousPipeServerStream? pipe, int destination)
    {
        if (pipe is null) return;
        var read = pipe.SafePipeHandle.DangerousGetHandle().ToInt32();
        var write = pipe.ClientSafePipeHandle.DangerousGetHandle().ToInt32();
        Check(SpawnActionsDuplicate(actions, write, destination), "posix_spawn_file_actions_adddup2");
        if (read != destination) Check(SpawnActionsClose(actions, read), "posix_spawn_file_actions_addclose");
        if (write != destination) Check(SpawnActionsClose(actions, write), "posix_spawn_file_actions_addclose");
    }

    internal override void KillTree()
    {
        if (_id <= 0 || _terminationRequested) return;
        // Signal once: Darwin may report EPERM when only the retained zombie
        // leader remains. A successful SIGKILL already terminates this scope.
        if (Kill(-_id, 9) == 0) { _terminationRequested = true; return; }
        var error = Marshal.GetLastWin32Error();
        if (_mac && error == 1 && HasExited && DarwinGroupContainsOnlyExitedLeader())
        {
            _terminationRequested = true;
            return;
        }
        if (error == 3) _terminationRequested = true;
        if (error != 3) // ESRCH means the group is empty.
            throw new Win32Exception(error, $"Could not terminate the owned validation process group (errno {error}).");
    }

    private bool DarwinGroupContainsOnlyExitedLeader()
    {
        // Darwin's killpg returns EPERM for a group containing only zombies.
        // Do not suppress a real permission failure: prove no other member exists
        // while WNOWAIT still reserves this leader and its process-group identity.
        for (var capacity = 64; capacity <= 1048576; capacity *= 2)
        {
            var members = new int[capacity];
            var bytes = ListDarwinProcesses(2, (uint)_id, members, members.Length * sizeof(int));
            var error = Marshal.GetLastWin32Error();
            if (bytes < 0 || (bytes == 0 && error != 0) || bytes % sizeof(int) != 0) return false;
            if (bytes >= members.Length * sizeof(int)) continue;
            for (var index = 0; index < bytes / sizeof(int); index++)
                if (members[index] != 0 && members[index] != _id) return false;
            return true;
        }
        return false;
    }

    public override void Dispose()
    {
        try
        {
            if (_id > 0)
            {
                KillTree();
                var watch = Stopwatch.StartNew();
                while (WaitPid(_id, out _, 1) == 0 && watch.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
                _id = 0;
            }
        }
        finally { ClosePipes(); }
    }

    private static void Check(int error, string operation)
    {
        if (error != 0) throw new Win32Exception(error, operation + " failed.");
    }

    private sealed class NativeStringArray : IDisposable
    {
        private readonly IntPtr[] _strings;
        internal IntPtr Pointer { get; }
        internal NativeStringArray(IEnumerable<string> values)
        {
            var entries = values.ToArray();
            _strings = new IntPtr[entries.Length + 1];
            Pointer = Marshal.AllocHGlobal(_strings.Length * IntPtr.Size);
            try
            {
                for (var index = 0; index < entries.Length; index++)
                {
                    if (entries[index].IndexOf('\0') >= 0) throw new ArgumentException("Process arguments and environment values cannot contain NUL.");
                    var bytes = Encoding.UTF8.GetBytes(entries[index] + "\0");
                    _strings[index] = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, _strings[index], bytes.Length);
                }
                Marshal.Copy(_strings, 0, Pointer, _strings.Length);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            foreach (var value in _strings) if (value != IntPtr.Zero) Marshal.FreeHGlobal(value);
            Marshal.FreeHGlobal(Pointer);
        }
    }
}
