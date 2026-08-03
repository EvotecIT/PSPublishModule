namespace PowerForge.Web;

/// <summary>Validates and reverses PNG scanline filters into a canonical pixel-byte stream.</summary>
internal sealed class PngScanlineFilterValidator
{
    private readonly long[] _rowBytes;
    private readonly uint[] _rowCounts;
    private readonly int _bytesPerPixel;
    private readonly MemoryStream _canonicalBytes = new();
    private int _passIndex;
    private uint _rowIndex;
    private long _remainingRowBytes;
    private bool _expectsFilter = true;
    private byte _filter;
    private int _rowPosition;
    private byte[]? _currentRow;
    private byte[]? _previousRow;

    internal PngScanlineFilterValidator(
        uint width,
        uint height,
        byte bitDepth,
        byte colorType,
        byte interlaceMethod)
    {
        var channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidOperationException("Unsupported PNG color type.")
        };
        _bytesPerPixel = Math.Max(1, checked((channels * bitDepth + 7) / 8));
        if (interlaceMethod == 0)
        {
            _rowBytes = [ComputeRowBytes(width, channels, bitDepth)];
            _rowCounts = [height];
            AdvanceEmptyPasses();
            return;
        }

        ReadOnlySpan<(uint X, uint Y, uint Dx, uint Dy)> passes =
        [
            (0, 0, 8, 8),
            (4, 0, 8, 8),
            (0, 4, 4, 8),
            (2, 0, 4, 4),
            (0, 2, 2, 4),
            (1, 0, 2, 2),
            (0, 1, 1, 2)
        ];
        _rowBytes = new long[passes.Length];
        _rowCounts = new uint[passes.Length];
        for (var index = 0; index < passes.Length; index++)
        {
            var pass = passes[index];
            var passWidth = PassLength(width, pass.X, pass.Dx);
            _rowBytes[index] = ComputeRowBytes(passWidth, channels, bitDepth);
            _rowCounts[index] = PassLength(height, pass.Y, pass.Dy);
        }
        AdvanceEmptyPasses();
    }

    internal void Consume(byte[] buffer, int count, string displayPath)
    {
        for (var index = 0; index < count; index++)
        {
            if (_passIndex >= _rowBytes.Length)
                throw new InvalidOperationException($"Visual-story APNG frame expands beyond its dimensions: {displayPath}");
            if (_expectsFilter)
            {
                if (buffer[index] > 4)
                    throw new InvalidOperationException($"Visual-story APNG frame contains an invalid scanline filter: {displayPath}");
                _filter = buffer[index];
                _remainingRowBytes = _rowBytes[_passIndex];
                var rowLength = checked((int)_remainingRowBytes);
                if (_currentRow is null || _currentRow.Length != rowLength)
                {
                    _currentRow = new byte[rowLength];
                    _previousRow = new byte[rowLength];
                }
                _rowPosition = 0;
                _expectsFilter = false;
                continue;
            }

            var filtered = buffer[index];
            var left = _rowPosition >= _bytesPerPixel ? _currentRow![_rowPosition - _bytesPerPixel] : (byte)0;
            var up = _previousRow![_rowPosition];
            var upLeft = _rowPosition >= _bytesPerPixel ? _previousRow[_rowPosition - _bytesPerPixel] : (byte)0;
            var value = _filter switch
            {
                0 => filtered,
                1 => unchecked((byte)(filtered + left)),
                2 => unchecked((byte)(filtered + up)),
                3 => unchecked((byte)(filtered + ((left + up) >> 1))),
                4 => unchecked((byte)(filtered + Paeth(left, up, upLeft))),
                _ => throw new InvalidOperationException($"Visual-story APNG frame contains an invalid scanline filter: {displayPath}")
            };
            _currentRow![_rowPosition++] = value;
            _canonicalBytes.WriteByte(value);
            _remainingRowBytes--;
            if (_remainingRowBytes != 0) continue;
            (_currentRow, _previousRow) = (_previousRow, _currentRow);
            Array.Clear(_currentRow!);
            _rowIndex++;
            _expectsFilter = true;
            if (_rowIndex < _rowCounts[_passIndex]) continue;
            _passIndex++;
            _rowIndex = 0;
            _currentRow = null;
            _previousRow = null;
            AdvanceEmptyPasses();
        }
    }

    internal void EnsureComplete(string displayPath)
    {
        if (_passIndex != _rowBytes.Length || !_expectsFilter)
            throw new InvalidOperationException($"Visual-story APNG frame has incomplete pixel data: {displayPath}");
    }

    internal string GetCanonicalSignature()
    {
        var hash = System.Security.Cryptography.SHA256.HashData(_canonicalBytes.GetBuffer().AsSpan(0, checked((int)_canonicalBytes.Length)));
        return Convert.ToBase64String(hash);
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upLeft);
        return leftDistance <= upDistance && leftDistance <= upperLeftDistance
            ? left
            : upDistance <= upperLeftDistance ? up : upLeft;
    }

    private void AdvanceEmptyPasses()
    {
        while (_passIndex < _rowBytes.Length &&
               (_rowBytes[_passIndex] == 0 || _rowCounts[_passIndex] == 0))
            _passIndex++;
    }

    private static long ComputeRowBytes(uint width, int channels, byte bitDepth)
        => checked(((long)width * channels * bitDepth + 7) / 8);

    private static uint PassLength(uint length, uint start, uint step)
        => length <= start ? 0 : (length - start + step - 1) / step;
}
