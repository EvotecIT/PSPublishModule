using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class ArtefactCopyMappingsTransformationAttribute : ArgumentTransformationAttribute
{
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
    {
        if (inputData is null) return Array.Empty<ArtefactCopyMapping>();
        inputData = Unwrap(inputData);

        if (inputData is ArtefactCopyMapping[] arr) return arr;
        if (inputData is ArtefactCopyMapping single) return new[] { single };

        if (inputData is IDictionary dict)
            return ConvertDictionary(dict);

        if (inputData is IEnumerable enumerable && inputData is not string)
        {
            var list = new List<ArtefactCopyMapping>();
            foreach (var item in enumerable)
            {
                var unwrapped = item is null ? null : Unwrap(item);
                if (unwrapped is null) continue;

                if (unwrapped is ArtefactCopyMapping m)
                {
                    list.Add(m);
                    continue;
                }

                if (unwrapped is IDictionary d)
                {
                    list.AddRange(ConvertDictionary(d));
                }
            }
            return list.ToArray();
        }

        throw new ArgumentTransformationMetadataException($"Unsupported value for copy mappings: {inputData.GetType().FullName}");
    }

    private static ArtefactCopyMapping[] ConvertDictionary(IDictionary dict)
    {
        if (TryGetString(dict, "Source", out var source) && TryGetString(dict, "Destination", out var destination))
            return new[] { new ArtefactCopyMapping { Source = source!, Destination = destination! } };

        var list = new List<ArtefactCopyMapping>();
        foreach (DictionaryEntry entry in dict)
        {
            var src = entry.Key?.ToString();
            var dst = entry.Value?.ToString();
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst)) continue;
            list.Add(new ArtefactCopyMapping { Source = src!, Destination = dst! });
        }
        return list.ToArray();
    }

    private static object Unwrap(object value)
        => value is PSObject pso ? (pso.BaseObject ?? value) : value;

    private static bool TryGetString(IDictionary dict, string key, out string? value)
    {
        value = null;
        try
        {
            if (dict.Contains(key))
            {
                value = dict[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) value = value!.Trim();
            }
        }
        catch { /* ignore */ }
        return !string.IsNullOrWhiteSpace(value);
    }
}

internal sealed class DeliveryImportantLinksTransformationAttribute : ArgumentTransformationAttribute
{
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
    {
        if (inputData is null) return Array.Empty<DeliveryImportantLink>();
        inputData = Unwrap(inputData);

        if (inputData is DeliveryImportantLink[] arr) return arr;
        if (inputData is DeliveryImportantLink single) return new[] { single };

        if (inputData is IDictionary dict)
        {
            var one = TryParse(dict);
            return one is null ? Array.Empty<DeliveryImportantLink>() : new[] { one };
        }

        if (inputData is IEnumerable enumerable && inputData is not string)
        {
            var list = new List<DeliveryImportantLink>();
            foreach (var item in enumerable)
            {
                var unwrapped = item is null ? null : Unwrap(item);
                if (unwrapped is null) continue;

                if (unwrapped is DeliveryImportantLink l) { list.Add(l); continue; }
                if (unwrapped is IDictionary d)
                {
                    var parsed = TryParse(d);
                    if (parsed is not null) list.Add(parsed);
                }
            }
            return list.ToArray();
        }

        throw new ArgumentTransformationMetadataException($"Unsupported value for ImportantLinks: {inputData.GetType().FullName}");
    }

    private static DeliveryImportantLink? TryParse(IDictionary dict)
    {
        var title = GetString(dict, "Title") ?? GetString(dict, "Name");
        var url = GetString(dict, "Url") ?? GetString(dict, "Link");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) return null;
        return new DeliveryImportantLink { Title = title!, Url = url! };
    }

    private static string? GetString(IDictionary dict, string key)
    {
        try
        {
            if (!dict.Contains(key)) return null;
            var v = dict[key]?.ToString();
            return string.IsNullOrWhiteSpace(v) ? null : v!.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static object Unwrap(object value)
        => value is PSObject pso ? (pso.BaseObject ?? value) : value;
}

internal sealed class IncludeToArrayEntriesTransformationAttribute : ArgumentTransformationAttribute
{
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
    {
        if (inputData is null) return Array.Empty<IncludeToArrayEntry>();
        inputData = Unwrap(inputData);

        if (inputData is IncludeToArrayEntry[] arr) return arr;
        if (inputData is IncludeToArrayEntry single) return new[] { single };

        if (inputData is IDictionary dict)
            return ConvertDictionary(dict);

        if (inputData is IEnumerable enumerable && inputData is not string)
        {
            var list = new List<IncludeToArrayEntry>();
            foreach (var item in enumerable)
            {
                var unwrapped = item is null ? null : Unwrap(item);
                if (unwrapped is null) continue;

                if (unwrapped is IncludeToArrayEntry e) { list.Add(e); continue; }
                if (unwrapped is IDictionary d)
                    list.AddRange(ConvertDictionary(d));
            }
            return list.ToArray();
        }

        throw new ArgumentTransformationMetadataException($"Unsupported value for IncludeToArray: {inputData.GetType().FullName}");
    }

    private static IncludeToArrayEntry[] ConvertDictionary(IDictionary dict)
    {
        if (TryGetString(dict, "Key", out var key))
        {
            var values = GetValues(dict, "Values");
            return string.IsNullOrWhiteSpace(key) ? Array.Empty<IncludeToArrayEntry>() : new[]
            {
                new IncludeToArrayEntry { Key = key!, Values = values }
            };
        }

        var list = new List<IncludeToArrayEntry>();
        foreach (DictionaryEntry entry in dict)
        {
            var k = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(k)) continue;
            list.Add(new IncludeToArrayEntry { Key = k!.Trim(), Values = ConvertValues(entry.Value) });
        }
        return list.ToArray();
    }

    private static string[] GetValues(IDictionary dict, string key)
    {
        try
        {
            if (!dict.Contains(key)) return Array.Empty<string>();
            return ConvertValues(dict[key]);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] ConvertValues(object? value)
    {
        return value switch
        {
            null => Array.Empty<string>(),
            string s => string.IsNullOrWhiteSpace(s) ? Array.Empty<string>() : new[] { s.Trim() },
            IEnumerable e when value is not string => e.Cast<object?>()
                .Select(v => v?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .ToArray(),
            _ => new[] { value.ToString() ?? string.Empty }
        };
    }

    private static object Unwrap(object value)
        => value is PSObject pso ? (pso.BaseObject ?? value) : value;

    private static bool TryGetString(IDictionary dict, string key, out string? value)
    {
        value = null;
        try
        {
            if (dict.Contains(key))
            {
                value = dict[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) value = value!.Trim();
            }
        }
        catch { /* ignore */ }
        return !string.IsNullOrWhiteSpace(value);
    }
}

internal sealed class PlaceHolderReplacementsTransformationAttribute : ArgumentTransformationAttribute
{
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
    {
        if (inputData is null) return Array.Empty<PlaceHolderReplacement>();
        inputData = Unwrap(inputData);

        if (inputData is PlaceHolderReplacement[] arr) return arr;
        if (inputData is PlaceHolderReplacement single) return new[] { single };

        if (inputData is IDictionary dict)
        {
            var one = TryParse(dict);
            return one is null ? Array.Empty<PlaceHolderReplacement>() : new[] { one };
        }

        if (inputData is IEnumerable enumerable && inputData is not string)
        {
            var list = new List<PlaceHolderReplacement>();
            foreach (var item in enumerable)
            {
                var unwrapped = item is null ? null : Unwrap(item);
                if (unwrapped is null) continue;

                if (unwrapped is PlaceHolderReplacement r) { list.Add(r); continue; }
                if (unwrapped is IDictionary d)
                {
                    var parsed = TryParse(d);
                    if (parsed is not null) list.Add(parsed);
                }
            }
            return list.ToArray();
        }

        throw new ArgumentTransformationMetadataException($"Unsupported value for CustomReplacement: {inputData.GetType().FullName}");
    }

    private static PlaceHolderReplacement? TryParse(IDictionary dict)
    {
        var find = GetString(dict, "Find");
        var replace = GetString(dict, "Replace");
        if (string.IsNullOrWhiteSpace(find) || replace is null) return null;
        return new PlaceHolderReplacement { Find = find!, Replace = replace };
    }

    private static string? GetString(IDictionary dict, string key)
    {
        try
        {
            if (!dict.Contains(key)) return null;
            return dict[key]?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static object Unwrap(object value)
        => value is PSObject pso ? (pso.BaseObject ?? value) : value;
}
