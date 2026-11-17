using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ConsoleMonster.Capabilities;
using PowerForge;

namespace PSPublishModule;

// Only finalized render inputs cross the process boundary. Planner requests and tokens do not.
internal sealed class DocumentationRenderRequest
{
    public DocumentItem[] Items { get; set; } = Array.Empty<DocumentItem>();
    public ModuleInfoModel? Module { get; set; }
    public DocumentationCommandHelpItem[] CommandHelp { get; set; } = Array.Empty<DocumentationCommandHelpItem>();
    public string? DestinationPath { get; set; }
    public JsonRendererPreference JsonPreference { get; set; }
    public string? DefaultLanguage { get; set; }
    // The parent's console profile, so the worker lays out and colours output for the console it is shown on.
    public int ConsoleWidth { get; set; } = 80;
    public int ConsoleHeight { get; set; } = 25;
    public ColorDepth ColorDepth { get; set; } = ColorDepth.None;
    public GlyphTier Glyphs { get; set; } = GlyphTier.Ascii;
}

internal sealed class DocumentationRenderResponse
{
    public string? HtmlPath { get; set; }
    public List<string> Logs { get; set; } = new();
    public string? Error { get; set; }

    // Set by the parent from the worker's standard output; never part of the response file.
    [JsonIgnore]
    public string Output { get; set; } = string.Empty;
}

// Populate preserves get-only dependency children and release asset lists in PowerForge models.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate)]
[JsonSerializable(typeof(DocumentationRenderRequest))]
[JsonSerializable(typeof(DocumentationRenderResponse))]
internal partial class DocumentationRenderJsonContext : JsonSerializerContext
{
}
