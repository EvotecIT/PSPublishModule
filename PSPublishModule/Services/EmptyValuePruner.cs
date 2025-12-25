using System.Collections;
using System.Collections.Generic;

namespace PSPublishModule.Services;

internal static class EmptyValuePruner
{
    internal static void RemoveEmptyValues(IDictionary dictionary, bool recursive = false, int rerun = 0)
    {
        if (dictionary.Count == 0) return;

        PruneOnce(dictionary, recursive);
        for (var i = 0; i < rerun; i++)
        {
            PruneOnce(dictionary, recursive);
        }
    }

    private static void PruneOnce(IDictionary dictionary, bool recursive)
    {
        if (dictionary.Count == 0) return;

        var keys = new List<object>(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary)
        {
            keys.Add(entry.Key);
        }

        foreach (var key in keys)
        {
            var value = dictionary[key];
            if (recursive && value is IDictionary nested)
            {
                if (nested.Count == 0)
                {
                    dictionary.Remove(key);
                    continue;
                }

                PruneOnce(nested, recursive: true);
                if (nested.Count == 0)
                {
                    dictionary.Remove(key);
                }

                continue;
            }

            if (value is null)
            {
                dictionary.Remove(key);
                continue;
            }

            if (value is string s && s.Length == 0)
            {
                dictionary.Remove(key);
                continue;
            }

            if (value is IList list && list.Count == 0)
            {
                dictionary.Remove(key);
            }
        }
    }
}
