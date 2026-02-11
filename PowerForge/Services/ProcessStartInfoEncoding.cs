using System.Diagnostics;
using System.Text;

namespace PowerForge;

/// <summary>
/// Best-effort helpers for configuring UTF-8 process output decoding across target frameworks.
/// </summary>
public static class ProcessStartInfoEncoding
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Attempts to configure <paramref name="startInfo"/> so redirected stdout/stderr are decoded as UTF-8.
    /// Uses reflection for cross-target compatibility (net472 does not expose these properties).
    /// </summary>
    public static void TryApplyUtf8(ProcessStartInfo startInfo)
    {
        if (startInfo is null) return;
        TrySetEncodingProperty(startInfo, "StandardOutputEncoding");
        TrySetEncodingProperty(startInfo, "StandardErrorEncoding");
    }

    private static void TrySetEncodingProperty(ProcessStartInfo startInfo, string propertyName)
    {
        try
        {
            var property = typeof(ProcessStartInfo).GetProperty(propertyName);
            if (property is null || !property.CanWrite) return;
            property.SetValue(startInfo, Utf8NoBom, index: null);
        }
        catch
        {
            // Best effort only: some frameworks/hosts do not expose these properties.
        }
    }
}

