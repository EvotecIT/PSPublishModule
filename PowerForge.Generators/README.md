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
| `bool Visible` / `bool IsVisible` (also `bool?`) | `Visible(bool value = true)` (`Visible(bool? value = true)`; Prefixed naming and cmdlet parameters keep `IsVisible` for a `bool?`) |
| `Legend Legend` (a settings type) | `Legend(Action<Legend> configure)`, creating it when null |
| `ICollection<Series> Series` | `Series(item)`, `Series(Action<Series> configure)`, `Series(IEnumerable<Series> items)` |
| `Func<...> LabelFormatter` with `[Fluent("FormatLabels")]` | `FormatLabels(Func<...> value)` |
| `ChartBarMode Mode` with `[FluentPreset("Stacked", ChartBarMode.Stacked)]` | `Stacked()` |
| `[FluentMutator] internal void SetZones(string left, string center = "")` | `Zones(string left, string center = "")`, calling `SetZones` |

- `[Fluent("Name")]` renames a property's method, `[Child("Name")]` renames a collection's single-item methods and
  `[NoFluent]` leaves a property or type out.
- `[FluentForward("Content", typeof(ContentBuilder), "Replace")]` on a base type or interface generates `Content(...)`
  for each static `Replace(owner, value)` overload, for values that must go through a helper.
- `[FluentMutator]` on a public or internal instance method generates a fluent method that calls it, with the same
  parameters and defaults, named after the method without its `Set` prefix (or `[FluentMutator("Name")]`). Use it where a
  value is written through a method rather than a public setter (`SetResolved(bool)` behind a read-only `Resolved`) or
  takes several values at once (`SetZones(left, center, right)`). A single `bool` or `bool?` parameter without a default
  gets `= true`, so `Resolved()` reads like a switch, unless an instance method or another extension already answers
  `Resolved()`; then the first argument stays required (a lone `params` array becomes an array overload plus one taking at
  least one item). An `[Optional]` parameter without a default keeps `[Optional]`,
  so an omitted `object` is still `Type.Missing`. A type whose only fluent members are mutators still counts as a
  settings type, so it can be inlined and configured. A property's setter method with the same signature gives way to
  the mutator, including one an inlined property brings, whatever order they're declared in. Mutators of a base type
  come through to derived types like its properties do; an override with its own `[FluentMutator("Name")]` also gets
  that name. `[FluentInline]` brings the inlined type's mutators to the owner when this assembly can call them; the
  internal mutators of a referenced library aren't visible to it. A static, generic, private or by-reference method,
  a pointer parameter, a parameter type that isn't public (the generated method is), an `[Optional]` COM interface
  parameter, or a default value that can't be repeated in code, is PFF013.
- `[Fluent]` on a struct generates by-value extensions for its `[FluentMutator]` methods (and `[FluentForward]`s), each
  returning the same handle: `cell.Bold().Border("red")` for a cell handle that writes through to its sheet, with no
  wrapper object. A struct's properties get no methods, since setting one on the receiver would change a
  copy.
- A public nested class marked `[Fluent]` itself gets methods in a top-level `<Outer><Inner>Fluent` class
  (`Menu.Item` gets `MenuItemFluent`), and properties of its type get `X(o => ...)`. Unmarked nested types
  still get nothing. A marked nested type inside a generic or non-public type can't have extension methods (PFF014).
- A property whose type is an abstract `[Fluent]` class of the same assembly gets `X(o => ...)`, which configures the
  current value in place and throws when it isn't set, since an abstract type can't be created.
- A read-only property whose getter returns a new object on every read (`=> _options.Clone()`, `=> new Options()`, a
  collection built in the getter or by a method it calls, `ToList()`), and a collection of a read-only type
  (`ReadOnlyCollection<T>`, immutable and frozen collections), gets no in-place `X(o => ...)`, add-item or `[FluentInline]`
  methods: the changes would land on a throwaway object. Every return of the getter must build a new object. Each is reported as PFF015 (info). Getters that return a field, an
  auto-property or a cached value (`_x ??= new X()`, `_x ?? new X()`, a local stored in a field or dictionary before it
  is returned, a cache-or-create helper, a virtual getter or helper a derived type can override) are configured as before, and so are a conditional that builds a new object on
  only one branch and anything the generator can't read (a referenced library, an interface). When the new object does write through, such as a wrapper over the owner's
  state, `[Fluent]` on the property asks for the methods anyway. A settable property keeps `X(o => ...)`, since it assigns
  the configured object back.
- `[FluentInline]` on a settings-type property gives the owner every method of that settings type, applied to the
  property and returning the owner: with `[FluentInline] ChartOptions Options { get; }`, `chart.Legend(l => ...)` and
  `chart.Theme(...)` set `chart.Options.Legend` and `chart.Options.Theme`. `[FluentForward]` methods of the settings type
  come along too, applied to the property. `Options(o => ...)` is still generated. The property must be readable and not
  nullable; one level is inlined (an inlined type's own `[FluentInline]` properties keep only their configure method on
  it).
- `[FluentPreset("Stacked", ChartBarMode.Stacked)]` on a property generates `Stacked()`, which assigns that constant (an
  enum member, bool, number, string or null; an integer that fits also works for `byte`, `short`, `uint` and the other
  integral types, as `byte size = 1` does, and `0` works for an enum, as `Mode mode = 0` does). `[FluentPreset("PlainLine", typeof(LineVisualStyle), nameof(LineVisualStyle.Plain))]`
  assigns the static member instead: a parameterless static method is called, a static property or field is read. A
  `const` field converts like a constant, so `const int Small = 1` also works for a `byte` property.
  Repeat the attribute for several presets; `Summary` replaces the generated documentation. Presets also come through
  `[FluentInline]`.
- `[Fluent(Copy = "Clone")]` on a settings type names a parameterless instance method (public or internal, declared or
  inherited) that returns a copy of the type or of a base it can be cast to. A settable property of that type, or of a
  type deriving from it, then gets a copy-then-commit `X(o => ...)`: the callback edits a copy of the current value (or
  of a new one), and a further copy is assigned only once the callback returns. A throwing callback leaves the owner's
  settings object unchanged, and the instance the callback saw is never the one stored. This covers the top-level settings
  object only: nested reference state (a list, a child object) is shared between the copies unless the copy method clones
  it. Read-only properties are still configured in place.
- A settings type from a referenced library works too: a property of that type gets `X(o => ...)`, and a derived type
  can inline it, so blocks in one package inherit the options of a base class in another. Such a type is configured in
  place, even when it names a copy method, because that method is usually not visible outside its own library. A
  forward overload whose helper is internal to that library goes through the fluent method the library generated for
  that overload, while public overloads call the helper directly; when the library generated none for a name, the
  inlining owner keeps the property's own method instead.
- A delegate property without a verb (PFF001), a method that collides with an existing one, with one a base type that
  can be derived from generates (including through `[FluentInline]`), or that a delegate-valued member would capture
  (PFF002), two members that map to one method (PFF003), a forward target without a usable
  overload (PFF004), a name that is not an identifier (PFF005), a preset whose value cannot be assigned or whose property
  has no public setter (PFF010), a copy method that is missing or has the wrong shape (PFF011) and `[FluentInline]` on a
  property that is not a readable, non-nullable settings type (PFF012) are build errors. A preset named like a bool
  method (`Visible()` beside `Visible(bool value = true)`) is PFF003.
- Methods for a type that can be derived from are generic over `TSelf`, so a chain keeps the derived type. They live in
  a `<Type>Fluent` partial class, where handwritten helpers can sit beside them; a class of that name you declare
  yourself must be `static partial`.
- Unmarked nested types and generic types that can be derived from get no methods; sealed generic types do, with their
  constraints.
- Generated code also compiles as C# 7.3 (the default for netstandard2.0 and .NET Framework projects); nullable
  annotations and `??=` are only emitted for C# 8 and later.

MSBuild settings:

| Property | Meaning |
| --- | --- |
| `PowerForgeFluentNaming=Prefixed` | ConsoleMonster's earlier names (`AddX`, `AddXs`, one `Configure`, `X()` for a bool) in one `<Namespace>Fluent` class; an existing method silently wins. `[FluentInline]`, `[FluentPreset]`, `[FluentMutator]` and the PFF015 checks apply only to the convention. For ConsoleMonster until it adopts the convention. |
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
