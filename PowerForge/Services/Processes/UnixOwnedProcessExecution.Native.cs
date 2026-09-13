using System.Runtime.InteropServices;

namespace PowerForge;

internal sealed partial class UnixOwnedProcessExecution
{
    [DllImport("libc", EntryPoint = "posix_spawnattr_init")]
    private static extern int SpawnAttributesInit(IntPtr attributes);
    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    private static extern int SpawnAttributesDestroy(IntPtr attributes);
    [DllImport("libc", EntryPoint = "posix_spawnattr_setpgroup")]
    private static extern int SpawnAttributesSetGroup(IntPtr attributes, int group);
    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    private static extern int SpawnAttributesSetFlags(IntPtr attributes, short flags);
    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    private static extern int SpawnActionsInit(IntPtr actions);
    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    private static extern int SpawnActionsDestroy(IntPtr actions);
    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np", CharSet = CharSet.Ansi)]
    private static extern int SpawnActionsChangeDirectory(IntPtr actions, string directory);
    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    private static extern int SpawnActionsDuplicate(IntPtr actions, int descriptor, int destination);
    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    private static extern int SpawnActionsClose(IntPtr actions, int descriptor);
    [DllImport("libc", EntryPoint = "posix_spawnp", CharSet = CharSet.Ansi)]
    private static extern int Spawn(out int processId, string file, IntPtr actions, IntPtr attributes, IntPtr arguments, IntPtr environment);
    [DllImport("libc", EntryPoint = "waitid", SetLastError = true)]
    private static extern int WaitId(int type, uint id, [Out] byte[] information, int options);
    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static extern int WaitPid(int processId, out int status, int options);
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
    [DllImport("libc", EntryPoint = "access", CharSet = CharSet.Ansi)]
    private static extern int Access(string path, int mode);
    [DllImport("libc", EntryPoint = "realpath", CharSet = CharSet.Ansi)]
    private static extern IntPtr RealPath(string path, IntPtr resolvedPath);
    [DllImport("libc", EntryPoint = "free")]
    private static extern void FreeNative(IntPtr pointer);
    [DllImport("/usr/lib/libproc.dylib", EntryPoint = "proc_listpids", SetLastError = true)]
    private static extern int ListDarwinProcesses(uint type, uint group, [Out] int[] processes, int bytes);
}
