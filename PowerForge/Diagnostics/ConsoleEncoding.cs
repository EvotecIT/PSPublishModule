using System;
using System.Text;

namespace PowerForge;

/// <summary>
/// Best-effort console encoding setup for UTF-8 output. Glyph and icon choices belong to the console renderer.
/// </summary>
public static class ConsoleEncoding
{
    /// <summary>
    /// Sets <see cref="Console.InputEncoding"/> and <see cref="Console.OutputEncoding"/> to UTF-8 (no BOM).
    /// Does nothing when the host does not allow changing encodings.
    /// </summary>
    /// <remarks>The change outlives the call and belongs to the console window, so a parent shell keeps it too.
    /// Commands should use <see cref="UseUtf8()"/>.</remarks>
    public static void EnsureUtf8()
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = utf8;
            Console.OutputEncoding = utf8;
        }
        catch
        {
            // Best-effort; do not throw.
        }
    }

    /// <summary>
    /// Switches the console to UTF-8 (no BOM) for one command in CI, where logs are read as UTF-8, and puts the previous
    /// encodings back when the returned scope is disposed. Interactive consoles keep the encoding their user chose.
    /// </summary>
    /// <returns>A scope that restores the console encodings it changed.</returns>
    public static IDisposable UseUtf8() => UseUtf8(ConsoleEnvironment.IsCI);

    /// <summary>
    /// Switches the console to UTF-8 (no BOM) when <paramref name="switchEncoding"/> is true, and puts the previous
    /// encodings back when the returned scope is disposed. Command-line tools that write machine-readable output use it
    /// for their whole run, so the shell that started them keeps its own encoding afterwards.
    /// </summary>
    /// <param name="switchEncoding">Whether to switch at all.</param>
    /// <returns>A scope that restores the console encodings it changed.</returns>
    public static IDisposable UseUtf8(bool switchEncoding) => new Utf8Scope(switchEncoding);

    private sealed class Utf8Scope : IDisposable
    {
        private readonly Encoding? _input;
        private readonly Encoding? _output;
        private bool _disposed;

        internal Utf8Scope(bool switchEncoding)
        {
            if (!switchEncoding)
                return;
            var input = Read(() => Console.InputEncoding);
            var output = Read(() => Console.OutputEncoding);
            // Never change what cannot be put back.
            if (input is null || output is null)
                return;
            _input = input;
            _output = output;
            EnsureUtf8();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            // Output first: it is the encoding a parent shell and later commands notice.
            if (_output is not null)
                Set(() => Console.OutputEncoding = _output);
            if (_input is not null)
                Set(() => Console.InputEncoding = _input);
        }

        private static Encoding? Read(Func<Encoding> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }

        private static void Set(Action set)
        {
            try
            {
                set();
            }
            catch
            {
                // Best-effort, as when the encoding was changed.
            }
        }
    }
}
