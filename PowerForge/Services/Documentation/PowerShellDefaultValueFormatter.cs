using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Formats tagged PowerShell runtime values as stable, XML-safe PowerShell expressions.
/// </summary>
internal static class PowerShellDefaultValueFormatter
{
    private static readonly Regex SafeTypeLiteralName = new(
        @"^[A-Za-z_][A-Za-z0-9_.+`]*(?:\[[A-Za-z0-9_.+`,\[\]]+\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            case "switchparameter":
                return "[System.Management.Automation.SwitchParameter]::new(" +
                       (string.Equals(value.Text, "True", StringComparison.OrdinalIgnoreCase) ? "$true" : "$false") + ")";
            case "enum":
                return FormatEnum(value);
            case "type":
                return string.IsNullOrEmpty(value.CanonicalTypeName)
                    ? string.Empty
                    : PowerShellRuntimeTypeFormatter.Format(
                        value.CanonicalTypeName!,
                        DecodeUtf16CodeUnits(value.Text),
                        DecodeUtf16CodeUnits(value.AssemblyNameCodeUnits),
                        value.RuntimeTypeShape);
            case "doublebits":
                return FormatDoubleBits(value.Text);
            case "double":
                return FormatFloatingPoint(value.Text, "double");
            case "singlebits":
                return FormatSingleBits(value.Text);
            case "single":
                return FormatFloatingPoint(value.Text, "single");
            case "decimalbits":
                return FormatDecimalBits(value.Text);
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
                return FormatFormattable(value);
            case "text":
                return string.Empty;
            case "textcodeunits":
                return FormatFallbackText(DecodeUtf16CodeUnits(value.Text));
            default:
                return string.Empty;
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

    /// <summary>
    /// Preserves authored display text while replacing only XML-invalid UTF-16
    /// code units with readable markers.
    /// </summary>
    internal static string FormatDisplayText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) &&
                index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                builder.Append(character);
                builder.Append(text[++index]);
            }
            else if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
            else
            {
                builder.Append("([char]").Append((int)character).Append(')');
            }
        }
        return builder.ToString();
    }

    private static string FormatEnum(DocumentationRuntimeValue value)
    {
        var typeName = value.CanonicalTypeName ?? string.Empty;
        if (typeName.Length == 0) return value.Text ?? string.Empty;
        var typeIsLiteral = SafeTypeLiteralName.IsMatch(typeName);
        if (typeIsLiteral && !string.IsNullOrWhiteSpace(value.Name))
            return "[" + typeName + "]::" + value.Name!.Trim();
        var typeExpression = PowerShellRuntimeTypeFormatter.Format(
            typeName,
            DecodeUtf16CodeUnits(value.RuntimeTypeNameCodeUnits),
            DecodeUtf16CodeUnits(value.AssemblyNameCodeUnits));
        if (typeExpression.Length == 0) return string.Empty;
        if (!typeIsLiteral) typeExpression = "(" + typeExpression + ")";
        var numericValue = value.Text ?? string.Empty;
        var underlyingTypeName = (value.UnderlyingTypeName ?? string.Empty).Trim();
        if (underlyingTypeName.Length > 0)
            numericValue = "([" + underlyingTypeName + "]" + numericValue + ")";
        return "[System.Enum]::ToObject(" + typeExpression + ", " + numericValue + ")";
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
        if (value.Equals("-0", StringComparison.Ordinal)) value = "-0.0";
        else if (value.Equals("0", StringComparison.Ordinal)) value = "0.0";
        return "([double]" + value + ")";
    }

    private static string FormatFormattable(DocumentationRuntimeValue value)
    {
        var text = value.Text ?? string.Empty;
        switch ((value.CanonicalTypeName ?? string.Empty).Trim())
        {
            case "System.SByte": return "([System.SByte]" + text + ")";
            case "System.Byte": return "([System.Byte]" + text + ")";
            case "System.Int16": return "([System.Int16]" + text + ")";
            case "System.UInt16": return "([System.UInt16]" + text + ")";
            case "System.Int32": return text;
            case "System.UInt32": return "([System.UInt32]" + text + ")";
            case "System.Int64": return "([System.Int64]" + text + ")";
            case "System.UInt64": return "([System.UInt64]" + text + ")";
            case "System.IntPtr":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedPointer) &&
                       signedPointer >= int.MinValue && signedPointer <= int.MaxValue
                    ? "[System.IntPtr]::new(([System.Int64]" + text + "))"
                    : string.Empty;
            case "System.UIntPtr":
                return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedPointer) &&
                       unsignedPointer <= uint.MaxValue
                    ? "[System.UIntPtr]::new(([System.UInt64]" + text + "))"
                    : string.Empty;
            default: return string.Empty;
        }
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

    private static string FormatDoubleBits(string? text)
    {
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
            throw new FormatException("The runtime default Double bit pattern is invalid.");
        return "[System.BitConverter]::Int64BitsToDouble(([long]" + bits.ToString(CultureInfo.InvariantCulture) + "))";
    }

    private static string FormatSingleBits(string? text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
            throw new FormatException("The runtime default Single bit pattern is invalid.");
        return "[System.BitConverter]::ToSingle([System.BitConverter]::GetBytes(([int]" +
               bits.ToString(CultureInfo.InvariantCulture) + ")), 0)";
    }

    private static string FormatDecimalBits(string? text)
    {
        var bits = (text ?? string.Empty).Split(',')
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new FormatException("The runtime default Decimal bit pattern is invalid."))
            .ToArray();
        if (bits.Length != 4)
            throw new FormatException("The runtime default Decimal bit pattern is invalid.");
        var isNegative = bits[3] < 0 ? "$true" : "$false";
        var scale = (bits[3] >> 16) & 0xFF;
        return "[System.Decimal]::new(([int]" + bits[0].ToString(CultureInfo.InvariantCulture) +
               "), ([int]" + bits[1].ToString(CultureInfo.InvariantCulture) +
               "), ([int]" + bits[2].ToString(CultureInfo.InvariantCulture) +
               "), " + isNegative + ", ([byte]" + scale.ToString(CultureInfo.InvariantCulture) + "))";
    }

    private static string FormatFallbackText(string text)
        => NeedsEncoding(text)
            ? FormatString(text, preserveCharacterType: false)
            : text;

    internal static string FormatString(string text, bool preserveCharacterType)
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
                frames.Push(new CollectionTokenFrame(
                    token.CanonicalTypeName,
                    token.Name,
                    token.ElementTypeName,
                    DecodeUtf16CodeUnits(token.RuntimeTypeNameCodeUnits),
                    DecodeUtf16CodeUnits(token.AssemblyNameCodeUnits),
                    token.RuntimeTypeShape));
                continue;
            }

            if (kind.Equals("CollectionEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is CollectionTokenFrame))
                    throw new FormatException("The runtime default token stream contains an unexpected collection terminator.");
                Append(frames.Pop().Complete(), isContainer: true);
                continue;
            }

            if (kind.Equals("DictionaryStart", StringComparison.OrdinalIgnoreCase))
            {
                frames.Push(new DictionaryTokenFrame(
                    token.CanonicalTypeName,
                    token.Name,
                    DecodeUtf16CodeUnits(token.RuntimeTypeNameCodeUnits),
                    DecodeUtf16CodeUnits(token.AssemblyNameCodeUnits),
                    token.RuntimeTypeShape));
                continue;
            }

            if (kind.Equals("ArrayStart", StringComparison.OrdinalIgnoreCase))
            {
                frames.Push(new ArrayTokenFrame(
                    token.CanonicalTypeName,
                    token.Text,
                    token.Name,
                    DecodeUtf16CodeUnits(token.RuntimeTypeNameCodeUnits),
                    DecodeUtf16CodeUnits(token.AssemblyNameCodeUnits),
                    token.RuntimeTypeShape));
                continue;
            }

            if (kind.Equals("ArrayEnd", StringComparison.OrdinalIgnoreCase))
            {
                if (frames.Count == 0 || !(frames.Peek() is ArrayTokenFrame))
                    throw new FormatException("The runtime default token stream contains an unexpected array terminator.");
                Append(frames.Pop().Complete(), isContainer: true);
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
                Append(frames.Pop().Complete(), isContainer: true);
                continue;
            }

            Append(Format(token));
        }

        if (frames.Count > 0)
            throw new FormatException("The runtime default token stream is missing a container terminator.");
        return result ?? throw new FormatException("The runtime default token stream is empty.");

        void Append(string value, bool isContainer = false)
        {
            if (frames.Count > 0)
            {
                frames.Peek().Add(value, isContainer);
                return;
            }

            if (result is not null)
                throw new FormatException("The runtime default token stream contains trailing values.");
            result = value;
        }
    }

    private abstract class TokenFrame
    {
        public abstract void Add(string value, bool isContainer);
        public abstract string Complete();
    }

    private sealed class CollectionTokenFrame : TokenFrame
    {
        private readonly string _collectionTypeName;
        private readonly string _elementTypeName;
        private readonly string _runtimeTypeName;
        private readonly string _assemblyName;
        private readonly string? _runtimeTypeShape;
        private readonly bool _isArray;
        private readonly List<string> _items = new();

        public CollectionTokenFrame(
            string? collectionTypeName,
            string? collectionKind,
            string? elementTypeName,
            string runtimeTypeName,
            string assemblyName,
            string? runtimeTypeShape)
        {
            if (string.IsNullOrWhiteSpace(collectionTypeName))
                throw new FormatException("The runtime default token stream is missing a constructible collection type.");
            _collectionTypeName = collectionTypeName!.Trim();
            _isArray = string.Equals(collectionKind, "Array", StringComparison.Ordinal);
            _elementTypeName = elementTypeName ??
                               (_isArray && _collectionTypeName.EndsWith("[]", StringComparison.Ordinal)
                                   ? _collectionTypeName.Substring(0, _collectionTypeName.Length - 2)
                                   : string.Empty);
            _runtimeTypeName = runtimeTypeName;
            _assemblyName = assemblyName;
            _runtimeTypeShape = runtimeTypeShape;
            if (!_isArray && !string.Equals(collectionKind, "List", StringComparison.Ordinal))
                throw new FormatException("The runtime default token stream contains an unsupported collection kind.");
        }

        public override void Add(string value, bool isContainer)
        {
            _items.Add(value);
        }

        public override string Complete()
        {
            var statements = new List<string>();
            if (_isArray)
            {
                if (SafeTypeLiteralName.IsMatch(_elementTypeName))
                {
                    statements.Add("$collection = [" + _collectionTypeName + "]::new(" +
                                   _items.Count.ToString(CultureInfo.InvariantCulture) + ")");
                }
                else
                {
                    var elementExpression = PowerShellRuntimeTypeFormatter.Format(
                        _elementTypeName,
                        _runtimeTypeName,
                        _assemblyName,
                        _runtimeTypeShape);
                    if (elementExpression.Length == 0)
                        throw new FormatException("The runtime default token stream contains an unresolved array element type.");
                    statements.Add("$collection = [System.Array]::CreateInstance((" + elementExpression + "), " +
                                   _items.Count.ToString(CultureInfo.InvariantCulture) + ")");
                }
                for (var index = 0; index < _items.Count; index++)
                    statements.Add("$collection.SetValue((" + _items[index] + "), " +
                                   index.ToString(CultureInfo.InvariantCulture) + ")");
            }
            else
            {
                var collectionTypeExpression = PowerShellRuntimeTypeFormatter.Format(
                    _collectionTypeName,
                    _runtimeTypeName,
                    _assemblyName,
                    _runtimeTypeShape);
                if (collectionTypeExpression.Length == 0)
                    throw new FormatException("The runtime default token stream contains an unresolved collection type.");
                statements.Add(SafeTypeLiteralName.IsMatch(_collectionTypeName)
                    ? "$collection = " + collectionTypeExpression + "::new()"
                    : "$collection = [System.Activator]::CreateInstance((" + collectionTypeExpression + "))");
                statements.AddRange(_items.Select(item =>
                    "[void]([System.Collections.IList]$collection).Add((" + item + "))"));
            }
            statements.Add("return ,$collection");
            return "& { " + string.Join("; ", statements) + " }";
        }
    }

    private sealed class DictionaryTokenFrame : TokenFrame
    {
        private readonly string _dictionaryTypeName;
        private readonly string _runtimeTypeName;
        private readonly string _assemblyName;
        private readonly string? _runtimeTypeShape;
        private readonly List<KeyValuePair<string, string>> _entries = new();
        private string? _key;
        private string? _value;
        private bool _entryOpen;

        private readonly string _constructorArgument;

        public DictionaryTokenFrame(
            string? dictionaryTypeName,
            string? comparerName,
            string runtimeTypeName,
            string assemblyName,
            string? runtimeTypeShape)
        {
            if (string.IsNullOrWhiteSpace(dictionaryTypeName))
                throw new FormatException("The runtime default token stream is missing a constructible dictionary type.");
            _dictionaryTypeName = dictionaryTypeName!.Trim();
            _constructorArgument = FormatDictionaryComparer(comparerName);
            _runtimeTypeName = runtimeTypeName;
            _assemblyName = assemblyName;
            _runtimeTypeShape = runtimeTypeShape;
        }

        public void BeginEntry()
        {
            if (_entryOpen)
                throw new FormatException("The runtime default token stream contains nested dictionary entry markers.");
            _entryOpen = true;
            _key = null;
            _value = null;
        }

        public override void Add(string value, bool isContainer)
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
            var dictionaryTypeExpression = PowerShellRuntimeTypeFormatter.Format(
                _dictionaryTypeName,
                _runtimeTypeName,
                _assemblyName,
                _runtimeTypeShape);
            if (dictionaryTypeExpression.Length == 0)
                throw new FormatException("The runtime default token stream contains an unresolved dictionary type.");
            var constructorExpression = SafeTypeLiteralName.IsMatch(_dictionaryTypeName)
                ? dictionaryTypeExpression + "::new(" + _constructorArgument + ")"
                : _constructorArgument.Length == 0
                    ? "[System.Activator]::CreateInstance((" + dictionaryTypeExpression + "))"
                    : "[System.Activator]::CreateInstance((" + dictionaryTypeExpression +
                      "), [object[]]@((" + _constructorArgument + ")))";
            var statements = new List<string> { "$dictionary = " + constructorExpression };
            statements.AddRange(_entries.Select(entry =>
                "([System.Collections.IDictionary]$dictionary).Add((" + entry.Key + "), (" + entry.Value + "))"));
            statements.Add("return ,$dictionary");
            return "& { " + string.Join("; ", statements) + " }";
        }

        private static string FormatDictionaryComparer(string? comparerName)
        {
            if (string.IsNullOrWhiteSpace(comparerName)) return string.Empty;
            if (comparerName!.StartsWith("Culture|", StringComparison.Ordinal))
            {
                var parts = comparerName.Split('|');
                if (parts.Length != 3 || !bool.TryParse(parts[2], out var ignoreCase))
                    throw new FormatException("The runtime default token stream contains invalid culture comparer metadata.");
                return "[System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('" +
                       parts[1].Replace("'", "''") + "'), " + (ignoreCase ? "$true" : "$false") + ")";
            }
            switch (comparerName!.Trim())
            {
                case "Ordinal":
                case "OrdinalIgnoreCase":
                case "InvariantCulture":
                case "InvariantCultureIgnoreCase":
                    return "[System.StringComparer]::" + comparerName.Trim();
                default:
                    throw new FormatException("The runtime default token stream contains an unsupported dictionary comparer.");
            }
        }
    }

    private sealed class ArrayTokenFrame : TokenFrame
    {
        private readonly string _elementTypeName;
        private readonly string _runtimeTypeName;
        private readonly string _assemblyName;
        private readonly string? _runtimeTypeShape;
        private readonly int[] _lengths;
        private readonly int[] _lowerBounds;
        private readonly List<string> _items = new();

        public ArrayTokenFrame(
            string? elementTypeName,
            string? lengths,
            string? lowerBounds,
            string runtimeTypeName,
            string assemblyName,
            string? runtimeTypeShape)
        {
            _elementTypeName = elementTypeName ?? string.Empty;
            _runtimeTypeName = runtimeTypeName;
            _assemblyName = assemblyName;
            _runtimeTypeShape = runtimeTypeShape;
            _lengths = ParseDimensions(lengths);
            _lowerBounds = ParseDimensions(lowerBounds);
            if (string.IsNullOrEmpty(_elementTypeName) ||
                _lengths.Length < 1 ||
                _lengths.Length != _lowerBounds.Length ||
                _lengths.Any(length => length < 0))
            {
                throw new FormatException("The runtime default token stream contains invalid multidimensional array metadata.");
            }
        }

        public override void Add(string value, bool isContainer) => _items.Add(value);

        public override string Complete()
        {
            long expectedCount = 1;
            foreach (var length in _lengths)
                expectedCount = checked(expectedCount * length);
            if (expectedCount != _items.Count)
                throw new FormatException("The runtime default token stream contains the wrong number of array elements.");

            var elementExpression = PowerShellRuntimeTypeFormatter.Format(
                _elementTypeName,
                _runtimeTypeName,
                _assemblyName,
                _runtimeTypeShape);
            if (elementExpression.Length == 0)
                throw new FormatException("The runtime default token stream contains an unresolved array element type.");
            if (elementExpression.StartsWith("& {", StringComparison.Ordinal))
                elementExpression = "(" + elementExpression + ")";
            var statements = new List<string>
            {
                "$array = [System.Array]::CreateInstance(" + elementExpression + ", [int[]]@(" +
                FormatIntegers(_lengths) + "), [int[]]@(" + FormatIntegers(_lowerBounds) + "))"
            };
            var indices = (int[])_lowerBounds.Clone();
            foreach (var item in _items)
            {
                statements.Add("$array.SetValue((" + item + "), [int[]]@(" + FormatIntegers(indices) + "))");
                IncrementIndices(indices);
            }
            statements.Add("return ,$array");
            return "& { " + string.Join("; ", statements) + " }";
        }

        private void IncrementIndices(int[] indices)
        {
            for (var dimension = indices.Length - 1; dimension >= 0; dimension--)
            {
                var offset = (long)indices[dimension] - _lowerBounds[dimension];
                if (offset + 1L < _lengths[dimension])
                {
                    indices[dimension]++;
                    return;
                }
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

        private static string FormatIntegers(IEnumerable<int> values)
            => string.Join(", ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
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
