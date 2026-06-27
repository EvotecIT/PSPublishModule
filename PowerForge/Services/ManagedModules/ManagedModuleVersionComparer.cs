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
        private readonly string? _prerelease;

        private ManagedModuleVersionParts(int[] numbers, string? prerelease)
        {
            _numbers = numbers;
            _prerelease = prerelease;
        }

        public static ManagedModuleVersionParts Parse(string version)
        {
            var trimmed = version.Trim();
            var plusIndex = trimmed.IndexOf('+');
            if (plusIndex >= 0)
                trimmed = trimmed.Substring(0, plusIndex);

            string? prerelease = null;
            var dashIndex = trimmed.IndexOf('-');
            if (dashIndex >= 0)
            {
                prerelease = trimmed.Substring(dashIndex + 1);
                trimmed = trimmed.Substring(0, dashIndex);
            }

            var numbers = trimmed
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => int.TryParse(part, out var number) ? number : 0)
                .ToArray();

            if (numbers.Length == 0)
                numbers = new[] { 0 };

            return new ManagedModuleVersionParts(numbers, string.IsNullOrWhiteSpace(prerelease) ? null : prerelease);
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

            if (_prerelease is null && other._prerelease is null)
                return 0;
            if (_prerelease is null)
                return 1;
            if (other._prerelease is null)
                return -1;

            return StringComparer.OrdinalIgnoreCase.Compare(_prerelease, other._prerelease);
        }
    }
}
