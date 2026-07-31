namespace PowerForge.Web;

/// <summary>Validates PNG scanline filter bytes without retaining decompressed frame payloads.</summary>
internal sealed class PngScanlineFilterValidator
{
    private readonly long[] _rowBytes;
    private readonly uint[] _rowCounts;
    private int _passIndex;
    private uint _rowIndex;
    private long _remainingRowBytes;
    private bool _expectsFilter = true;

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
                _remainingRowBytes = _rowBytes[_passIndex];
                _expectsFilter = false;
                continue;
            }

            _remainingRowBytes--;
            if (_remainingRowBytes != 0) continue;
            _rowIndex++;
            _expectsFilter = true;
            if (_rowIndex < _rowCounts[_passIndex]) continue;
            _passIndex++;
            _rowIndex = 0;
            AdvanceEmptyPasses();
        }
    }

    internal void EnsureComplete(string displayPath)
    {
        if (_passIndex != _rowBytes.Length || !_expectsFilter)
            throw new InvalidOperationException($"Visual-story APNG frame has incomplete pixel data: {displayPath}");
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
