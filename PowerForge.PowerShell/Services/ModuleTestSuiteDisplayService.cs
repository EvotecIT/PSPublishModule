using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleTestSuiteDisplayService
{
    private readonly ModuleTestFailureDisplayService _failureDisplayService;

    public ModuleTestSuiteDisplayService(ModuleTestFailureDisplayService? failureDisplayService = null)
    {
        _failureDisplayService = failureDisplayService ?? new ModuleTestFailureDisplayService();
    }

    public IReadOnlyList<ModuleTestSuiteDisplayLine> CreateFailureSummary(ModuleTestFailureAnalysis? analysis, bool detailed)
    {
        if (analysis is null)
        {
            return new[]
            {
                Line("No failure analysis available.", ConsoleColor.Yellow)
            };
        }

        var sourceLines = detailed
            ? _failureDisplayService.CreateDetailed(analysis)
            : _failureDisplayService.CreateSummary(analysis, showSuccessful: true);

        return sourceLines
            .Select(line => new ModuleTestSuiteDisplayLine
            {
                Text = line.Text,
                Color = line.Color
            })
            .ToArray();
    }

    private static ModuleTestSuiteDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };
}
