using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PowerForge.Generators.Cmdlets;
using PowerForge.Generators.Fluent;
using PowerForge.Generators.Vocabulary;
using Xunit;

namespace PowerForge.Generators.Tests;

/// <summary>
/// Compiles a module with a [FluentCmdlet] cmdlet, imports it into a PowerShell runspace and calls it as a script would.
/// </summary>
public sealed class FluentCmdletGeneratorTests {
    private const string Module = """
        using System.Collections.Generic;
        using System.Management.Automation;
        using PowerForge.Fluent;

        namespace Sample {
            public sealed class Chart {
                /// <summary>The chart title.</summary>
                public string? Title { get; set; }
                public bool IsVisible { get; set; }
                public int Width { get; set; }
                public Legend Legend { get; set; } = new();
                public List<string> Tags { get; } = new();
                public string Id { get; set; } = "";
                public Tone Tone { get; set; }
                public string? Secret { get; set; }
            }

            public sealed class Legend {
                public string? Position { get; set; }
                public Font? Font { get; set; }
            }

            public sealed class Font {
                public int Size { get; set; }
            }

            [FluentCmdlet(typeof(Chart), Exclude = new[] { "Secret" })]
            [Cmdlet(VerbsCommon.New, "SampleChart")]
            public sealed partial class NewSampleChartCommand : PSCmdlet {
                // Declared here, so the generated parameters step aside; ApplyParameters still copies them.
                [Parameter(Mandatory = true, Position = 0)]
                public string Id { get; set; } = "";

                [Parameter]
                [ValidateRange(1, 500)]
                public int Width { get; set; }

                protected override void ProcessRecord() {
                    Chart chart = new Chart { Id = Id };
                    ApplyParameters(chart);
                    WriteObject(chart);
                }
            }
        }
        """;

    [Fact]
    public void ScriptsSetSwitchesHashtablesAndArraysAndOnlyBoundParametersApply() {
        Assembly module = Compile(Module);
        using PowerShell powerShell = PowerShell.Create();
        powerShell.AddCommand("Import-Module").AddParameter("Assembly", module).Invoke();
        powerShell.Commands.Clear();
        powerShell.AddScript("New-SampleChart 'orders' -Title 'Orders' -Visible -Legend @{ Position = 'Bottom'; Font = @{ Size = '12' } } -Tags a, b -Tone Warning -Width 300");

        dynamic chart = powerShell.Invoke().Single().BaseObject;

        Assert.Empty(powerShell.Streams.Error);
        Assert.Equal("orders", (string)chart.Id);
        Assert.Equal("Orders", (string)chart.Title);
        Assert.True((bool)chart.IsVisible);
        Assert.Equal(300, (int)chart.Width);
        Assert.Equal("Bottom", (string)chart.Legend.Position);
        Assert.Equal(12, (int)chart.Legend.Font.Size);
        Assert.Equal(new[] { "a", "b" }, ((IEnumerable<string>)chart.Tags).ToArray());
        Assert.Equal("warning", chart.Tone.ToString());
    }

    [Fact]
    public void DeclaredParametersKeepTheirValidationAndExcludedPropertiesAreNotParameters() {
        Assembly module = Compile(Module);
        using PowerShell powerShell = PowerShell.Create();
        powerShell.AddCommand("Import-Module").AddParameter("Assembly", module).Invoke();
        powerShell.Commands.Clear();
        powerShell.AddScript("(Get-Command New-SampleChart).Parameters.ContainsKey('Secret'); New-SampleChart 'orders' -Width 0");

        Collection<PSObject> output = powerShell.Invoke();

        Assert.False((bool)output[0].BaseObject);
        ErrorRecord error = Assert.Single(powerShell.Streams.Error);
        Assert.Equal("ParameterArgumentValidationError,Sample.NewSampleChartCommand", error.FullyQualifiedErrorId);
    }

    [Fact]
    public void UnknownHashtableKeysAreRejectedWithTheValidKeys() {
        Assembly module = Compile(Module);
        using PowerShell powerShell = PowerShell.Create();
        powerShell.AddCommand("Import-Module").AddParameter("Assembly", module).Invoke();
        powerShell.Commands.Clear();
        powerShell.AddScript("New-SampleChart 'orders' -Legend @{ Placement = 'Bottom' }");

        powerShell.Invoke();

        ErrorRecord error = Assert.Single(powerShell.Streams.Error);
        Assert.Contains("-Legend has no key 'Placement'. Valid keys: Position, Font.", error.Exception.Message);
    }

    [Fact]
    public void NonPartialCmdletsReservedNamesAndUnknownListedPropertiesAreBuildErrors() {
        const string module = """
            using System.Management.Automation;
            using PowerForge.Fluent;

            namespace Sample {
                public sealed class Settings {
                    public bool Verbose { get; set; }
                }

                [FluentCmdlet(typeof(Settings))]
                [Cmdlet(VerbsCommon.Get, "Whole")]
                public sealed class GetWholeCommand : PSCmdlet {
                }

                [FluentCmdlet(typeof(Settings), Include = new[] { "Verbose", "Missing" })]
                [Cmdlet(VerbsCommon.Get, "Settings")]
                public sealed partial class GetSettingsCommand : PSCmdlet {
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = Run(module);

        Assert.Equal(new[] { "PFF006", "PFF007", "PFF009" }, diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    private static Assembly Compile(string source) {
        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = Run(source);
        Assert.Empty(diagnostics);
        return TestCompiler.Load(compilation);
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source) =>
        TestCompiler.Run(source, LanguageVersion.Latest, new Dictionary<string, string> { ["PowerForgeVocabulary"] = "Tone", ["PowerForgeVocabularyNamespace"] = "Sample" },
            new FluentGenerator(), new FluentCmdletGenerator(), new VocabularyGenerator());
}
