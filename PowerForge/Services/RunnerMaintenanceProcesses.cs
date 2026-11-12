using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerForge;

internal sealed class RunnerMaintenanceProcess
{
    internal int Id { get; set; }
    internal int ParentId { get; set; }
    internal string Name { get; set; } = string.Empty;
    internal string? Executable { get; set; }
    internal long StartTicks { get; set; }
}

internal static class RunnerMaintenanceProcesses
{
    internal static FileStream OpenRegularFile(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        // Opening a CLR diagnostic FIFO with the ordinary FileStream constructor waits for a writer.
        var descriptor = Open(path, RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 4 : 2048);
        if (descriptor < 0) throw new IOException("File cannot be opened without blocking.");
        var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        FileStream? stream = null;
        try
        {
            stream = new FileStream(handle, FileAccess.Read);
            if (!stream.CanSeek || Flock(descriptor, 6) != 0) throw new IOException("Special or active file retained.");
            return stream;
        }
        catch
        {
            stream?.Dispose();
            handle.Dispose();
            throw;
        }
    }

    internal static Dictionary<int, RunnerMaintenanceProcess> Read()
    {
        var entries = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ReadWindows() : ReadUnix();
        foreach (var entry in entries.Values.ToArray())
        {
            try
            {
                using var process = Process.GetProcessById(entry.Id);
                entry.StartTicks = process.StartTime.ToUniversalTime().Ticks;
                entry.Executable = process.MainModule?.FileName;
            }
            catch (ArgumentException) { entries.Remove(entry.Id); }
            catch (InvalidOperationException) { entries.Remove(entry.Id); }
            catch (System.ComponentModel.Win32Exception) { /* Unknown live identities remain blockers. */ }
        }
        return entries;
    }

    internal static bool IsWorker(RunnerMaintenanceProcess entry) => entry.Name.Equals("Runner.Worker", StringComparison.OrdinalIgnoreCase);

    internal static HashSet<int> Ancestors(Dictionary<int, RunnerMaintenanceProcess> entries, int id)
    {
        var result = new HashSet<int>();
        while (id > 0 && result.Add(id) && entries.TryGetValue(id, out var entry)) id = entry.ParentId;
        return result;
    }

    internal static bool IsIdleBuildServer(RunnerMaintenanceProcess process)
    {
        if (!process.Name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && !process.Name.Equals("MSBuild", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var command = ReadCommandLine(process.Id);
            if (command.IndexOf("VBCSCompiler.dll", StringComparison.OrdinalIgnoreCase) >= 0 && command.IndexOf("pipename", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return (process.Name.Equals("MSBuild", StringComparison.OrdinalIgnoreCase) || command.IndexOf("MSBuild.dll", StringComparison.OrdinalIgnoreCase) >= 0)
                && command.IndexOf("nodemode:1", StringComparison.OrdinalIgnoreCase) >= 0
                && command.IndexOf("nodereuse:true", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    internal static bool IsBuildProcess(RunnerMaintenanceProcess process) =>
        new[] { "dotnet", "MSBuild", "testhost", "vstest.console", "pwsh", "powershell", "xcodebuild" }
            .Contains(process.Name, StringComparer.OrdinalIgnoreCase);

    internal static string Run(string executable, string directory, params string[] arguments)
    {
        var request = new ProcessRunRequest(executable, directory, arguments, TimeSpan.FromSeconds(15));
        var result = new ProcessRunner(ownProcessTree: true).RunAsync(request).GetAwaiter().GetResult();
        if (result.ExitCode != 0 || result.TimedOut || result.StartFailed)
            throw new IOException($"Maintenance command '{executable}' failed with exit code {result.ExitCode}.");
        return result.StdOut;
    }

    private static Dictionary<int, RunnerMaintenanceProcess> ReadUnix()
    {
        var result = new Dictionary<int, RunnerMaintenanceProcess>();
        foreach (var line in Run("ps", "/", "-axo", "pid=,ppid=,comm=").Split('\n'))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !int.TryParse(parts[0], out var pid) || !int.TryParse(parts[1], out var parent)) continue;
            result[pid] = new RunnerMaintenanceProcess { Id = pid, ParentId = parent, Name = NormalizeName(parts[2]) };
        }
        return result;
    }

    private static Dictionary<int, RunnerMaintenanceProcess> ReadWindows()
    {
        var result = new Dictionary<int, RunnerMaintenanceProcess>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception();
            do
            {
                result[(int)entry.Id] = new RunnerMaintenanceProcess { Id = (int)entry.Id, ParentId = (int)entry.ParentId, Name = NormalizeName(entry.Executable) };
            } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    private static string NormalizeName(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
    }

    private static string ReadCommandLine(int id)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return File.ReadAllText($"/proc/{id}/cmdline").Replace('\0', ' ');
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Run("ps", "/", "-p", id.ToString(), "-o", "command=");
        var process = OpenProcess(0x1000, false, id);
        if (process == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        IntPtr buffer = IntPtr.Zero;
        try
        {
            NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out var length);
            if (length <= 0 || length > 1024 * 1024) throw new IOException("Invalid process command-line length.");
            buffer = Marshal.AllocHGlobal(length);
            if (NtQueryInformationProcess(process, 60, buffer, length, out _) != 0) throw new IOException("Process command line is unavailable.");
            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            return Marshal.PtrToStringUni(value.Buffer, value.Length / 2) ?? string.Empty;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public IntPtr Heap;
        public uint Module, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnedLength);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int Flock(int descriptor, int operation);
}
