# PowerForge.Generators

Build-time source generators for Evotec libraries. Reference it as a development dependency; it adds no runtime assembly.

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
- A delegate property without a verb (PFF001), a method that collides with an existing one (PFF002) and two members that
  map to one method (PFF003) are build errors.
- Methods for a type that can be derived from are generic over `TSelf`, so a chain keeps the derived type.

MSBuild settings:

| Property | Meaning |
| --- | --- |
| `PowerForgeFluentNaming=Prefixed` | ConsoleMonster's earlier names (`AddX`, `AddXs`, one `Configure`, `X()` for a bool); an existing method silently wins. For ConsoleMonster until it adopts the convention. |
| `PowerForgeFluentFactory=Namespace.Type` | A static factory class whose parameterless methods are used in the generated documentation examples. |
