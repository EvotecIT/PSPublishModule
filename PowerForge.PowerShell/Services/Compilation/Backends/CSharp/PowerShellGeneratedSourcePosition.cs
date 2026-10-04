using System.Text;

namespace PowerForge;

/// <summary>Calculates one-based generated C# coordinates for source-map publication.</summary>
internal static class PowerShellGeneratedSourcePosition
{
    internal static Position Get(StringBuilder builder)
    {
        var line = 1;
        var column = 1;
        // StringBuilder's indexer walks its linked chunks. Scan a contiguous
        // snapshot so large generated methods do not multiply chunk traversal.
        var source = builder.ToString();
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else if (source[index] != '\r')
            {
                column++;
            }
        }
        return new Position(line, column);
    }

    internal readonly struct Position
    {
        internal Position(int line, int column)
        {
            Line = line;
            Column = column;
        }

        internal int Line { get; }
        internal int Column { get; }
    }
}
