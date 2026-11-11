# PowerForge.Generators

Build-time source generators for Evotec libraries: fluent methods on the models, PowerShell cmdlet parameters from the
same models, and the shared `ThemeMode`/`Tone`/`Icon` vocabulary. Reference it as a development dependency; it adds no
runtime assembly.

```xml
<PackageReference Include="PowerForge.Generators" Version="3.0.*" PrivateAssets="all" />
```

## Fluent methods

Mark a model, a base class or an interface with `[PowerForge.Fluent.Fluent]`. Every public concrete type deriving from it,
and every public settings type of the same assembly reachable from their properties, gets extension methods generated
from its public properties. They set the property and return the same instance, so chaining, object initializers and
property access all edit one object.

| Property | Generated |
| --- | --- |
| `T Title { get; set; }` | `Title(T value)` |
| `bool Visible` / `bool IsVisible` | `Visible(bool value = true)` |
| `Legend Legend` (a settings type) | `Legend(Action<Legend> configure)`, creating it when null |
| `ICollection<Series> Series` | `Series(item)`, `Series(Action<Series> configure)`, `Series(IEnumerable<Series> items)` |
| `Func<...> LabelFormatter` with `[Fluent("FormatLabels")]` | `FormatLabels(Func<...> value)` |

- `[Fluent("Name")]` renames a property's method, `[Child("Name")]` renames a collection's single-item methods and
  `[NoFluent]` leaves a property or type out.
- `[FluentForward("Content", typeof(ContentBuilder), "Replace")]` on a base type or interface generates `Content(...)`
  for each static `Replace(owner, value)` overload, for values that must go through a helper.
- A delegate property without a verb (PFF001), a method that collides with an existing one or that a delegate-valued
  member would capture (PFF002), two members that map to one method (PFF003), a forward target without a usable
  overload (PFF004) and a name that is not an identifier (PFF005) are build errors.
- Methods for a type that can be derived from are generic over `TSelf`, so a chain keeps the derived type. They live in
  a `<Type>Fluent` partial class, where handwritten helpers can sit beside them; a class of that name you declare
  yourself must be `static partial`.
- Nested types and generic types that can be derived from get no methods; sealed generic types do, with their
  constraints.
- Generated code also compiles as C# 7.3 (the default for netstandard2.0 and .NET Framework projects); nullable
  annotations and `??=` are only emitted for C# 8 and later.

MSBuild settings:

| Property | Meaning |
| --- | --- |
| `PowerForgeFluentNaming=Prefixed` | ConsoleMonster's earlier names (`AddX`, `AddXs`, one `Configure`, `X()` for a bool) in one `<Namespace>Fluent` class; an existing method silently wins. For ConsoleMonster until it adopts the convention. |
| `PowerForgeFluentFactory=Namespace.Type` | Static factory classes (separate several with `;` or `,`) whose parameterless methods are used in the generated documentation examples. |

## Cmdlet parameters

A PowerShell module marks a partial cmdlet with the model it builds:

```csharp
[FluentCmdlet(typeof(TopologyChart), Exclude = new[] { "Theme" })]
[Cmdlet(VerbsCommon.New, "CfxTopology")]
public sealed partial class NewCfxTopologyCommand : PSCmdlet {
    protected override void ProcessRecord() {
        TopologyChart chart = new TopologyChart();
        ApplyParameters(chart);
        WriteObject(chart);
    }
}
```

Each public property becomes a parameter named like its fluent method, documented with the property's summary. That
summary is C# XML documentation: it reaches `Get-Help` when the module builds its help with PowerForge, which reads the
XML documentation file (`GenerateDocumentationFile`). Type mapping: a bool is a
switch, a settings object takes a hashtable whose keys are its properties (nested hashtables for nested settings; an
unknown key is an error that lists the valid ones), a collection takes an array, and anything else takes the property's
type. `ApplyParameters(model)` copies only the parameters the caller bound, so unbound ones keep the model's defaults;
hashtable values are converted with PowerShell's own rules.

- `Include` and `Exclude` choose the properties; an unknown name is PFF009.
- A `[Parameter]` the cmdlet declares itself replaces the generated one and keeps its validation, completers and help.
  When its type fits the property, `ApplyParameters` still copies it, so an existing cmdlet adopts the attribute without
  losing anything.
- A name PowerShell or the cmdlet base class already uses is PFF006; a cmdlet that is not partial, or a project without a
  PowerShell reference, is PFF007.

### What stays handwritten

These have no single right mapping, so the cmdlet declares them and the generator steps aside:

- parameter sets, `Mandatory`, `Position`, aliases, validation and argument completers (declare the parameter);
- children collected from `-Content {}` or the pipeline, and data projection such as `-XProperty`;
- delegate-valued properties, which would need script blocks;
- values the cmdlet computes from several inputs or from the host, such as `-NoLive` or a terminal profile.

## Shared vocabulary

```xml
<PowerForgeVocabulary>ThemeMode;Tone;Icon</PowerForgeVocabulary>
<PowerForgeVocabularyNamespace>ChartForgeX</PowerForgeVocabularyNamespace>
```

emits the listed types into the library's own namespace (the root namespace when none is set), from one definition, so
every engine spells them alike without a runtime dependency. `ThemeMode` is `Auto`, `Light` or `Dark`. `Tone` is a
built-in emphasis (`Neutral`, `Info`, `Success`, `Warning`, `Danger`, `Muted`) or `Tone.Named("brand")` for one the theme
defines. `Icon` is a catalog key such as `tabler:server`, never a URL or markup. `Tone` and `Icon` parse from strings, so
PowerShell accepts `-Tone warning`. An unknown type name is PFF008. The generated code compiles as C# 7.3.
