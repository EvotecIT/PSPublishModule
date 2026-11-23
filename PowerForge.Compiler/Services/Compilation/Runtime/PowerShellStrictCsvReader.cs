using System.Text;

namespace PowerForge.Generated.Runtime;

internal static class PowerShellStrictCsvReader
{
    internal static Dictionary<string, string>[] ReadFile(string path, string[] header, bool literal, int sourceLine)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException($"Cannot bind argument to parameter '{(literal ? "LiteralPath" : "Path")}' because it is an empty string.");
            if (!literal && PowerShellStrictFileSystemReader.HasWildcard(path))
            {
                var files = PowerShellStrictFileSystemReader.ResolveEntries(PowerShellStrictFileSystemReader.GetFullPath(path), literal: false);
                if (files.Length == 0)
                    throw new FileNotFoundException($"Cannot perform operation because the wildcard path {path} did not resolve to a file.");
                if (files.Length != 1)
                    throw new IOException("Cannot perform operation because the path resolved to more than one file. This command cannot operate on multiple files.");
                return Read(files[0], header).ToArray();
            }
            return Read(PowerShellStrictFileSystemReader.GetFullPath(path), header).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new PowerShellScriptCommandError(error.Message, error.Message + Environment.NewLine, sourceLine, error);
        }
    }

    private static IEnumerable<Dictionary<string, string>> Read(string path, string[] header, char delimiter = ',')
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (reader.Peek() == '#') reader.ReadLine();

        var fields = new List<string>(header.Length);
        var field = new StringBuilder();
        while (ReadRecord(reader, delimiter, fields, field))
        {
            if (fields.Count == 1 && fields[0].Length == 0) continue;
            var row = new Dictionary<string, string>(header.Length, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < header.Length; index++)
                row.Add(header[index], index < fields.Count ? fields[index] : string.Empty);
            yield return row;
        }
    }

    private static bool ReadRecord(TextReader reader, char delimiter, List<string> fields, StringBuilder field)
    {
        fields.Clear();
        field.Clear();
        var quoted = false;
        int next;
        while ((next = reader.Read()) != -1)
        {
            var value = (char)next;
            if (quoted)
            {
                if (value != '"')
                {
                    field.Append(value);
                    continue;
                }
                if (reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                    continue;
                }

                if (ReadSuffix(reader, delimiter, field, discardBlankSuffix: true))
                {
                    fields.Add(field.ToString());
                    return true;
                }
                fields.Add(field.ToString());
                field.Clear();
                quoted = false;
                continue;
            }

            if (value == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }
            if (IsRecordBreak(reader, value))
            {
                fields.Add(field.ToString());
                return true;
            }
            if (field.Length == 0)
            {
                if (value is ' ' or '\t') continue;
                if (value == '"')
                {
                    quoted = true;
                    continue;
                }
            }
            field.Append(value);
            if (value is not ('"' or ' ' or '\t')) continue;
            var endOfRecord = ReadSuffix(reader, delimiter, field, discardBlankSuffix: value != '"');
            fields.Add(field.ToString());
            field.Clear();
            if (endOfRecord) return true;
        }

        if (field.Length != 0) fields.Add(field.ToString());
        return fields.Count != 0;
    }

    private static bool ReadSuffix(TextReader reader, char delimiter, StringBuilder field, bool discardBlankSuffix)
    {
        var originalLength = field.Length;
        int next;
        while ((next = reader.Read()) != -1)
        {
            var value = (char)next;
            if (value == delimiter)
            {
                if (discardBlankSuffix) field.Length = originalLength;
                return false;
            }
            if (IsRecordBreak(reader, value)) break;
            field.Append(value);
            if (value is not (' ' or '\t')) discardBlankSuffix = false;
        }
        if (discardBlankSuffix) field.Length = originalLength;
        return true;
    }

    private static bool IsRecordBreak(TextReader reader, char value)
    {
        if (value == '\r' && reader.Peek() == '\n') reader.Read();
        return value is '\r' or '\n';
    }
}
