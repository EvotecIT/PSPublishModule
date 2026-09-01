using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private static void ClearReadOnlyAttributesRecursively(string target, string? allowedRootPath)
    {
        EnsureDeleteTargetWithinRoot(target, allowedRootPath);

        var targetAttributes = File.GetAttributes(target);
        EnsureNotReparsePoint(target, targetAttributes);

        var pending = new Stack<string>();
        pending.Push(target);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var currentAttributes = File.GetAttributes(current);
            EnsureNotReparsePoint(current, currentAttributes);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                ClearReadOnlyAttribute(entry, attributes);
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
            }
        }

        ClearReadOnlyAttribute(target, targetAttributes);
    }

    private static void EnsureNotReparsePoint(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Refusing to traverse reparse-point cleanup path '{path}'.");
    }

    private static void ClearReadOnlyAttribute(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
