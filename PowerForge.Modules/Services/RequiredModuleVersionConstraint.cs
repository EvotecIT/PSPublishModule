namespace PowerForge;

internal static class RequiredModuleVersionConstraint
{
    internal static bool DoesVersionMatchRequiredModule(RequiredModuleReference requiredModule, string candidateVersion)
    {
        if (requiredModule is null || string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            return false;
        if (string.IsNullOrWhiteSpace(candidateVersion))
            return false;

        var hasConstraints =
            !string.IsNullOrWhiteSpace(requiredModule.ModuleVersion) ||
            !string.IsNullOrWhiteSpace(requiredModule.RequiredVersion) ||
            !string.IsNullOrWhiteSpace(requiredModule.MaximumVersion);

        if (!TryParseSemVer(candidateVersion, out var candidate))
        {
            if (!hasConstraints)
                return true;

            if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
                return string.Equals(candidateVersion.Trim(), requiredModule.RequiredVersion!.Trim(), StringComparison.OrdinalIgnoreCase);

            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
        {
            var requiredVersion = requiredModule.RequiredVersion!.Trim();
            if (!TryParseSemVer(requiredVersion, out var required))
                return string.Equals(candidateVersion.Trim(), requiredVersion, StringComparison.OrdinalIgnoreCase);

            return candidate.CompareTo(required) == 0;
        }

        if (!string.IsNullOrWhiteSpace(requiredModule.ModuleVersion))
        {
            if (!TryParseSemVer(requiredModule.ModuleVersion!, out var minimum))
                return false;

            if (candidate.CompareTo(minimum) < 0)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredModule.MaximumVersion))
        {
            if (!TryParseSemVer(requiredModule.MaximumVersion!, out var maximum))
                return false;

            if (candidate.CompareTo(maximum) > 0)
                return false;
        }

        return true;
    }

    internal static string FormatRequiredModuleConstraint(RequiredModuleReference requiredModule)
    {
        if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
            return $"RequiredVersion = {requiredModule.RequiredVersion}";
        if (!string.IsNullOrWhiteSpace(requiredModule.ModuleVersion) && !string.IsNullOrWhiteSpace(requiredModule.MaximumVersion))
            return $"ModuleVersion >= {requiredModule.ModuleVersion}, MaximumVersion <= {requiredModule.MaximumVersion}";
        if (!string.IsNullOrWhiteSpace(requiredModule.ModuleVersion))
            return $"ModuleVersion >= {requiredModule.ModuleVersion}";
        if (!string.IsNullOrWhiteSpace(requiredModule.MaximumVersion))
            return $"MaximumVersion <= {requiredModule.MaximumVersion}";
        return "Any version";
    }

    internal readonly struct SemVer : IComparable<SemVer>
    {
        public Version Version { get; }
        public string? PreRelease { get; }

        public SemVer(Version version, string? preRelease)
        {
            Version = version;
            PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
        }

        public int CompareTo(SemVer other)
        {
            var c = Version.CompareTo(other.Version);
            if (c != 0) return c;

            var a = PreRelease;
            var b = other.PreRelease;
            if (a is null && b is null) return 0;
            if (a is null) return 1; // stable > prerelease
            if (b is null) return -1;

            return ComparePreRelease(a, b);
        }

        private static int ComparePreRelease(string a, string b)
        {
            var aa = a.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var bb = b.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var len = Math.Max(aa.Length, bb.Length);
            for (var i = 0; i < len; i++)
            {
                if (i >= aa.Length) return -1;
                if (i >= bb.Length) return 1;

                var pa = aa[i];
                var pb = bb[i];

                var na = int.TryParse(pa, out var ia);
                var nb = int.TryParse(pb, out var ib);
                if (na && nb)
                {
                    var c = ia.CompareTo(ib);
                    if (c != 0) return c;
                }
                else
                {
                    var c = StringComparer.OrdinalIgnoreCase.Compare(pa, pb);
                    if (c != 0) return c;
                }
            }
            return 0;
        }
    }

    internal static bool TryParseSemVer(string text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim();
        var dash = t.IndexOf('-');
        var main = dash >= 0 ? t.Substring(0, dash) : t;
        var pre = dash >= 0 ? t.Substring(dash + 1) : null;
        if (!Version.TryParse(main, out var v)) return false;
        version = new SemVer(v, pre);
        return true;
    }

    internal static string FormatSemVer(string moduleVersion, string? preRelease)
        => string.IsNullOrWhiteSpace(preRelease) ? moduleVersion : moduleVersion + "-" + preRelease;
}
