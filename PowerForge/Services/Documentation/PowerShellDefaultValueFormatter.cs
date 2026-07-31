using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Formats tagged PowerShell runtime values as stable, XML-safe PowerShell expressions.
/// </summary>
internal static class PowerShellDefaultValueFormatter
{
    /// <summary>
    /// Formats a captured runtime value for Markdown and MAML default-value metadata.
    /// </summary>
    /// <param name="value">Tagged value captured in the target PowerShell host.</param>
    /// <returns>A stable PowerShell expression, or an empty string when no value was captured.</returns>
    public static string Format(DocumentationRuntimeValue? value)
    {
        if (value is null) return string.Empty;
        if (value.Tokens is { Count: > 0 })
            return FormatTokens(value.Tokens);

        switch ((value.Kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "null":
                return "$null";
            case "string":
                return FormatString(value.Text ?? string.Empty, preserveCharacterType: false);
            case "stringcodeunits":
                return FormatString(DecodeUtf16CodeUnits(value.Text), preserveCharacterType: false);
            case "char":
                return FormatString(value.Text ?? string.Empty, preserveCharacterType: true);
            case "charcodeunit":
                return FormatCharacterCodeUnit(value.Text);
            case "boolean":
                return string.Equals(value.Text, "True", StringComparison.OrdinalIgnoreCase)
                    ? "$true"
                    : "$false";
            case "enum":
                return FormatEnum(value);
            case "type":
                return string.IsNullOrWhiteSpace(value.CanonicalTypeName)
                    ? string.Empty
                    : "[" + value.CanonicalTypeName!.Trim() + "]";
            case "double":
                return FormatFloatingPoint(value.Text, "double");
            case "single":
                return FormatFloatingPoint(value.Text, "single");
            case "decimal":
                return "[System.Decimal]::Parse('" + (value.Text ?? string.Empty).Replace("'", "''") +
                       "', [System.Globalization.CultureInfo]::InvariantCulture)";
            case "biginteger":
                return "[System.Numerics.BigInteger]::Parse('" + (value.Text ?? string.Empty).Replace("'", "''") +
                       "', [System.Globalization.CultureInfo]::InvariantCulture)";
            case "guid":
                return "[System.Guid]::ParseExact('" + (value.Text ?? string.Empty).Replace("'", "''") + "', 'D')";
            case "version":
                return "[System.Version]::Parse('" + (value.Text ?? string.Empty).Replace("'", "''") + "')";
            case "uricodeunits":
                return FormatUri(DecodeUtf16CodeUnits(value.Text), value.Name);
            case "dateonly":
                return "[System.DateOnly]::FromDayNumber(([int]" + (value.Text ?? string.Empty) + "))";
            case "timeonly":
                return "[System.TimeOnly]::new(([long]" + (value.Text ?? string.Empty) + "))";
            case "datetime":
                return "[System.DateTime]::new(([long]" + (value.Text ?? string.Empty) +
                       "), [System.DateTimeKind]::" + (value.Name ?? string.Empty) + ")";
            case "datetimeoffset":
                return FormatTemporalParseExact("System.DateTimeOffset", value.Text, "O", includeStyles: true);
            case "timespan":
                return FormatTemporalParseExact("System.TimeSpan", value.Text, "c", includeStyles: false);
            case "scriptblockcodeunits":
                return "[scriptblock]::Create(" + FormatString(DecodeUtf16CodeUnits(value.Text), preserveCharacterType: false) + ")";
            case "collection":
                return "@(" + string.Join(", ", (value.Items ?? new List<DocumentationRuntimeValue>()).Select(Format)) + ")";
            case "formattable":
            case "text":
                return value.Text ?? string.Empty;
            case "textcodeunits":
                return FormatFallbackText(DecodeUtf16CodeUnits(value.Text));
            default:
                return value.Text ?? string.Empty;
        }
    }

    /// <summary>
    /// Returns whether authored help text must be represented as an expression to
    /// remain safe in XML and fixed-fence Markdown metadata blocks.
    /// </summary>
    public static bool NeedsEncoding(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var character in text!)
        {
            if (character == '\r' || character == '\n' || !XmlConvert.IsXmlChar(character))
                return true;
        }
        return false;
    }

    private static string FormatEnum(DocumentationRuntimeValue value)
    {
        var typeName = (value.CanonicalTypeName ?? string.Empty).Trim();
        if (typeName.Length == 0) return value.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value.Name))
            return "[" + typeName + "]::" + value.Name!.Trim();
        var numericValue = value.Text ?? string.Empty;
        var underlyingTypeName = (value.UnderlyingTypeName ?? string.Empty).Trim();
        if (underlyingTypeName.Length > 0)
            numericValue = "([" + underlyingTypeName + "]" + numericValue + ")";
        return "[System.Enum]::ToObject([" + typeName + "], " + numericValue + ")";
    }

    private static string FormatFloatingPoint(string? text, string powerShellType)
    {
        var value = text ?? string.Empty;
        if (value.Equals("NaN", StringComparison.OrdinalIgnoreCase))
            return "[" + powerShellType + "]::NaN";
        if (value.Equals("Infinity", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("+Infinity", StringComparison.OrdinalIgnoreCase))
            return "[" + powerShellType + "]::PositiveInfinity";
        if (value.Equals("-Infinity", StringComparison.OrdinalIgnoreCase))
            return "[" + powerShellType + "]::NegativeInfinity";
        if (powerShellType.Equals("single", StringComparison.Ordinal))
        {
            if (value.Equals("-0", StringComparison.Ordinal)) value = "-0.0";
            else if (value.Equals("0", StringComparison.Ordinal)) value = "0.0";
            return "([single]" + value + ")";
        }
        if (value.Equals("-0", StringComparison.Ordinal)) return "-0.0";
        if (value.Equals("0", StringComparison.Ordinal)) return "0.0";
        return value;
    }

    private static string FormatTemporalParseExact(
        string typeName,
        string? text,
        string format,
        bool includeStyles)
    {
        var expression = "[" + typeName + "]::ParseExact('" +
                         (text ?? string.Empty).Replace("'", "''") + "', '" + format +
                         "', [System.Globalization.CultureInfo]::InvariantCulture";
        if (includeStyles)
            expression += ", [System.Globalization.DateTimeStyles]::RoundtripKind";
        return expression + ")";
    }

    private static string FormatUri(string text, string? kind)
    {
        var uriKind = string.Equals(kind, "Absolute", StringComparison.OrdinalIgnoreCase)
            ? "Absolute"
            : "Relative";
        return "[System.Uri]::new(" + FormatString(text, preserveCharacterType: false) +
               ", [System.UriKind]::" + uriKind + ")";
    }

    private static string FormatFallbackText(string text)
        => NeedsEncoding(text)
            ? FormatString(text, preserveCharacterType: false)
            : text;

    private static string FormatString(string text, bool preserveCharacterType)
    {
        if (preserveCharacterType)
        {
            var character = text.Length > 0 ? text[0] : '\0';
            return "([char]" + ((int)character).ToString(CultureInfo.InvariantCulture) + ")";
        }

        if (!NeedsEncoding(text))
            return "'" + text.Replace("'", "''") + "'";

        var parts = new List<string>();
        var segment = new List<char>();

        void FlushSegment()
        {
            if (segment.Count == 0) return;
            parts.Add("'" + new string(segment.ToArray()).Replace("'", "''") + "'");
            segment.Clear();
        }

        foreach (var character in text)
        {
            if (character != '\r' && character != '\n' && XmlConvert.IsXmlChar(character))
            {
                segment.Add(character);
                continue;
            }

            FlushSegment();
            parts.Add("([char]" + ((int)character).ToString(CultureInfo.InvariantCulture) + ")");
        }

        FlushSegment();
        return "(-join @(" + string.Join(", ", parts) + "))";
    }

    private static string FormatTokens(IReadOnlyList<DocumentationRuntimeValue> tokens)
    {
        var frames = new Stack<TokenFrame>();
        string? result = null;

        foreach (var token in tokens)
        {
            var kind = (token.Kind ?? string.Empty).Trim();
            if (kind.Equals("CollectionStart", StringComparison.OrdinalIgnoreCase))
            {
                frames.Push(new CollectionTokenFrame());
                continue;
            }

            if (kind.Equals("CollectionEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is CollectionTokenFrame))
                    throw new FormatException("The runtime default token stream contains an unexpected collection terminator.");
                Append(frames.Pop().Complete());
                continue;
            }

            if (kind.Equals("DictionaryStart", StringComparison.OrdinalIgnoreCase))
            {
                frames.Push(new DictionaryTokenFrame());
                continue;
            }

            if (kind.Equals("ArrayStart", StringComparison.OrdinalIgnoreCase))
            {
                frames.Push(new ArrayTokenFrame(token.CanonicalTypeName, token.Text, token.Name));
                continue;
            }

            if (kind.Equals("ArrayEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is ArrayTokenFrame))
                    throw new FormatException("The runtime default token stream contains an unexpected array terminator.");
                Append(frames.Pop().Complete());
                continue;
            }

            if (kind.Equals("DictionaryEntryStart", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is DictionaryTokenFrame dictionary))
                    throw new FormatException("The runtime default token stream contains a dictionary entry outside a dictionary.");
                dictionary.BeginEntry();
                continue;
            }

            if (kind.Equals("DictionaryEntryEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is DictionaryTokenFrame dictionary))
                    throw new FormatException("The runtime default token stream contains a dictionary entry terminator outside a dictionary.");
                dictionary.EndEntry();
                continue;
            }

            if (kind.Equals("DictionaryEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is DictionaryTokenFrame))
                    throw new FormatException("The runtime default token stream contains an unexpected dictionary terminator.");
                Append(frames.Pop().Complete());
                continue;
            }

            Append(Format(token));
        }

        if (frames.Count > 0)
            throw new FormatException("The runtime default token stream is missing a container terminator.");
        return result ?? throw new FormatException("The runtime default token stream is empty.");

        void Append(string value)
        {
            if (frames.Count > 0)
            {
                frames.Peek().Add(value);
                return;
            }

            if (result is not null)
                throw new FormatException("The runtime default token stream contains trailing values.");
            result = value;
        }
    }

    private abstract class TokenFrame
    {
        public abstract void Add(string value);
        public abstract string Complete();
    }

    private sealed class CollectionTokenFrame : TokenFrame
    {
        private readonly List<string> _items = new();

        public override void Add(string value) => _items.Add(value);

        public override string Complete() => "@(" + string.Join(", ", _items) + ")";
    }

    private sealed class DictionaryTokenFrame : TokenFrame
    {
        private readonly List<KeyValuePair<string, string>> _entries = new();
        private string? _key;
        private string? _value;
        private bool _entryOpen;

        public void BeginEntry()
        {
            if (_entryOpen)
                throw new FormatException("The runtime default token stream contains nested dictionary entry markers.");
            _entryOpen = true;
            _key = null;
            _value = null;
        }

        public override void Add(string value)
        {
            if (!_entryOpen)
                throw new FormatException("The runtime default token stream contains a dictionary value outside an entry.");
            if (_key is null)
                _key = value;
            else if (_value is null)
                _value = value;
            else
                throw new FormatException("The runtime default token stream contains more than two values in a dictionary entry.");
        }

        public void EndEntry()
        {
            if (!_entryOpen || _key is null || _value is null)
                throw new FormatException("The runtime default token stream contains an incomplete dictionary entry.");
            _entries.Add(new KeyValuePair<string, string>(_key, _value));
            _entryOpen = false;
        }

        public override string Complete()
        {
            if (_entryOpen)
                throw new FormatException("The runtime default token stream ends inside a dictionary entry.");
            return "@{ " + string.Join("; ", _entries.Select(entry => "(" + entry.Key + ") = " + entry.Value)) + " }";
        }
    }

    private sealed class ArrayTokenFrame : TokenFrame
    {
        private readonly string _elementTypeName;
        private readonly int[] _lengths;
        private readonly int[] _lowerBounds;
        private readonly List<string> _items = new();

        public ArrayTokenFrame(string? elementTypeName, string? lengths, string? lowerBounds)
        {
            _elementTypeName = elementTypeName?.Trim() ?? string.Empty;
            _lengths = ParseDimensions(lengths);
            _lowerBounds = ParseDimensions(lowerBounds);
            if (string.IsNullOrEmpty(_elementTypeName) ||
                _lengths.Length < 2 ||
                _lengths.Length != _lowerBounds.Length ||
                _lengths.Any(length => length < 0))
            {
                throw new FormatException("The runtime default token stream contains invalid multidimensional array metadata.");
            }
        }

        public override void Add(string value) => _items.Add(value);

        public override string Complete()
        {
            long expectedCount = 1;
            foreach (var length in _lengths)
                expectedCount = checked(expectedCount * length);
            if (expectedCount != _items.Count)
                throw new FormatException("The runtime default token stream contains the wrong number of array elements.");

            var statements = new List<string>
            {
                "$array = [System.Array]::CreateInstance([" + _elementTypeName + "], [int[]]@(" +
                string.Join(", ", _lengths) + "), [int[]]@(" + string.Join(", ", _lowerBounds) + "))"
            };
            var indices = (int[])_lowerBounds.Clone();
            foreach (var item in _items)
            {
                statements.Add("$array.SetValue(" + item + ", [int[]]@(" + string.Join(", ", indices) + "))");
                IncrementIndices(indices);
            }
            statements.Add("Write-Output -NoEnumerate $array");
            return "& { " + string.Join("; ", statements) + " }";
        }

        private void IncrementIndices(int[] indices)
        {
            for (var dimension = indices.Length - 1; dimension >= 0; dimension--)
            {
                indices[dimension]++;
                if (indices[dimension] < _lowerBounds[dimension] + _lengths[dimension])
                    return;
                indices[dimension] = _lowerBounds[dimension];
            }
        }

        private static int[] ParseDimensions(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
            return value!.Split(',')
                .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new FormatException("The runtime default token stream contains a non-integer array dimension."))
                .ToArray();
        }
    }

    private static string FormatCharacterCodeUnit(string? text)
    {
        if (!ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("The runtime default character code unit is invalid.");
        return "([char]" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    internal static string DecodeUtf16CodeUnits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var values = text!.Split(',');
        var characters = new char[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            if (!ushort.TryParse(values[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new FormatException("The runtime default UTF-16 code-unit sequence is invalid.");
            characters[index] = (char)value;
        }
        return new string(characters);
    }
}
