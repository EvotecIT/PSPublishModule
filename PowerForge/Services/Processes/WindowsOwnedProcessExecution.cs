using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

/// <summary>Assigns a suspended process to a job before any child code can spawn descendants.</summary>
internal sealed partial class WindowsOwnedProcessExecution : OwnedProcessExecution
{
    private SafeFileHandle? _job;
    private SafeFileHandle? _process;
    private int _id;
    internal WindowsOwnedProcessExecution(ProcessStartInfo startInfo) : base(startInfo) { }
    internal override int Id => _id;
    internal override bool HasExited => _process is not null && WaitForSingleObject(_process, 0) == 0;
    internal override int ExitCode
    {
        get
        {
            if (_process is null || !GetExitCodeProcess(_process, out var code)) throw Error("GetExitCodeProcess");
            return unchecked((int)code);
        }
    }

    internal override void Start()
    {
        OpenPipes();
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job.IsInvalid) throw Error("CreateJobObject");
        var limits = new JobLimits { Basic = new BasicJobLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<JobLimits>())) throw Error("SetInformationJobObject");

        using var input = DuplicateStandardHandle(-10, write: false);
        using var output = OutputPipe is null ? DuplicateStandardHandle(-11, write: true) : null;
        using var error = ErrorPipe is null ? DuplicateStandardHandle(-12, write: true) : null;
        var handles = new[] {
            input.DangerousGetHandle(),
            OutputPipe?.ClientSafePipeHandle.DangerousGetHandle() ?? output!.DangerousGetHandle(),
            ErrorPipe?.ClientSafePipeHandle.DangerousGetHandle() ?? error!.DangerousGetHandle()
        };
        var attributeSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
        if (attributeSize == IntPtr.Zero) throw Error("InitializeProcThreadAttributeList");
        var attributes = Marshal.AllocHGlobal(attributeSize);
        var handleList = Marshal.AllocHGlobal(IntPtr.Size * handles.Length);
        var environment = Marshal.StringToHGlobalUni(string.Join("\0", StartInfo.EnvironmentVariables.Keys.Cast<string>()
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase).Select(key => key + "=" + StartInfo.EnvironmentVariables[key])) + "\0\0");
        var initialized = false;
        ProcessInformation information = default;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeSize)) throw Error("InitializeProcThreadAttributeList");
            initialized = true;
            Marshal.Copy(handles, 0, handleList, handles.Length);
            if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x20002), handleList,
                new IntPtr(IntPtr.Size * handles.Length), IntPtr.Zero, IntPtr.Zero)) throw Error("UpdateProcThreadAttribute(HANDLE_LIST)");
            var startup = new StartupInfoEx {
                Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    StandardInput = handles[0], StandardOutput = handles[1], StandardError = handles[2] },
                AttributeList = attributes
            };
            var commandLine = new StringBuilder(ProcessRunner.QuoteArgument(StartInfo.FileName));
#if NET472
            if (!string.IsNullOrEmpty(StartInfo.Arguments)) commandLine.Append(' ').Append(StartInfo.Arguments);
#else
            foreach (var argument in StartInfo.ArgumentList) commandLine.Append(' ').Append(ProcessRunner.QuoteArgument(argument));
#endif
            const uint flags = 0x4 | 0x400 | 0x80000; // suspended, Unicode environment, extended startup info
            if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                flags | (StartInfo.CreateNoWindow ? 0x08000000u : 0), environment, StartInfo.WorkingDirectory, ref startup, out information))
                throw Error("CreateProcess");
            _process = new SafeFileHandle(information.Process, ownsHandle: true);
            _id = unchecked((int)information.ProcessId);
            if (!AssignProcessToJobObject(_job, _process)) throw Error("AssignProcessToJobObject");
            if (ResumeThread(information.Thread) == uint.MaxValue) throw Error("ResumeThread");
        }
        catch
        {
            if (_process is not null && !_process.IsInvalid) TerminateProcess(_process, 127);
            throw;
        }
        finally
        {
            if (information.Thread != IntPtr.Zero) CloseHandle(information.Thread);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(handleList);
            Marshal.FreeHGlobal(environment);
            CloseChildPipeHandles();
        }
    }

    internal override void KillTree()
    {
        // The job remains valid after its original process exits; parent PID lookup is unnecessary.
        if (_job is not null && !_job.IsInvalid && !TerminateJobObject(_job, 1)) throw Error("TerminateJobObject");
    }

    public override void Dispose()
    {
        _job?.Dispose(); // Also closes descendants on exception and normal isolated-probe completion.
        _process?.Dispose();
        ClosePipes();
    }

    private static SafeFileHandle DuplicateStandardHandle(int kind, bool write)
    {
        var handle = GetStdHandle(kind);
        if (handle != IntPtr.Zero && handle != new IntPtr(-1) &&
            DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), out var duplicate, 0, true, 2)) return duplicate;
        var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        var fallback = CreateFile("NUL", write ? 0x40000000u : 0x80000000u, 3, ref attributes, 3, 0x80, IntPtr.Zero);
        if (fallback.IsInvalid) throw Error("CreateFile(NUL)");
        return fallback;
    }

    private static Win32Exception Error(string operation) => new(Marshal.GetLastWin32Error(), operation + " failed.");
}
