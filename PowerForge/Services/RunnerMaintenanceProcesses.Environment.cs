using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static partial class RunnerMaintenanceProcesses
{
    internal const string JobMarker = "POWERFORGE_RUNNER_JOB_ID";
    private const int EnvironmentLimit = 1024 * 1024;

    internal static string? ReadJobMarker(int id)
    {
        string block;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) block = ReadWindowsEnvironment(id);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            using var input = File.OpenRead($"/proc/{id}/environ");
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (output.Length + count > EnvironmentLimit) throw new IOException("Process environment exceeds its inspection bound.");
                output.Write(buffer, 0, count);
            }
            block = Encoding.UTF8.GetString(output.ToArray());
        }
        else
        {
            // Only the fixed, whitespace-free job nonce is inspected. Other environment values are never returned or logged.
            block = Run("ps", "/", "eww", "-p", id.ToString(), "-o", "command=");
            var match = Regex.Match(block, @"(?:^|\s)" + JobMarker + @"=([a-f0-9]{32})(?:\s|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return match.Success ? match.Groups[1].Value : null;
        }
        var entry = block.Split('\0').FirstOrDefault(value => value.StartsWith(JobMarker + "=", StringComparison.Ordinal));
        var marker = entry?.Substring(JobMarker.Length + 1);
        return marker is not null && Guid.TryParseExact(marker, "N", out _) ? marker : null;
    }

    private static string ReadWindowsEnvironment(int id)
    {
        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            throw new IOException("Cross-bitness environment inspection requires the native maintenance executable.");
        var process = OpenProcess(0x410, false, id);
        if (process == IntPtr.Zero) throw new IOException("Process environment is unavailable.");
        var information = Marshal.AllocHGlobal(IntPtr.Size * 6);
        try
        {
            var width = IntPtr.Size;
            IntPtr peb;
            if (width == 8 && NtQueryInformationProcess(process, 26, information, IntPtr.Size, out _) == 0 && Marshal.ReadIntPtr(information) != IntPtr.Zero)
            {
                peb = Marshal.ReadIntPtr(information);
                width = 4;
            }
            else
            {
                if (NtQueryInformationProcess(process, 0, information, IntPtr.Size * 6, out _) != 0) throw new IOException("Process identity is unavailable.");
                peb = Marshal.ReadIntPtr(information, IntPtr.Size);
            }
            var parameters = ReadEnvironmentPointer(process, IntPtr.Add(peb, width == 8 ? 0x20 : 0x10), width);
            var address = ReadEnvironmentPointer(process, IntPtr.Add(parameters, width == 8 ? 0x80 : 0x48), width);
            var memory = new EnvironmentMemory();
            if (VirtualQueryEnvironment(process, address, ref memory, (UIntPtr)Marshal.SizeOf<EnvironmentMemory>()) == UIntPtr.Zero)
                throw new IOException("Process environment region is unavailable.");
            var available = checked((long)memory.RegionSize.ToUInt64() - (address.ToInt64() - memory.BaseAddress.ToInt64()));
            if (available < 4) throw new IOException("Process environment region is incomplete.");
            var bytes = new byte[(int)Math.Min(available, EnvironmentLimit)];
            if (!ReadEnvironmentMemory(process, address, bytes, (UIntPtr)bytes.Length, out var read) || read.ToUInt64() != (ulong)bytes.Length)
                throw new IOException("Process environment could not be read.");
            var end = 0;
            for (var index = 0; index + 3 < bytes.Length; index += 2)
            {
                if (bytes[index] != 0 || bytes[index + 1] != 0 || bytes[index + 2] != 0 || bytes[index + 3] != 0) continue;
                end = index + 2;
                break;
            }
            if (end == 0) throw new IOException("Process environment is incomplete.");
            return Encoding.Unicode.GetString(bytes, 0, end);
        }
        finally
        {
            Marshal.FreeHGlobal(information);
            CloseHandle(process);
        }
    }

    private static IntPtr ReadEnvironmentPointer(IntPtr process, IntPtr address, int width)
    {
        var bytes = new byte[width];
        if (!ReadEnvironmentMemory(process, address, bytes, (UIntPtr)width, out var read) || read.ToUInt64() != (ulong)width)
            throw new IOException("Process environment pointer is unavailable.");
        return width == 8 ? new IntPtr(BitConverter.ToInt64(bytes, 0)) : new IntPtr(BitConverter.ToUInt32(bytes, 0));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnvironmentMemory
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true)]
    private static extern bool ReadEnvironmentMemory(IntPtr process, IntPtr address, [Out] byte[] bytes, UIntPtr length, out UIntPtr read);
    [DllImport("kernel32.dll", EntryPoint = "VirtualQueryEx", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEnvironment(IntPtr process, IntPtr address, ref EnvironmentMemory information, UIntPtr length);
}
