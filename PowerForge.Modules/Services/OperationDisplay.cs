using System.Globalization;

namespace PowerForge;

internal static class OperationDisplay
{
    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            var hours = (int)duration.TotalHours;
            var minutes = duration.TotalMinutes - (hours * 60);
            return $"{hours}h {minutes.ToString("0.0", CultureInfo.InvariantCulture)}m";
        }

        if (duration.TotalMinutes >= 1)
            return $"{duration.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)}m";

        if (duration.TotalSeconds >= 1)
            return $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";

        return $"{duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}ms";
    }

    internal static string FormatBytes(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        if (bytes >= gb)
            return $"{(bytes / gb).ToString("0.##", CultureInfo.InvariantCulture)} GB";

        if (bytes >= mb)
            return $"{(bytes / mb).ToString("0.##", CultureInfo.InvariantCulture)} MB";

        if (bytes >= kb)
            return $"{(bytes / kb).ToString("0.##", CultureInfo.InvariantCulture)} KB";

        return $"{bytes} B";
    }
}
