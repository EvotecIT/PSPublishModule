using System.Text;

namespace PowerForge;

/// <summary>Calculates one-based generated C# coordinates for source-map publication.</summary>
internal static class PowerShellGeneratedSourcePosition
{
    internal static Position Get(StringBuilder builder)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < builder.Length; index++)
        {
            if (builder[index] == '\n')
            {
                line++;
                column = 1;
            }
            else if (builder[index] != '\r')
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
