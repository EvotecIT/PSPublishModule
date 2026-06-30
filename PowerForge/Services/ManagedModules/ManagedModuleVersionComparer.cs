namespace PowerForge;

internal sealed class ManagedModuleVersionComparer : IComparer<string>
{
    public static readonly ManagedModuleVersionComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var left = ManagedModuleVersionParts.Parse(x);
        var right = ManagedModuleVersionParts.Parse(y);
        return left.CompareTo(right);
    }

    internal static bool IsPrerelease(string version)
        => !string.IsNullOrWhiteSpace(version) && version.IndexOf('-') >= 0;

    private sealed class ManagedModuleVersionParts : IComparable<ManagedModuleVersionParts>
    {
        private readonly int[] _numbers;
        private readonly string[] _prereleaseIdentifiers;

        private ManagedModuleVersionParts(int[] numbers, string[] prereleaseIdentifiers)
        {
            _numbers = numbers;
            _prereleaseIdentifiers = prereleaseIdentifiers;
        }

        public static ManagedModuleVersionParts Parse(string version)
        {
            var trimmed = version.Trim();
            var plusIndex = trimmed.IndexOf('+');
            if (plusIndex >= 0)
                trimmed = trimmed.Substring(0, plusIndex);

            var prereleaseIdentifiers = Array.Empty<string>();
            var dashIndex = trimmed.IndexOf('-');
            if (dashIndex >= 0)
            {
                prereleaseIdentifiers = trimmed.Substring(dashIndex + 1)
                    .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                trimmed = trimmed.Substring(0, dashIndex);
            }

            var numbers = trimmed
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => int.TryParse(part, out var number) ? number : 0)
                .ToArray();

            if (numbers.Length == 0)
                numbers = new[] { 0 };

            return new ManagedModuleVersionParts(numbers, prereleaseIdentifiers);
        }

        public int CompareTo(ManagedModuleVersionParts? other)
        {
            if (other is null)
                return 1;

            var length = Math.Max(_numbers.Length, other._numbers.Length);
            for (var index = 0; index < length; index++)
            {
                var left = index < _numbers.Length ? _numbers[index] : 0;
                var right = index < other._numbers.Length ? other._numbers[index] : 0;
                var numberComparison = left.CompareTo(right);
                if (numberComparison != 0)
                    return numberComparison;
            }

            if (_prereleaseIdentifiers.Length == 0 && other._prereleaseIdentifiers.Length == 0)
                return 0;
            if (_prereleaseIdentifiers.Length == 0)
                return 1;
            if (other._prereleaseIdentifiers.Length == 0)
                return -1;

            var prereleaseLength = Math.Min(_prereleaseIdentifiers.Length, other._prereleaseIdentifiers.Length);
            for (var index = 0; index < prereleaseLength; index++)
            {
                var prereleaseComparison = ComparePrereleaseIdentifier(
                    _prereleaseIdentifiers[index],
                    other._prereleaseIdentifiers[index]);
                if (prereleaseComparison != 0)
                    return prereleaseComparison;
            }

            return _prereleaseIdentifiers.Length.CompareTo(other._prereleaseIdentifiers.Length);
        }

        private static int ComparePrereleaseIdentifier(string left, string right)
        {
            var leftNumeric = IsNumericIdentifier(left);
            var rightNumeric = IsNumericIdentifier(right);
            if (leftNumeric && rightNumeric)
                return CompareNumericIdentifier(left, right);
            if (leftNumeric)
                return -1;
            if (rightNumeric)
                return 1;

            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static bool IsNumericIdentifier(string value)
            => value.Length > 0 && value.All(static character => character >= '0' && character <= '9');

        private static int CompareNumericIdentifier(string left, string right)
        {
            var normalizedLeft = left.TrimStart('0');
            var normalizedRight = right.TrimStart('0');
            if (normalizedLeft.Length == 0)
                normalizedLeft = "0";
            if (normalizedRight.Length == 0)
                normalizedRight = "0";

            var lengthComparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
            return lengthComparison != 0
                ? lengthComparison
                : StringComparer.Ordinal.Compare(normalizedLeft, normalizedRight);
        }
    }
}
