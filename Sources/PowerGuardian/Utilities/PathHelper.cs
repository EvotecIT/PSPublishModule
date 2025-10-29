using System;
using System.IO;

namespace PowerGuardian;

internal static class PathHelper
{
    public static string Combine(string a, string b) => Path.Combine(a, b);
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        return Path.GetFullPath(path);
    }

    public static void EnsureDirectory(string dir)
    {
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }
}

