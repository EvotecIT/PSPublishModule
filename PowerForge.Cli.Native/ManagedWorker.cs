using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class ManagedWorker
{
    internal static int Run(string[] args)
    {
        var name = OperatingSystem.IsWindows() ? "powerforge-full.exe" : "powerforge-full";
        var path = Path.Combine(AppContext.BaseDirectory, "full", name);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"This command requires the managed PowerForge companion. Expected '{path}'.");
            return 126;
        }

        return OperatingSystem.IsWindows() ? RunWindows(path, args) : ReplaceUnix(path, args);
    }

    private static int RunWindows(string path, string[] args)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        // A console control event also reaches the attached worker. Stay alive to return its exit code.
        ConsoleCancelEventHandler cancel = (_, eventArgs) => eventArgs.Cancel = true;
        Console.CancelKeyPress += cancel;
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                Console.Error.WriteLine("Could not start the managed PowerForge companion.");
                return 126;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"Could not start the managed PowerForge companion: {exception.Message}");
            return 126;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static int ReplaceUnix(string path, string[] args)
    {
        string[] arguments = [path, .. args];
        var argv = Marshal.AllocHGlobal((arguments.Length + 1) * IntPtr.Size);
        var strings = new IntPtr[arguments.Length];
        try
        {
            for (var index = 0; index < arguments.Length; index++)
            {
                strings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
                Marshal.WriteIntPtr(argv, index * IntPtr.Size, strings[index]);
            }

            Marshal.WriteIntPtr(argv, arguments.Length * IntPtr.Size, IntPtr.Zero);
            _ = Exec(strings[0], argv);
            var error = Marshal.GetLastPInvokeError();

            // execv only returns on failure.
            Console.Error.WriteLine($"Could not start the managed PowerForge companion (errno {error}).");
            return 126;
        }
        finally
        {
            foreach (var value in strings)
                Marshal.FreeCoTaskMem(value);

            Marshal.FreeHGlobal(argv);
        }
    }

    [DllImport("libc", EntryPoint = "execv", SetLastError = true)]
    private static extern int Exec(IntPtr path, IntPtr argv);
}
