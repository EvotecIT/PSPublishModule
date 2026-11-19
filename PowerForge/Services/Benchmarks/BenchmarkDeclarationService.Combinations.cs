using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class BenchmarkDeclarationService
{
    // Checks every case × library × axis-value combination, not only each dimension on its own: three rows can
    // cover both libraries, both scales and the case while the fourth combination never ran. The expected set is
    // the full matrix of declared cases and axis values for each declared library that ran, or the comparison's
    // explicit cells when not every combination is valid. A failed cell still counts as present. Libraries that did
    // not run at all are left out, because the run selects libraries from the policy; combinations of a case or
    // axis value already reported as missing or unexpected are not repeated.
    private static void CheckCombinations(
        List<BenchmarkDeclarationIssue> issues,
        string subject,
        BenchmarkComparisonDefinition comparison,
        IReadOnlyCollection<BenchmarkEvidenceCell> cells)
    {
        string[] cases = DeclaredCaseNames(comparison);
        BenchmarkExpectedCell[] explicitCells = comparison.Cells ?? Array.Empty<BenchmarkExpectedCell>();
        if (cases.Length == 0)
            return;

        BenchmarkComparisonAxis[] axes = (comparison.Axes ?? Array.Empty<BenchmarkComparisonAxis>())
            .Where(axis => axis is not null && !string.IsNullOrWhiteSpace(axis.Name))
            .ToArray();
        string[] declaredLibraries = comparison.Libraries ?? Array.Empty<string>();
        string[] ranLibraries = cells
            .Select(cell => cell.Library)
            .Where(library => !string.IsNullOrWhiteSpace(library) && declaredLibraries.Contains(library, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(library => library, StringComparer.Ordinal)
            .ToArray();
        if (ranLibraries.Length == 0)
            return;

        bool withCase = cases.Length > 0;
        var measuredCases = new HashSet<string>(cells.Select(cell => cell.Case ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        bool Declared(string? caseName) => !withCase || cases.Contains(caseName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        bool Ran(string caseName) => !withCase || measuredCases.Contains(caseName);
        bool AxisValueRan(BenchmarkComparisonAxis axis, string value)
            => cells.Any(cell => cell.Parameters.TryGetValue(axis.Name, out string? measured) &&
                                 string.Equals(Normalize(measured), value, StringComparison.OrdinalIgnoreCase));
        bool AxisValueDeclared(BenchmarkComparisonAxis axis, string value)
            => DeclaredValues(axis).Contains(value, StringComparer.OrdinalIgnoreCase);

        // Expected combinations, as display text keyed by a case-insensitive identity.
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Expect(string caseName, string library, IReadOnlyList<string> values)
        {
            if (!Ran(caseName) || axes.Where((axis, index) => !AxisValueRan(axis, values[index])).Any())
                return;
            expected[Key(caseName, library, values)] = Describe(withCase ? caseName : null, library, axes, values);
        }

        if (explicitCells.Length > 0)
        {
            foreach (BenchmarkExpectedCell cell in explicitCells.Where(cell => cell is not null))
            {
                string[] values = axes
                    .Select(axis => cell.Parameters is not null && TryGetIgnoreCase(cell.Parameters, axis.Name, out string value) ? value : string.Empty)
                    .ToArray();
                string[] libraries = string.IsNullOrWhiteSpace(cell.Library)
                    ? ranLibraries
                    : ranLibraries.Where(library => string.Equals(library, (cell.Library ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (string library in libraries)
                    Expect(withCase ? (cell.Case ?? string.Empty).Trim() : string.Empty, library, values);
            }
        }
        else
        {
            IEnumerable<string[]> combinations = new[] { Array.Empty<string>() };
            foreach (BenchmarkComparisonAxis axis in axes)
            {
                string[] axisValues = DeclaredValues(axis);
                combinations = combinations.SelectMany(prefix => axisValues.Select(value => prefix.Concat(new[] { value }).ToArray())).ToArray();
            }
            foreach (string caseName in cases)
                foreach (string library in ranLibraries)
                    foreach (string[] values in combinations)
                        Expect(caseName, library, values);
        }

        // Measured combinations whose case, library and axis values are all declared; anything else is already
        // reported by the per-dimension checks.
        var measured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkEvidenceCell cell in cells)
        {
            if (!Declared(cell.Case) || !ranLibraries.Contains(cell.Library, StringComparer.OrdinalIgnoreCase))
                continue;
            var values = new string[axes.Length];
            bool complete = true;
            for (int index = 0; index < axes.Length && complete; index++)
            {
                complete = cell.Parameters.TryGetValue(axes[index].Name, out string? value) && value is not null && AxisValueDeclared(axes[index], Normalize(value));
                values[index] = complete ? Normalize(value) : string.Empty;
            }
            if (!complete)
                continue;
            string caseName = withCase ? cell.Case ?? string.Empty : string.Empty;
            measured[Key(caseName, cell.Library, values)] = Describe(withCase ? caseName : null, cell.Library, axes, values);
        }

        foreach (KeyValuePair<string, string> missing in expected.Where(pair => !measured.ContainsKey(pair.Key)).OrderBy(pair => pair.Value, StringComparer.Ordinal))
            Add(issues, subject, "missing-combination", $"Declared combination {missing.Value} did not run.");
        foreach (KeyValuePair<string, string> unexpected in measured.Where(pair => !expected.ContainsKey(pair.Key)).OrderBy(pair => pair.Value, StringComparer.Ordinal))
            Add(issues, subject, "unexpected-combination", $"Combination {unexpected.Value} ran but is not declared.");
    }

    // Declared case names: the comparison's cases, or, when it lists only cells, the cases those cells name.
    internal static string[] DeclaredCaseNames(BenchmarkComparisonDefinition comparison)
    {
        string[] cases = (comparison.Cases ?? Array.Empty<string>()).Select(Normalize).Where(value => value.Length > 0).ToArray();
        return cases.Length > 0
            ? cases
            : (comparison.Cells ?? Array.Empty<BenchmarkExpectedCell>())
                .Where(cell => cell is not null)
                .Select(cell => Normalize(cell.Case))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    // Explicit cells that apply to a library that ran: cells for every library, or for a declared library that ran.
    private static BenchmarkExpectedCell[] ApplicableCells(BenchmarkComparisonDefinition comparison, IReadOnlyCollection<BenchmarkEvidenceCell> cells)
    {
        string[] declaredLibraries = comparison.Libraries ?? Array.Empty<string>();
        var ran = new HashSet<string>(
            cells.Select(cell => cell.Library).Where(library => !string.IsNullOrWhiteSpace(library) && declaredLibraries.Contains(library, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        return (comparison.Cells ?? Array.Empty<BenchmarkExpectedCell>())
            .Where(cell => cell is not null && (string.IsNullOrWhiteSpace(cell.Library) ? ran.Count > 0 : ran.Contains(Normalize(cell.Library))))
            .ToArray();
    }

    // Cases that must have run: every declared case, or with explicit cells the cases of the cells that apply.
    private static string[] RequiredCaseNames(BenchmarkComparisonDefinition comparison, IReadOnlyCollection<BenchmarkEvidenceCell> cells, string[] declaredCases)
        => (comparison.Cells ?? Array.Empty<BenchmarkExpectedCell>()).Length == 0
            ? declaredCases
            : ApplicableCells(comparison, cells).Select(cell => Normalize(cell.Case)).Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Axis values that must have run: every declared value, or with explicit cells the values of the cells that apply.
    private static string[] RequiredAxisValues(BenchmarkComparisonDefinition comparison, BenchmarkComparisonAxis axis, IReadOnlyCollection<BenchmarkEvidenceCell> cells, string[] declaredValues)
        => (comparison.Cells ?? Array.Empty<BenchmarkExpectedCell>()).Length == 0
            ? declaredValues
            : ApplicableCells(comparison, cells)
                .Select(cell => cell.Parameters is not null && TryGetIgnoreCase(cell.Parameters, axis.Name, out string value) ? value : string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    // Checks that explicit cells name declared cases, libraries and axis values, with one value for every axis.
    private static void ValidateExplicitCells(List<BenchmarkDeclarationIssue> issues, string subject, BenchmarkComparisonDefinition comparison)
    {
        string[] cases = (comparison.Cases ?? Array.Empty<string>()).Select(Normalize).Where(value => value.Length > 0).ToArray();
        string[] libraries = comparison.Libraries ?? Array.Empty<string>();
        BenchmarkComparisonAxis[] axes = (comparison.Axes ?? Array.Empty<BenchmarkComparisonAxis>())
            .Where(axis => axis is not null && !string.IsNullOrWhiteSpace(axis.Name))
            .ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkExpectedCell cell in comparison.Cells ?? Array.Empty<BenchmarkExpectedCell>())
        {
            if (cell is null)
                continue;
            string caseName = (cell.Case ?? string.Empty).Trim();
            if (caseName.Length == 0)
                Add(issues, subject, "comparison-cell", "Every cell needs a case.");
            else if (cases.Length > 0 && !cases.Contains(caseName, StringComparer.OrdinalIgnoreCase))
                Add(issues, subject, "comparison-cell", $"Cell case '{caseName}' is not one of the declared cases.");
            if (!string.IsNullOrWhiteSpace(cell.Library) && !libraries.Contains((cell.Library ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
                Add(issues, subject, "comparison-cell", $"Cell library '{cell.Library}' is not one of the comparison's libraries.");
            var parameters = cell.Parameters ?? new Dictionary<string, string>();
            foreach (string name in parameters.Keys.GroupBy(name => name.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key))
                Add(issues, subject, "comparison-cell", $"A cell for case '{caseName}' gives parameter '{name}' more than once; parameter names ignore case.");
            foreach (string name in parameters.Keys.Where(name => !axes.Any(axis => string.Equals(axis.Name, name, StringComparison.OrdinalIgnoreCase))))
                Add(issues, subject, "comparison-cell", $"Cell parameter '{name}' is not a declared axis.");
            var values = new List<string>();
            foreach (BenchmarkComparisonAxis axis in axes)
            {
                if (!TryGetIgnoreCase(parameters, axis.Name, out string value))
                    Add(issues, subject, "comparison-cell", $"A cell for case '{caseName}' needs a value for axis '{axis.Name}'.");
                else if (!DeclaredValues(axis).Contains(value, StringComparer.OrdinalIgnoreCase))
                    Add(issues, subject, "comparison-cell", $"Cell value '{value}' is not declared for axis '{axis.Name}'.");
                values.Add(value);
            }
            if (!seen.Add(Key(caseName, (cell.Library ?? string.Empty).Trim(), values)))
                Add(issues, subject, "comparison-cell", $"Cell {Describe(caseName, cell.Library, axes, values)} is declared more than once.");
        }
    }

    // Declared axis values, explicit cell values and measured values are compared trimmed, as catalogs store them.
    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string[] DeclaredValues(BenchmarkComparisonAxis axis)
        => (axis.Values ?? Array.Empty<string>()).Select(Normalize).ToArray();

    private static bool TryGetIgnoreCase(IReadOnlyDictionary<string, string> values, string name, out string value)
    {
        foreach (KeyValuePair<string, string> pair in values)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && pair.Value is not null)
            {
                value = pair.Value.Trim();
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static string Key(string caseName, string library, IReadOnlyList<string> values)
        => string.Join("\u001f", new[] { caseName, library }.Concat(values)).ToLowerInvariant();

    private static string Describe(string? caseName, string? library, IReadOnlyList<BenchmarkComparisonAxis> axes, IReadOnlyList<string> values)
    {
        var parts = new List<string>();
        if (caseName is not null)
            parts.Add($"case '{caseName}'");
        parts.Add(string.IsNullOrWhiteSpace(library) ? "every library" : $"library '{library}'");
        for (int index = 0; index < axes.Count; index++)
            parts.Add($"{axes[index].Name}={values[index]}");
        return string.Join(", ", parts);
    }
}
