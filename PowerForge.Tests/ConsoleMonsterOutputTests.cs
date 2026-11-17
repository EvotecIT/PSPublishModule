using System;
using System.IO;
using System.Runtime.CompilerServices;
using ConsoleMonster;
using ConsoleMonster.Capabilities;
using ConsoleMonster.Testing;
using PowerForge.ConsoleUi;
using PSPublishModule;

namespace PowerForge.Tests;

// Golden output of the static views that moved to ConsoleMonster, rendered through ConsoleOutput into a
// fixed 80-column recording target. Refresh with CONSOLEMONSTER_UPDATE_GOLDEN=1 after an intended change.
[Collection(ConsoleCapture.Collection)]
public sealed class ConsoleMonsterOutputTests
{
    [Theory]
    [InlineData(GlyphTier.Full)]
    [InlineData(GlyphTier.Ascii)]
    public void ManagedModuleUpdatePlan_RendersEscapedFieldsAndPolicyVerdicts(GlyphTier glyphs)
    {
        var plan = new ManagedModuleUpdatePlan
        {
            Name = "Contoso.Tools",
            TargetVersion = "2.1.0",
            PreviousVersion = "2.0.3",
            Action = ManagedModuleUpdatePlanAction.Update,
            RepositoryName = "Internal",
            RepositorySource = "https://packages.contoso.test/[v3]/index.json",
            ModulePath = @"C:\Program Files\PowerShell\Modules\Contoso.Tools\2.1.0",
            WouldWriteFiles = true,
            License = "MIT",
            LicenseAcceptanceRequired = true,
            SourcePolicySatisfied = false,
            SourcePolicyReason = "Installed copy came from PSGallery.",
            FamilyActions = new[]
            {
                new ManagedModuleFamilyUpdatePlanItem { Name = "Contoso.Tools.Core", Action = ManagedModuleFamilyUpdatePlanAction.Update, TargetVersion = "2.1.0" }
            }
        };

        var text = Render(glyphs, () => ManagedModuleSummaryWriter.Write(plan));

        GoldenFile.Match(GoldenPath($"ManagedModuleUpdatePlan.{glyphs}.txt"), text);
    }

    [Fact]
    public void ModuleStatePlan_MarksErrorFindingsAndReportsOverflow()
    {
        var actions = new ModuleStatePlanActionResult[21];
        for (var i = 0; i < actions.Length; i++)
            actions[i] = new ModuleStatePlanActionResult { Kind = "Install", ModuleName = $"Module{i:00}", VersionPolicy = "Latest", Reason = "Missing", TargetScope = "CurrentUser" };

        var plan = new ModuleStatePlanResult
        {
            InventoryPath = "inventory.json",
            DesiredStatePath = "desired.json",
            HasErrors = true,
            Actions = actions,
            Findings = new[]
            {
                new ModuleStateConflictFindingResult { Severity = "Error", Code = "DuplicateFamily", ModuleNames = new[] { "Az.Accounts" }, Scope = "AllUsers", Message = "Two versions of [Az] load first." },
                new ModuleStateConflictFindingResult { Severity = "Warning", Code = "Shadowed", ModuleNames = new[] { "Pester" }, Message = "A newer copy is shadowed." }
            }
        };

        var text = Render(GlyphTier.Full, () => ModuleStateConsoleRenderer.WritePlan(plan));

        GoldenFile.Match(GoldenPath("ModuleStatePlan.txt"), text);
    }

    [Fact]
    public void ModuleDocumentation_AppliesDefaultLanguageAndSystemJsonPreference()
    {
        const string markdown = """
            # Contoso.Tools

            Install with **PowerShellGet** and see [the docs](https://contoso.test/docs).

            ```
            Install-Module -Name Contoso.Tools -Scope CurrentUser # quick start
            ```

            ```json
            {"name":"Contoso.Tools","tags":["build","release"]}
            ```

            | Setting | Default |
            |---------|---------|
            | Scope   | `CurrentUser` |
            """;

        var text = Render(GlyphTier.Full, () => new Renderer(JsonRendererPreference.System, "powershell").ShowContent("README.md", markdown, raw: false));

        GoldenFile.Match(GoldenPath("ModuleDocumentation.txt"), text);
    }

    [Theory]
    [InlineData("ascii", GlyphTier.Ascii)]
    [InlineData("unicode", GlyphTier.Basic)]
    [InlineData("RICH", GlyphTier.Full)]
    [InlineData(null, GlyphTier.Basic)]
    [InlineData("emoji", GlyphTier.Basic)]
    public void ConsoleMode_MapsPowerForgeModeOntoGlyphTier(string? mode, GlyphTier expected)
    {
        var detected = TestTerminal.Profile(glyphs: GlyphTier.Basic);

        Assert.Equal(expected, ConsoleMode.Apply(detected, mode).Glyphs);
    }

    private static string Render(GlyphTier glyphs, Action write)
    {
        var target = TestTerminal.Target(width: 80, glyphs: glyphs);
        using (Terminal.Use(target))
            write();
        return target.Text;
    }

    private static string GoldenPath(string name, [CallerFilePath] string sourceFile = "")
        => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Golden", "ConsoleMonster", name);
}
