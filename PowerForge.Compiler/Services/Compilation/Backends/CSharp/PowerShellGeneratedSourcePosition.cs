using System.Text;

namespace PowerForge;

/// <summary>Calculates one-based generated C# coordinates for source-map publication.</summary>
internal static class PowerShellGeneratedSourcePosition
{
    internal static Position Get(StringBuilder builder)
        => Advance(builder.ToString(), new Position(1, 1));

    private static Position Advance(string source, Position start)
    {
        var line = start.Line;
        var column = start.Column;
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

    /// <summary>Tracks builders that the function emitter only appends to.</summary>
    internal sealed class AppendTracker
    {
        private StringBuilder? _builder;
        private int _length;
        private Position _position = new(1, 1);

        internal Position Get(StringBuilder builder)
        {
            if (!ReferenceEquals(_builder, builder) || builder.Length < _length)
            {
                _builder = builder;
                _length = 0;
                _position = new Position(1, 1);
            }
            _position = Advance(builder.ToString(_length, builder.Length - _length), _position);
            _length = builder.Length;
            return _position;
        }
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
