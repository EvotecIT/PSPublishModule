using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class BenchmarkResultImporter
{
    /// <summary>
    /// Job characteristics with the same value in every row of a BenchmarkDotNet CSV report. They describe how
    /// the whole run was configured, so they are recorded once as run metadata instead of as case variables.
    /// A characteristic that differs between rows (two runtimes in one report) stays a variable, because it is
    /// part of what distinguishes the measured rows.
    /// </summary>
    private static IEnumerable<string> SharedBenchmarkDotNetJobColumns(string[][] records)
        => ReadSharedBenchmarkDotNetJobSettings(records).Keys;

    private static Dictionary<string, string> ReadSharedBenchmarkDotNetJobSettings(string[][] records)
    {
        var shared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (records.Length < 2 || !LooksLikeBenchmarkDotNetCsv(records[0]))
            return shared;

        var headers = records[0];
        var rows = records.Skip(1).Where(record => record.Length > 0 && !record.All(string.IsNullOrWhiteSpace)).ToArray();
        for (var column = 0; column < headers.Length; column++)
        {
            var name = headers[column].Trim();
            if (!BenchmarkDotNetColumns.IsJobCharacteristic(name))
                continue;
            var values = rows.Select(record => column < record.Length ? record[column].Trim() : string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (values.Length == 1)
                shared[name] = values[0];
        }
        return shared;
    }

    /// <summary>Records the shared job characteristics of BenchmarkDotNet CSV reports as run metadata.</summary>
    private static void ApplyBenchmarkDotNetJobSettings(BenchmarkRunResult result, IEnumerable<string> csvFiles)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in csvFiles)
        {
            foreach (var setting in ReadSharedBenchmarkDotNetJobSettings(ReadCsvRecords(file, out _)))
            {
                if (settings.TryGetValue(setting.Key, out var existing) && !string.Equals(existing, setting.Value, StringComparison.Ordinal))
                    conflicting.Add(setting.Key);
                settings[setting.Key] = setting.Value;
            }
        }

        foreach (var setting in settings.Where(item => !conflicting.Contains(item.Key)).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            result.Metadata[BenchmarkDotNetColumns.JobMetadataPrefix + setting.Key] = setting.Value;
    }
}
