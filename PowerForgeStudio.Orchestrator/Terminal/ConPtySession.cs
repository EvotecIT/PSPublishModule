using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static PowerForgeStudio.Orchestrator.Terminal.ConPtyNative;

namespace PowerForgeStudio.Orchestrator.Terminal;

public sealed class ConPtySession : ITerminalSession
{
    private nint _hPC;
    private nint _hProcess;
    private nint _hThread;
    private readonly SafeFileHandle _pipeInWrite;
    private readonly SafeFileHandle _pipeOutRead;
    private nint _attributeList;
    private readonly CancellationTokenSource _readCts = new();
    private readonly Thread _readThread;
    private readonly object _outputLock = new();
    private byte[] _outputBuffer = [];
    private readonly Timer _flushTimer;
    private bool _disposed;
    private int _exitCode;

    public ConPtySession(string executable, string workingDirectory, int initialCols, int initialRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };

        if (!CreatePipe(out var pipeInRead, out _pipeInWrite, ref sa, 0))
            throw new InvalidOperationException($"CreatePipe (stdin) failed: {Marshal.GetLastWin32Error()}");

        if (!CreatePipe(out _pipeOutRead, out var pipeOutWrite, ref sa, 0))
            throw new InvalidOperationException($"CreatePipe (stdout) failed: {Marshal.GetLastWin32Error()}");

        var size = new COORD { X = (short)initialCols, Y = (short)initialRows };
        var hr = CreatePseudoConsole(size, pipeInRead, pipeOutWrite, 0, out _hPC);
        if (hr != 0)
            throw new InvalidOperationException($"CreatePseudoConsole failed: HRESULT 0x{hr:X8}");

        // Close the ends that ConPTY now owns
        pipeInRead.Dispose();
        pipeOutWrite.Dispose();

        // Set up attribute list for CreateProcess
        var attrListSize = nint.Zero;
        InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref attrListSize);
        _attributeList = Marshal.AllocHGlobal(attrListSize);

        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref attrListSize))
            throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {Marshal.GetLastWin32Error()}");

        if (!UpdateProcThreadAttribute(
                _attributeList, 0,
                (nint)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPC, (nint)nint.Size,
                nint.Zero, nint.Zero))
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {Marshal.GetLastWin32Error()}");

        var startupInfo = new STARTUPINFOEX
        {
            StartupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFOEX>() },
            lpAttributeList = _attributeList
        };

        if (!CreateProcess(
                null, executable,
                nint.Zero, nint.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                nint.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInfo))
            throw new InvalidOperationException($"CreateProcess '{executable}' failed: {Marshal.GetLastWin32Error()}");

        _hProcess = processInfo.hProcess;
        _hThread = processInfo.hThread;

        // Output batching: 16ms flush timer
        _flushTimer = new Timer(FlushOutputBuffer, null, Timeout.Infinite, Timeout.Infinite);

        // Start dedicated read thread (synchronous reads on pipe handles are most reliable)
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ConPTY-Read" };
        _readThread.Start();
    }

    public bool IsRunning
    {
        get
        {
            if (_disposed) return false;
            return WaitForSingleObject(_hProcess, 0) == WAIT_TIMEOUT;
        }
    }

    public event Action<byte[]>? OutputReceived;
    public event Action<int>? Exited;

    public void WriteInput(byte[] data)
    {
        if (_disposed || _pipeInWrite.IsInvalid || _pipeInWrite.IsClosed) return;
        try
        {
            WriteFile(_pipeInWrite, data, (uint)data.Length, out _, nint.Zero);
        }
        catch
        {
            // Process may have exited
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || _hPC == nint.Zero) return;
        var size = new COORD { X = (short)cols, Y = (short)rows };
        ResizePseudoConsole(_hPC, size);
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (WaitForSingleObject(_hProcess, 100) == WAIT_OBJECT_0)
            {
                GetExitCodeProcess(_hProcess, out var exitCode);
                _exitCode = (int)exitCode;
                return _exitCode;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return -1;
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        try
        {
            while (!_readCts.IsCancellationRequested)
            {
                bool success = ReadFile(_pipeOutRead, buffer, (uint)buffer.Length, out var bytesRead, nint.Zero);
                if (!success || bytesRead == 0)
                {
                    break;
                }

                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, (int)bytesRead);

                lock (_outputLock)
                {
                    var newBuffer = new byte[_outputBuffer.Length + bytesRead];
                    Buffer.BlockCopy(_outputBuffer, 0, newBuffer, 0, _outputBuffer.Length);
                    Buffer.BlockCopy(chunk, 0, newBuffer, _outputBuffer.Length, (int)bytesRead);
                    _outputBuffer = newBuffer;
                }

                _flushTimer.Change(16, Timeout.Infinite);
            }
        }
        catch
        {
            // Pipe closed or cancelled
        }
        finally
        {
            FlushOutputBuffer(null);
            GetExitCodeProcess(_hProcess, out var exitCode);
            _exitCode = (int)exitCode;
            Exited?.Invoke(_exitCode);
        }
    }

    private void FlushOutputBuffer(object? state)
    {
        byte[] data;
        lock (_outputLock)
        {
            if (_outputBuffer.Length == 0) return;
            data = _outputBuffer;
            _outputBuffer = [];
        }

        OutputReceived?.Invoke(data);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _readCts.CancelAsync().ConfigureAwait(false);
        _readCts.Dispose();
        _flushTimer.Dispose();

        // Close pipes first (this unblocks the read thread)
        if (!_pipeInWrite.IsInvalid && !_pipeInWrite.IsClosed)
            _pipeInWrite.Close();
        if (!_pipeOutRead.IsInvalid && !_pipeOutRead.IsClosed)
            _pipeOutRead.Close();

        // Wait for read thread
        _readThread.Join(TimeSpan.FromSeconds(2));

        // Close pseudo console
        if (_hPC != nint.Zero)
        {
            ClosePseudoConsole(_hPC);
            _hPC = nint.Zero;
        }

        // Terminate process if still running
        if (_hProcess != nint.Zero)
        {
            if (WaitForSingleObject(_hProcess, 1000) == WAIT_TIMEOUT)
            {
                TerminateProcess(_hProcess, 1);
            }

            CloseHandle(_hProcess);
            _hProcess = nint.Zero;
        }

        if (_hThread != nint.Zero)
        {
            CloseHandle(_hThread);
            _hThread = nint.Zero;
        }

        if (_attributeList != nint.Zero)
        {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = nint.Zero;
        }
    }
}
