using System.Text;

namespace PowerForge;

/// <summary>Retains decoded pipe chunks as they arrive, including partial lines before EOF or cancellation.</summary>
internal sealed partial class RedirectedProcessOutput
{
    private readonly object _sync = new();
    private readonly StringBuilder _output = new();
    private readonly int _maximumCharacters;
    private bool _limitExceeded;
    private readonly CancellationTokenSource _readCancellation = new();
    private StreamReader? _reader;
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal RedirectedProcessOutput(int maximumCharacters = int.MaxValue) => _maximumCharacters = maximumCharacters;
    internal bool LimitExceeded { get { lock (_sync) return _limitExceeded; } }
    internal string Snapshot() { lock (_sync) return _output.ToString(); }

    internal static RedirectedProcessOutput Start(StreamReader reader, int maximumCharacters = int.MaxValue, Action<string>? lineReceived = null)
    {
        var capture = new RedirectedProcessOutput(maximumCharacters) { _reader = reader };
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.Completion = completion.Task;
        var stream = reader.BaseStream;
        var encoding = reader.CurrentEncoding;
        // Pipe operations can complete synchronously on Unix. Arm them away from the
        // caller so a continuous writer cannot prevent the process deadline from starting.
        var thread = new Thread(() => { _ = capture.ReadAsync(stream, encoding, lineReceived, completion); })
        { IsBackground = true, Name = "PowerForge redirected output reader" };
        thread.Start();
        return capture;
    }

    /// <summary>Stops pipe intake, then joins decoding and callbacks before a final snapshot is taken.
    /// Diagnostic callbacks already running must return before shutdown can finish.</summary>
    internal async Task StopAsync()
    {
        if (!Completion.IsCompleted)
        {
            try { _readCancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            try { _reader?.Dispose(); }
            catch (IOException) { }
        }
        await Completion.ConfigureAwait(false);
    }

    private async Task ReadAsync(Stream stream, Encoding encoding, Action<string>? lineReceived, TaskCompletionSource<object?> completion)
    {
        var bytes = new byte[4096];
        var chars = new char[Math.Max(encoding.GetMaxCharCount(bytes.Length + 4), (bytes.Length + 4) * 2)];
        var decoder = new ProcessOutputDecoder(encoding);
        var line = lineReceived is null ? null : new StringBuilder();
        var previousCarriageReturn = false;
        Exception? failure = null;
        try
        {
            int read;
            while ((read = await ReadChunkAsync(stream, bytes, _readCancellation.Token).ConfigureAwait(false)) > 0)
            {
                var count = decoder.Decode(bytes, read, chars, flush: false);
                Append(chars, count, line, lineReceived, ref previousCarriageReturn);
            }
            var finalCount = decoder.Decode(Array.Empty<byte>(), 0, chars, flush: true);
            Append(chars, finalCount, line, lineReceived, ref previousCarriageReturn);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) when (_readCancellation.IsCancellationRequested) { }
        catch (Exception exception) { failure = exception; }
        finally
        {
            if (line is { Length: > 0 }) Notify(lineReceived, line.ToString());
            _readCancellation.Dispose();
            if (failure is null) completion.TrySetResult(null);
            else completion.TrySetException(failure);
        }
    }

    private static Task<int> ReadChunkAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
#if NETFRAMEWORK
        if (FrameworkCompatibility.IsWindows()) return ReadWindowsPipeChunkAsync(stream, bytes, cancellationToken);
#endif
        return stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken);
    }

    private void Append(char[] chars, int count, StringBuilder? line, Action<string>? lineReceived,
        ref bool previousCarriageReturn)
    {
        lock (_sync)
        {
            var remaining = _maximumCharacters - _output.Length;
            if (remaining > 0) _output.Append(chars, 0, Math.Min(remaining, count));
            if (count > remaining) _limitExceeded = true;
        }
        if (line is null) return;
        for (var index = 0; index < count; index++)
        {
            var character = chars[index];
            if (character == '\n' && previousCarriageReturn) { previousCarriageReturn = false; continue; }
            previousCarriageReturn = character == '\r';
            if (character is '\r' or '\n')
            {
                Notify(lineReceived, line.ToString());
                line.Clear();
            }
            else if (line.Length < _maximumCharacters) line.Append(character);
        }
    }

    private static void Notify(Action<string>? callback, string line)
    {
        try { callback?.Invoke(line); }
        catch { /* A diagnostic callback must not interrupt pipe drainage. */ }
    }
}
