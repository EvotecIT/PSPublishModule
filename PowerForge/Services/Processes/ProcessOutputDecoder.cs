using System.Text;

namespace PowerForge;

/// <summary>Detects Unicode preambles without waiting for a complete line or filling a text-reader buffer.</summary>
internal sealed class ProcessOutputDecoder
{
    private static readonly Encoding[] PreambleEncodings = {
        new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true),
        new UTF32Encoding(false, true), new UTF32Encoding(true, true)
    };
    private static readonly byte[][] Preambles = PreambleEncodings.Select(encoding => encoding.GetPreamble()).ToArray();
    private readonly Encoding _fallback;
    private readonly byte[] _prefix = new byte[4];
    private int _prefixCount;
    private int _preambleLength;
    private Decoder? _decoder;

    internal ProcessOutputDecoder(Encoding fallback) => _fallback = fallback;

    internal int Decode(byte[] bytes, int count, char[] characters, bool flush)
    {
        var offset = 0;
        while (_decoder is null && offset < count)
        {
            _prefix[_prefixCount++] = bytes[offset++];
            SelectEncoding(flush: false);
        }
        if (_decoder is null && flush) SelectEncoding(flush: true);
        if (_decoder is null) return 0;

        var written = 0;
        if (_prefixCount > 0)
        {
            written = _decoder.GetChars(_prefix, _preambleLength, _prefixCount - _preambleLength,
                characters, 0, flush: false);
            _prefixCount = 0;
        }
        return written + _decoder.GetChars(bytes, offset, count - offset, characters, written, flush);
    }

    private void SelectEncoding(bool flush)
    {
        var selected = -1;
        var mayNeedMore = false;
        for (var candidate = 0; candidate < Preambles.Length; candidate++)
        {
            var preamble = Preambles[candidate];
            var matches = true;
            for (var index = 0; index < Math.Min(_prefixCount, preamble.Length); index++)
            {
                if (_prefix[index] != preamble[index]) { matches = false; break; }
            }
            if (!matches) continue;
            if (_prefixCount < preamble.Length) mayNeedMore = true;
            else if (selected < 0 || preamble.Length > Preambles[selected].Length) selected = candidate;
        }
        // FF FE can be UTF-16LE or the beginning of UTF-32LE. Wait only
        // while the bytes actually remain a possible longer preamble.
        if (mayNeedMore && !flush) return;
        _decoder = (selected < 0 ? _fallback : PreambleEncodings[selected]).GetDecoder();
        _preambleLength = selected < 0 ? 0 : Preambles[selected].Length;
    }
}
