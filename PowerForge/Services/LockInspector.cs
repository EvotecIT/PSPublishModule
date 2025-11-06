using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Inspects which processes hold locks on files/directories using the Windows Restart Manager API.
/// On non-Windows platforms, returns an empty result.
/// </summary>
public static class LockInspector
{
    /// <summary>Returns processes locking any of the provided paths.</summary>
    public static IReadOnlyList<(int Pid, string Name)> GetLockingProcesses(IEnumerable<string> paths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Array.Empty<(int, string)>();
        var list = new List<(int, string)>();
        uint session = 0;
        string key = Guid.NewGuid().ToString();
        int res = RmStartSession(out session, 0, key);
        if (res != 0) return list;
        try
        {
            var arr = (paths is null) ? Array.Empty<string>() : new List<string>(paths).ToArray();
            if (arr.Length == 0) return list;
            res = RmRegisterResources(session, (uint)arr.Length, arr, 0, null, 0, null);
            if (res != 0) return list;

            uint needed = 0, count = 0, reason = 0;
            // First call to get count
            res = RmGetList(session, out needed, ref count, null, out reason);
            if (res == ERROR_MORE_DATA)
            {
                var infos = new RM_PROCESS_INFO[needed];
                count = needed;
                res = RmGetList(session, out needed, ref count, infos, out reason);
                if (res == 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var p = infos[i].Process;
                        int pid = (int)p.dwProcessId;
                        string name = infos[i].strAppName;
                        list.Add((pid, name));
                    }
                }
            }
        }
        finally { RmEndSession(session); }
        return list;
    }

    /// <summary>Attempts to terminate processes locking any of the provided paths.</summary>
    public static int TerminateLockingProcesses(IEnumerable<string> paths, bool force = false)
    {
        int killed = 0;
        foreach (var (pid, _) in GetLockingProcesses(paths))
        {
            try { var p = System.Diagnostics.Process.GetProcessById(pid); if (force) { try { p.Kill(); } catch { } } else { p.Kill(); } killed++; }
            catch { /* ignore */ }
        }
        return killed;
    }

    private const int ERROR_MORE_DATA = 234;

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications, uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgProcInfo, out uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmEndSession(uint pSessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }
}
