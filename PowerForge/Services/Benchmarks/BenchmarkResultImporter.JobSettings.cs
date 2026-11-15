using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class BenchmarkResultImporter
{
    /// <summary>
    /// Records the BenchmarkDotNet job characteristics of CSV reports as run metadata. They are kept out of
    /// row variables, so this is where the run configuration stays visible. A characteristic whose value
    /// differs between rows is recorded as "varies"; the raw value stays in the source report.
    /// </summary>
    private static void ApplyBenchmarkDotNetJobSettings(BenchmarkRunResult result, IEnumerable<string> csvFiles)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in csvFiles)
        {
            var records = ReadCsvRecords(file, out _);
            if (records.Length < 2 || !LooksLikeBenchmarkDotNetCsv(records[0]))
                continue;

            var headers = records[0];
            for (var column = 0; column < headers.Length; column++)
            {
                var name = headers[column].Trim();
                if (!BenchmarkDotNetColumns.IsJobCharacteristic(name))
                    continue;

                foreach (var record in records.Skip(1))
                {
                    if (column >= record.Length || record.All(string.IsNullOrWhiteSpace))
                        continue;
                    var value = record[column].Trim();
                    if (!settings.TryGetValue(name, out var existing))
                        settings[name] = value;
                    else if (!string.Equals(existing, value, StringComparison.Ordinal))
                        settings[name] = "varies";
                }
            }
        }

        foreach (var setting in settings.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            result.Metadata[BenchmarkDotNetColumns.JobMetadataPrefix + setting.Key] = setting.Value;
    }
}
