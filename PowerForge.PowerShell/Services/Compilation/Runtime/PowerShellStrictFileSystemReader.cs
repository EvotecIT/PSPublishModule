using System.Text.RegularExpressions;
using System.Globalization;

namespace PowerForge.Generated.Runtime;

/// <summary>Enumerates the filesystem records used by a typed recursive pipeline.</summary>
internal static class PowerShellStrictFileSystemReader
{
    internal static IEnumerable<FileInfo> ReadFiles(string? path)
        => Read(path, filesOnly: true).Cast<FileInfo>();

    internal static IEnumerable<FileSystemInfo> ReadItems(string? path)
        => Read(path, filesOnly: false);

    internal static string ResolvePath(string path, bool literal, string source, int line, int column, int length,
        string authoredLine, string invokedName, bool windowsPowerShell)
    {
        if (path.Length == 0)
        {
            const string message = "Cannot bind argument to parameter 'Path' because it is an empty string.";
            throw new PowerShellScriptCommandError(message, message + Environment.NewLine, line, new ArgumentException(message));
        }
        var fullPath = Path.GetFullPath(path);
        var matches = ResolveEntries(fullPath, literal);
        if (matches.Length != 0) return string.Join(" ", matches);
        ReportUnresolvedPath(fullPath, source, line, column, length, authoredLine, invokedName, windowsPowerShell);
        return string.Empty;
    }

    internal static bool HasWildcard(string path) => path.IndexOfAny(new[] { '*', '?', '[' }) >= 0;

    internal static string[] ResolveEntries(string fullPath, bool literal)
    {
        if (literal || !HasWildcard(fullPath))
            return File.Exists(fullPath) || Directory.Exists(fullPath) ? new[] { fullPath } : Array.Empty<string>();
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return Array.Empty<string>();
        var matcher = CreateLeafMatcher(Path.GetFileName(fullPath));
        return Visible(new DirectoryInfo(parent!).GetFileSystemInfos()).Where(entry => matcher.IsMatch(entry.Name))
            .Select(static entry => entry.FullName).ToArray();
    }

    private static void ReportUnresolvedPath(string path, string source, int line, int column, int length,
        string authoredLine, string invokedName, bool windowsPowerShell)
    {
        var lineNumber = line.ToString(CultureInfo.InvariantCulture);
        var indent = new string(' ', Math.Max(0, column - 1));
        var marker = new string('~', Math.Max(1, length));
        var message = $"Cannot find path '{path}' because it does not exist.";
        if (windowsPowerShell)
        {
            var chars = column.ToString(CultureInfo.InvariantCulture);
            var target = path.Length <= 32 ? path : path.Substring(0, 15) + "..." + path.Substring(path.Length - 14);
            Console.Error.WriteLine($"{invokedName} : {message}");
            Console.Error.WriteLine($"At {source}:{lineNumber} char:{chars}");
            Console.Error.WriteLine("+ " + authoredLine);
            Console.Error.WriteLine("+ " + indent + marker);
            Console.Error.WriteLine($"    + CategoryInfo          : ObjectNotFound: ({target}:String) [Resolve-Path], ItemNotFoundException");
            Console.Error.WriteLine("    + FullyQualifiedErrorId : PathNotFound,Microsoft.PowerShell.Commands.ResolvePathCommand");
            Console.Error.WriteLine(" ");
            return;
        }
        var linePadding = new string(' ', Math.Max(4, lineNumber.Length) + 1);
        Console.Error.WriteLine($"Resolve-Path: {source}:{lineNumber}");
        Console.Error.WriteLine("Line |");
        Console.Error.WriteLine(lineNumber.PadLeft(4) + " |  " + authoredLine);
        Console.Error.WriteLine(linePadding + "|  " + indent + marker);
        Console.Error.WriteLine(linePadding + "| " + message);
    }

    private static IEnumerable<FileSystemInfo> Read(string? path, bool filesOnly)
    {
        var fullPath = string.IsNullOrEmpty(path) ? Environment.CurrentDirectory : Path.GetFullPath(path!);
        if (Directory.Exists(fullPath))
        {
            foreach (var record in Descendants(new DirectoryInfo(fullPath), filesOnly))
                yield return record;
            yield break;
        }
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) yield break;
        var leaf = Path.GetFileName(fullPath);
        var matcher = CreateLeafMatcher(leaf);
        foreach (var record in Search(new DirectoryInfo(parent), matcher, filesOnly))
            yield return record;
    }

    private static Regex CreateLeafMatcher(string leaf)
    {
        var pattern = new System.Text.StringBuilder("\\A");
        for (var index = 0; index < leaf.Length; index++)
        {
            var value = leaf[index];
            if (value == '*') pattern.Append(".*");
            else if (value == '?') pattern.Append('.');
            else if (value == '[' && leaf.IndexOf(']', index + 1) is var last && last > index + 1)
            {
                var first = index + 1;
                var range = leaf.Substring(first, last - first).Replace("\\", "\\\\").Replace("[", "\\[").Replace("^", "\\^");
                pattern.Append('[').Append(range).Append(']');
                index = last;
            }
            else pattern.Append(Regex.Escape(value.ToString()));
        }
        return new Regex(pattern.Append("\\z").ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    }

    private static IEnumerable<FileSystemInfo> Descendants(DirectoryInfo directory, bool filesOnly)
    {
        var folders = Visible(directory.GetDirectories());
        if (!filesOnly)
            foreach (var folder in folders)
                yield return folder;
        foreach (var file in Visible(directory.GetFiles()))
            yield return file;
        foreach (var folder in folders)
            if (CanDescend(folder))
                foreach (var child in Descendants(folder, filesOnly))
                    yield return child;
    }

    private static IEnumerable<FileSystemInfo> Search(DirectoryInfo directory, Regex matcher, bool filesOnly)
    {
        foreach (var folder in Visible(directory.GetDirectories()))
        {
            if (!filesOnly && matcher.IsMatch(folder.Name))
                yield return folder;
            if (CanDescend(folder))
                foreach (var child in Search(folder, matcher, filesOnly))
                    yield return child;
        }
        foreach (var file in Visible(directory.GetFiles()))
            if (matcher.IsMatch(file.Name))
                yield return file;
    }

    private static TEntry[] Visible<TEntry>(TEntry[] entries) where TEntry : FileSystemInfo
        => entries.Where(entry => (entry.Attributes & FileAttributes.Hidden) == 0)
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static bool CanDescend(DirectoryInfo directory)
        => (directory.Attributes & FileAttributes.ReparsePoint) == 0;
}
