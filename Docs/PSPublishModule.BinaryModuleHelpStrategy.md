# Binary Module Help Strategy

This note captures the current recommendation for binary PowerShell module help inside PSPublishModule / PowerForge after comparing the existing pipeline with `C:\Support\GitHub\XmlDoc2CmdletDoc`.

## Recommendation

PowerForge should replace `XmlDoc2CmdletDoc` for the maintained Evotec / PowerForge use cases, but not by copying it as a second standalone tool.

The better long-term shape is:

- keep one canonical documentation pipeline in `PowerForge`
- keep `PSPublishModule` as the PowerShell-facing configuration surface
- keep authoring on the cmdlet source type plus curated module content
- emit both Markdown and external help XML from the same extracted model
- continue treating about topics as first-class authored content
- extend the current engine with the few binary-cmdlet capabilities that `XmlDoc2CmdletDoc` still does better

`XmlDoc2CmdletDoc` is worth mining for ideas, warning behavior, and XML-doc conventions. It is not a good fit as the long-term runtime dependency because it only solves one output format, only for binary cmdlets, and would duplicate work already done in the PowerForge docs pipeline.

## Canonical authoring model

For C# cmdlets, the source of truth should be:

- cmdlet class XML docs
  - `<summary>`: synopsis
  - `<remarks>` and/or top-level `<para>` blocks: long description
  - `<example>` blocks: authored examples
  - `<seealso>`: related links
  - `<list type="alertSet">`: notes / warnings / caveats
- parameter property / field XML docs
  - `<summary>` / `<remarks>`: parameter description
- referenced input/output CLR type XML docs
  - `<summary>` / `<remarks>`: input/output type descriptions
- curated module content
  - `Help/About/about_*.help.txt|.txt|.md|.markdown`: about topics
  - optional example scripts under `Examples/` for harvested fallback examples and validation

That gives one authoring flow for binary cmdlets while still supporting script and mixed modules through the same pipeline.

## Build outputs

The pipeline should continue to produce:

- Markdown help under `Docs/*.md`
- module help index at `Docs/Readme.md`
- about-topic Markdown under `Docs/About/*.md`
- about-topic index at `Docs/About/README.md`
- external help XML under `<culture>/<ModuleName>-help.xml`

Those outputs should all come from one shared `DocumentationExtractionPayload`, not from separate tools with separate interpretation rules.

## What PowerForge already does well

- imports the staged module and extracts command metadata with `Get-Command` / `Get-Help`
- supports script, binary, and mixed modules in one engine
- enriches binary cmdlets from the assembly XML doc file
- writes Platy-style Markdown help
- writes external help MAML
- converts `about_*` topics to Markdown and indexes them
- generates fallback examples when authored examples are missing
- validates coverage through module validation and website/API-doc preflight flows
- feeds PowerForge.Web PowerShell API docs, including fallback/imported examples

## Gap vs XmlDoc2CmdletDoc

Before this change, the main remaining binary-focused gaps were:

- cmdlet notes from XML docs were not preserved
- input/output type docs were not reliably enriched from CLR XML docs
- extraction relied heavily on `Get-Help` payload richness even when XML docs could provide more structure

`XmlDoc2CmdletDoc` also has a nice strict-warning model and a focused binary-only reflection path. Those are useful patterns to absorb into PowerForge without adopting the package itself.

After scanning the current module repos under `C:\Support\GitHub`, the most relevant real-world remaining gap was the warning model, not extra XML syntax:

- several repos use `-strict`
- some repos explicitly relax strictness with `XmlDoc2CmdletDocStrict=false`
- `ADPlayground.PowerShell` uses `-ignoreOptional`
- no maintained repos in that scan were using `-excludeParameterSets` or `-ignoreMissing`

That made parameter-description and type-description validation the right PowerForge-native follow-up, rather than cloning every old package switch.

## Long-term architecture

Recommended direction:

1. Keep the current hybrid engine as the default.
2. Continue extracting runtime-truth metadata from PowerShell:
   - command names
   - parameter sets
   - aliases
   - pipeline behavior
   - validate-set / enum values
   - common PowerShell help semantics
3. Layer binary XML-doc enrichment on top for:
   - synopsis
   - remarks
   - examples
   - related links
   - notes
   - parameter descriptions
   - input/output type descriptions
4. Add stricter validation knobs for binary cmdlets rather than depending on an external MSBuild hook.
5. Only add a reflection-first extractor when runtime extraction is insufficient for a specific binary scenario.

That means “replace the package” functionally, but with a broader PowerForge-native system rather than a re-hosted clone.

## Windows PowerShell 5.1 vs PowerShell 7+

- External help XML remains the safest common output for both Windows PowerShell 5.1 and PowerShell 7+.
- Markdown help is useful for repo/docs UX, but `Get-Help` compatibility still depends on MAML.
- Runtime extraction should continue to prefer `pwsh` when available for modern modules, but the generated help must stay compatible with Windows PowerShell consumers.
- A future reflection-only path must be careful with target-framework differences. Loading a `net472` binary into a modern .NET host is not always safe, which is another reason the current PowerShell-host extraction model is still valuable.
- One host quirk to account for in tests and expectations: Windows PowerShell 5.1 trims the visible separator between `maml:introduction` and `dev:code` in rendered examples (`PS>Command`), while `pwsh` preserves the expected spacing (`PS> Command`). The MAML is still valid in both hosts, but display assertions should treat this as a host-formatting difference.
