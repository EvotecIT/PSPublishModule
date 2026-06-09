using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class DotNetRepositoryReleaseDisplayModel
{
    internal string Title { get; set; } = string.Empty;
    internal IReadOnlyList<DotNetRepositoryReleaseProjectDisplayRow> Projects { get; set; } =
        Array.Empty<DotNetRepositoryReleaseProjectDisplayRow>();
    internal IReadOnlyList<DotNetRepositoryReleaseTotalsDisplayRow> Totals { get; set; } =
        Array.Empty<DotNetRepositoryReleaseTotalsDisplayRow>();
}

internal sealed class DotNetRepositoryReleaseProjectDisplayRow
{
    internal string ProjectName { get; set; } = string.Empty;
    internal string Packable { get; set; } = string.Empty;
    internal string VersionDisplay { get; set; } = string.Empty;
    internal string PackageCount { get; set; } = string.Empty;
    internal string StatusText { get; set; } = string.Empty;
    internal ConsoleColor? StatusColor { get; set; }
    internal string ErrorPreview { get; set; } = string.Empty;
}

internal sealed class DotNetRepositoryReleaseTotalsDisplayRow
{
    internal string Label { get; set; } = string.Empty;
    internal string Value { get; set; } = string.Empty;
}
