using System;
using System.Text;

namespace PowerForge;

/// <summary>
/// Best-effort console encoding setup for Unicode/emoji output.
/// </summary>
public static class ConsoleEncoding
{
    /// <summary>
    /// Sets <see cref="Console.InputEncoding"/> and <see cref="Console.OutputEncoding"/> to UTF-8 (no BOM).
    /// Does nothing when the host does not allow changing encodings.
    /// </summary>
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
}

