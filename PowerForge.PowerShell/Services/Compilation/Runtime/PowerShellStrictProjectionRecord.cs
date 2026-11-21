using System.Text.Json;

namespace PowerForge.Generated.Runtime;

/// <summary>One ordered record from an authored projection over a closed CLR shape.</summary>
internal sealed class PowerShellStrictProjectionRecord
{
    private static readonly List<PowerShellStrictProjectionRecord> Pending = new();
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

    internal void WriteConsole(bool windowsPowerShell)
    {
        if (Pending.Count != 0 && !Pending[0].Names.SequenceEqual(Names, StringComparer.Ordinal))
            FlushConsole(windowsPowerShell);
        if (Pending.Count == 0) Console.WriteLine();
        Pending.Add(this);
    }

    internal static void FlushConsole(bool windowsPowerShell)
    {
        if (Pending.Count == 0) return;
        var names = Pending[0].Names;
        var widths = names.Select(static name => name.Length).ToArray();
        foreach (var record in Pending)
        for (var index = 0; index < names.Length; index++)
            widths[index] = Math.Max(widths[index], record.Values[index].Length);

        WriteRow(names);
        WriteRow(names.Select(static name => new string('-', name.Length)).ToArray());
        foreach (var record in Pending)
            WriteRow(record.Values);
        Console.WriteLine();
        if (windowsPowerShell) Console.WriteLine();
        Pending.Clear();

        void WriteRow(string[] values)
        {
            var columns = values.Select((value, index) => value.PadRight(widths[index]));
            var row = string.Join(" ", columns);
            Console.WriteLine(windowsPowerShell ? row : row.TrimEnd());
        }
    }
}
