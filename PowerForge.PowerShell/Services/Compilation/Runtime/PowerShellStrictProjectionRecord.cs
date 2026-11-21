using System.Text.Json;

namespace PowerForge.Generated.Runtime;

/// <summary>One ordered record from an authored projection over a closed CLR shape.</summary>
internal sealed class PowerShellStrictProjectionRecord
{
    private static readonly List<object?> Pending = new();
    internal const string ProjectedTypeName = "System.Management.Automation.PSCustomObject";

    internal PowerShellStrictProjectionRecord(string[] names, string[] values)
    {
        Names = names;
        Values = values;
    }

    internal string[] Names { get; }
    internal string[] Values { get; }

    internal string ToObservationPayload()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            for (var index = 0; index < Names.Length; index++)
                writer.WriteString(Names[index], Values[index]);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    internal void WriteConsole()
    {
        if (Pending.Count == 0) Console.WriteLine();
        Pending.Add(this);
    }

    internal static bool BufferAfterProjection(object? value)
    {
        if (Pending.Count == 0) return false;
        Pending.Add(value);
        return true;
    }

    internal static void FlushConsole(bool windowsPowerShell, Action<object?> writeScalar)
    {
        var index = 0;
        var wroteTable = false;
        while (index < Pending.Count)
        {
            if (Pending[index] is not PowerShellStrictProjectionRecord first)
            {
                writeScalar(Pending[index++]);
                continue;
            }
            var end = index + 1;
            while (end < Pending.Count && Pending[end] is PowerShellStrictProjectionRecord next &&
                   first.Names.SequenceEqual(next.Names, StringComparer.Ordinal))
                end++;
            if (wroteTable)
            {
                WriteTableFooter(windowsPowerShell);
                Console.WriteLine();
            }
            WriteTable(first.Names, index, end, windowsPowerShell);
            wroteTable = true;
            index = end;
        }
        if (wroteTable) WriteTableFooter(windowsPowerShell);
        Pending.Clear();
    }

    private static void WriteTableFooter(bool windowsPowerShell)
    {
        Console.WriteLine();
        if (windowsPowerShell) Console.WriteLine();
    }

    private static void WriteTable(string[] names, int start, int end, bool windowsPowerShell)
    {
        var widths = names.Select(static name => name.Length).ToArray();
        for (var index = start; index < end; index++)
        {
            var record = (PowerShellStrictProjectionRecord)Pending[index]!;
            for (var column = 0; column < names.Length; column++)
                widths[column] = Math.Max(widths[column], record.Values[column].Length);
        }

        WriteRow(names);
        WriteRow(names.Select(static name => new string('-', name.Length)).ToArray());
        for (var index = start; index < end; index++)
            WriteRow(((PowerShellStrictProjectionRecord)Pending[index]!).Values);
        void WriteRow(string[] values)
        {
            var columns = values.Select((value, index) => value.PadRight(widths[index]));
            var row = string.Join(" ", columns);
            Console.WriteLine(windowsPowerShell ? row : row.TrimEnd());
        }
    }
}
